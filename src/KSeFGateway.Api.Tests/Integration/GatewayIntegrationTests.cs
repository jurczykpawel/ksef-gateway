using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Integration;

/// <summary>
/// Integration tests against a running KSeF Gateway (localhost:8080).
/// Requires gateway running with a valid KSEF_TOKEN + KSEF_NIP, and GATEWAY_API_KEY set to the
/// same value the gateway itself was started with (falls back to no header if unset, which
/// only works against a gateway that also has no GATEWAY_API_KEY configured).
///
/// Run: dotnet test --filter Category=Integration
/// Not run in CI (no token available).
/// </summary>
[Trait("Category", "Integration")]
public class GatewayIntegrationTests
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://localhost:8080";

    private static string? ApiKey => Environment.GetEnvironmentVariable("GATEWAY_API_KEY");

    private static readonly HttpClient Http = CreateHttpClient(includeApiKey: true);

    private static HttpClient CreateHttpClient(bool includeApiKey)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        if (includeApiKey && !string.IsNullOrEmpty(ApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    private static string SellerNip =>
        Environment.GetEnvironmentVariable("KSEF_NIP") ?? "9124229327";

    private static InvoiceRequest SampleInvoice(string number) => new()
    {
        InvoiceNumber = number,
        IssueDate = "2026-03-25",
        SaleDate = "2026-03-25",
        IssuePlace = "Warszawa",
        Currency = "PLN",
        Type = "VAT",
        Seller = new SellerData
        {
            Nip = SellerNip,
            Name = "Integration Test Seller sp. z o.o.",
            Address = new AddressData { Street = "ul. Testowa 1", City = "00-001 Warszawa" }
        },
        Buyer = new BuyerData
        {
            Nip = "5265877635",
            Name = "Integration Test Buyer sp. z o.o.",
            Address = new AddressData { Street = "ul. Kupiecka 2", City = "00-002 Kraków" }
        },
        Items = [new InvoiceItem { Name = "Usługa testowa", Quantity = 1, UnitPrice = 100m, VatRate = 23 }],
        Payment = new PaymentData { Paid = true, Date = "2026-03-25", Method = "transfer" }
    };

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendFriendlyJson_ReturnsKsefNumber()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/JSON"));
        var resp = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task SendRawXml_ReturnsKsefNumber()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/XML"));
        var xml = InvoiceXmlBuilder.Build(invoice);
        var resp = await Http.PostAsync($"{BaseUrl}/ksef/send",
            new StringContent(xml, Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
    }

    [Fact]
    public async Task GetInvoiceXml_AfterSend_ReturnsXml()
    {
        var ksefNumber = await SendAndGetKsefNumber();
        var resp = await RetryUntilAsync(
            () => Http.GetAsync($"{BaseUrl}/ksef/invoice/{ksefNumber}"),
            r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var xml = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<Faktura", xml);
        Assert.Contains("http://crd.gov.pl/wzor/2025/06/25/13775/", xml);
    }

    [Fact]
    public async Task GetInvoicePdf_AfterSend_ReturnsPdf()
    {
        var ksefNumber = await SendAndGetKsefNumber();
        var resp = await RetryUntilAsync(
            () => Http.GetAsync($"{BaseUrl}/ksef/invoice/{ksefNumber}/pdf"),
            r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000, $"PDF suspiciously small: {bytes.Length} bytes");
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public async Task ListReceivedInvoices_AfterSelfInvoice_FindsItAsBuyer()
    {
        // Self-invoice (seller == buyer == our only authenticated test NIP) is the only way
        // to reliably exercise the buyer-role query with a single-NIP CI credential.
        // IssueDate must be today - the query below searches by today's date, but
        // SampleInvoice() defaults to a fixed historical date for XML/XSD tests.
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var invoiceNumber = UniqueNumber("FV/INT/RECEIVED");
        var invoice = SampleInvoice(invoiceNumber) with
        {
            IssueDate = today,
            SaleDate = today,
            Buyer = new BuyerData
            {
                Nip = SellerNip,
                Name = "Integration Test Seller sp. z o.o.",
                Address = new AddressData { Street = "ul. Testowa 1", City = "00-001 Warszawa" }
            }
        };
        var sendResp = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        sendResp.EnsureSuccessStatusCode();

        var from = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var invoiceNumbers = await PollForInvoiceNumbersAsync(
            $"{BaseUrl}/ksef/invoices/received?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}",
            invoiceNumber);

        Assert.Contains(invoiceNumber, invoiceNumbers);
    }

    [Fact]
    public async Task ListIssuedInvoices_AfterSend_FindsItAsSeller()
    {
        // Any invoice we send has our own authenticated NIP as the seller - no self-invoice
        // trick needed here (unlike the buyer-role test above).
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var invoiceNumber = UniqueNumber("FV/INT/ISSUED");
        var invoice = SampleInvoice(invoiceNumber) with { IssueDate = today, SaleDate = today };
        var sendResp = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        sendResp.EnsureSuccessStatusCode();

        var from = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var invoiceNumbers = await PollForInvoiceNumbersAsync(
            $"{BaseUrl}/ksef/invoices/issued?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}",
            invoiceNumber);

        Assert.Contains(invoiceNumber, invoiceNumbers);
    }

    [Fact]
    public async Task ListNewReceivedInvoices_SinceNow_ReturnsEmptyWithCheckpoint()
    {
        // "since" this close to real time is always later than KSeF's own
        // PermanentStorageHwmDate (data isn't durably complete there yet) - exercises the
        // gateway's graceful handling of KSeF's 21183 rejection (nothing new yet, not an error).
        var since = DateTimeOffset.UtcNow.ToString("O");
        var resp = await Http.GetAsync($"{BaseUrl}/ksef/invoices/received/new?since={Uri.EscapeDataString(since)}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Empty(body.GetProperty("data").GetProperty("invoices").EnumerateArray());
        Assert.True(body.GetProperty("data").TryGetProperty("nextSince", out _));
    }

    // ── API key protection ───────────────────────────────────────────────────

    [Fact]
    public async Task Status_WithoutApiKey_Returns401()
    {
        using var client = CreateHttpClient(includeApiKey: false);
        var resp = await client.GetAsync($"{BaseUrl}/ksef/status");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Status_WithWrongApiKey_Returns401()
    {
        using var client = CreateHttpClient(includeApiKey: false);
        client.DefaultRequestHeaders.Add("X-Api-Key", "definitely-wrong");
        var resp = await client.GetAsync($"{BaseUrl}/ksef/status");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Health_WithoutApiKey_StillReturns200()
    {
        using var client = CreateHttpClient(includeApiKey: false);
        var resp = await client.GetAsync($"{BaseUrl}/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Error paths ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SendFriendlyJson_EmptyBody_Returns400()
    {
        var resp = await Http.PostAsync($"{BaseUrl}/ksef/invoice",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task SendRawXml_EmptyBody_Returns400()
    {
        var resp = await Http.PostAsync($"{BaseUrl}/ksef/send",
            new StringContent("", Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SendRawXml_InvalidXml_Returns400()
    {
        var resp = await Http.PostAsync($"{BaseUrl}/ksef/send",
            new StringContent("this is not xml", Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task GetInvoiceXml_NonExistentKsefNumber_Returns502WithKsefError()
    {
        // Use authenticated NIP in header so gateway can reach KSeF (which then returns 404/error)
        var fakeNumber = $"{SellerNip}-20260325-AAAAAAAAAA-99";
        var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/ksef/invoice/{fakeNumber}");
        req.Headers.Add("X-KSeF-NIP", SellerNip);
        var resp = await Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task GetInvoicePdf_NonExistentKsefNumber_ReturnsError()
    {
        var fakeNumber = "9999999999-20260325-AAAAAAAAAA-99";
        var resp = await Http.GetAsync($"{BaseUrl}/ksef/invoice/{fakeNumber}/pdf");

        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string UniqueNumber(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpper();
        return $"{prefix}/{suffix}";
    }

    private static async Task<string> SendAndGetKsefNumber()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/E2E"));
        var resp = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("ksefNumber").GetString()
            ?? throw new InvalidOperationException("No ksefNumber in response");
    }

    /// <summary>
    /// KSeF's own indexing (query/metadata, invoice-by-number lookup) lags a send by anywhere
    /// from under a second to several seconds - polls until <paramref name="isReady"/> passes or
    /// attempts run out, returning whatever the last attempt produced either way so the caller's
    /// own asserts still fail with a real diagnostic message instead of a bare timeout.
    /// </summary>
    private static async Task<T> RetryUntilAsync<T>(
        Func<Task<T>> action, Func<T, bool> isReady, int maxAttempts = 8, TimeSpan? delay = null)
    {
        var interval = delay ?? TimeSpan.FromSeconds(1.5);
        var result = await action();
        for (var attempt = 1; attempt < maxAttempts && !isReady(result); attempt++)
        {
            await Task.Delay(interval);
            result = await action();
        }
        return result;
    }

    /// <summary>
    /// Polls a /ksef/invoices/{received,issued} URL until targetInvoiceNumber shows up. This
    /// endpoint shares KSeF's own tight invoices/query budget (20/hour, real server-side quota) -
    /// every poll attempt spends from that same scarce pool, so this favors fewer attempts spaced
    /// further apart over more, tighter ones: the same ~20s of wall-clock coverage for eventual
    /// consistency, at half the request cost. A short 429 (the gateway's own proactive throttle)
    /// means "back off exactly as long as told, then keep polling". A long 429 means the real
    /// per-hour account quota is exhausted - that won't clear before the test times out anyway, so
    /// fail fast with a clear diagnostic instead of silently blocking a CI run for up to an hour.
    /// </summary>
    private static readonly TimeSpan MaxWorthwhileRetryAfter = TimeSpan.FromSeconds(15);

    private static async Task<List<string?>> PollForInvoiceNumbersAsync(string url, string targetInvoiceNumber, int maxAttempts = 6)
    {
        var invoiceNumbers = new List<string?>();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var resp = await Http.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = resp.Headers.RetryAfter?.Delta;
                if (retryAfter is null || retryAfter > MaxWorthwhileRetryAfter)
                {
                    var errorBody = await resp.Content.ReadAsStringAsync();
                    throw new InvalidOperationException(
                        $"Hit KSeF's real rate limit polling {url} - Retry-After is " +
                        $"{(retryAfter?.ToString() ?? "unset")}, too long to wait out in a test run. " +
                        $"Response: {errorBody}");
                }
                await Task.Delay(retryAfter.Value + TimeSpan.FromSeconds(1));
                continue;
            }

            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            invoiceNumbers = body.GetProperty("data").GetProperty("invoices")
                .EnumerateArray()
                .Select(i => i.GetProperty("invoiceNumber").GetString())
                .ToList();

            if (invoiceNumbers.Contains(targetInvoiceNumber) || attempt == maxAttempts)
                return invoiceNumbers;

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        return invoiceNumbers;
    }
}
