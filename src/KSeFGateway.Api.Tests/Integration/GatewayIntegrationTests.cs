using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using KSeFGateway.Api.Invoice;
using Xunit.Abstractions;

namespace KSeFGateway.Api.Tests.Integration;

/// <summary>
/// Integration tests against a running KSeF Gateway (localhost:8080).
/// Requires gateway running with a valid KSEF_TOKEN + KSEF_NIP, and GATEWAY_API_KEY set to the
/// same value the gateway itself was started with (missing header returns 401; an unconfigured
/// gateway returns 503). A certificate can replace KSEF_TOKEN.
///
/// Run: dotnet test --filter Category=Integration
/// Run in the CI integration job with a generated KSeF TEST token.
/// </summary>
[Trait("Category", "Integration")]
public class GatewayIntegrationTests(GatewayIntegrationTests.SharedInvoiceFixture fixture, ITestOutputHelper output)
    : IClassFixture<GatewayIntegrationTests.SharedInvoiceFixture>
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

    /// <summary>Asserts HTTP 200 and, on failure, shows the response body (KSeF rejections explain themselves there).</summary>
    internal static async Task AssertOkAsync(HttpResponseMessage resp, Func<DateTimeOffset>? utcNow = null)
    {
        if (resp.StatusCode == HttpStatusCode.OK) return;
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var unblock = resp.Headers.RetryAfter?.Date
                ?? (resp.Headers.RetryAfter?.Delta is { } delta ? (utcNow ?? (() => DateTimeOffset.UtcNow))() + delta : (DateTimeOffset?)null);
            var diagnostic = unblock is { } time
                ? $"limit KSeF TEST wyczerpany, spróbuj po {time:HH:mm} UTC"
                : "limit KSeF TEST wyczerpany, brak Retry-After; sprawdź status przed ponowieniem";
            Assert.Fail($"{diagnostic}; HTTP 429: {await resp.Content.ReadAsStringAsync()}");
        }
        Assert.Fail($"Expected 200 OK but got {(int)resp.StatusCode} {resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
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

        await AssertOkAsync(resp);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task SendFriendlyJson_TwoThreeGroszLines_DownloadsConsistentTotals()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/VAT")) with
        {
            Items = [
                new InvoiceItem { Name = "Grosz A", Quantity = 1, UnitPrice = 0.03m, VatRate = 23 },
                new InvoiceItem { Name = "Grosz B", Quantity = 1, UnitPrice = 0.03m, VatRate = 23 }
            ]
        };
        using var response = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
        var number = body.GetProperty("data").GetProperty("ksefNumber").GetString();
        Assert.False(string.IsNullOrEmpty(number));

        // One download, no metadata queries or polling: preserve the shared TEST quota.
        using var download = await Http.GetAsync($"{BaseUrl}/ksef/invoice/{number}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        XNamespace ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";
        var fa = XDocument.Parse(await download.Content.ReadAsStringAsync()).Root!.Element(ns + "Fa")!;
        Assert.Equal(0.06m, (decimal)fa.Element(ns + "P_13_1")!);
        Assert.Equal(0.01m, (decimal)fa.Element(ns + "P_14_1")!);
        Assert.Equal(0.07m, (decimal)fa.Element(ns + "P_15")!);
    }

    [Fact]
    public async Task SendFriendlyJson_DomesticZeroVat_IsAccepted()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/ZERO")) with
        {
            Items = [new InvoiceItem { Name = "Domestic zero VAT", Quantity = 1, UnitPrice = 100m, VatRate = 0 }]
        };
        // Acceptance is returned by the send flow; no extra status/download calls needed.
        using var response = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
    }

    [Fact]
    public async Task SendFriendlyJson_Exempt_IsAccepted()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/ZW")) with
        {
            ExemptionBasis = "art. 113 ust. 1 ustawy o VAT",
            Items = [new InvoiceItem { Name = "Exempt service", Quantity = 1, UnitPrice = 100m, VatRate = "zw" }]
        };
        using var response = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
    }

    [Fact]
    public async Task SendFriendlyJson_DecreasingCorrection_IsAccepted()
    {
        // One send only: reference an outside-KSeF invoice, without creating another invoice.
        var invoice = SampleInvoice(UniqueNumber("KOR/INT")) with
        {
            Type = "KOR", CorrectionReason = "Price correction",
            CorrectedInvoice = new CorrectedInvoiceData { IssueDate = "2026-03-01", InvoiceNumber = "OUTSIDE/1", OutsideKsef = true },
            Items = [new InvoiceItem { Name = "Service price difference", Quantity = 1, UnitPrice = -10m, VatRate = 23 }],
            Payment = null
        };
        using var response = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
    }

    [Fact]
    [Trait("Cadence", "Nightly")]
    public async Task SendFriendlyJson_EuBuyerNpII_DownloadsEuIdentityAndTotals()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/EU")) with
        {
            Buyer = ForeignBuyer("DE", euVatNumber: "DE123456789"),
            Items = [new InvoiceItem { Name = "EU service", Quantity = 1, UnitPrice = 100m, VatRate = "np II" }]
        };
        var xml = await SendFeatureInvoiceAsync(invoice);
        var identity = xml.Root!.Element(InvoiceNs + "Podmiot2")!.Element(InvoiceNs + "DaneIdentyfikacyjne")!;
        Assert.Equal("DE", identity.Element(InvoiceNs + "KodUE")?.Value);
        Assert.Equal("123456789", identity.Element(InvoiceNs + "NrVatUE")?.Value);
        var fa = xml.Root.Element(InvoiceNs + "Fa")!;
        Assert.Equal(100m, (decimal)fa.Element(InvoiceNs + "P_13_9")!);
        Assert.Equal(100m, (decimal)fa.Element(InvoiceNs + "P_15")!);
        Assert.Equal("np II", fa.Element(InvoiceNs + "FaWiersz")!.Element(InvoiceNs + "P_12")?.Value);
    }

    [Fact]
    [Trait("Cadence", "Nightly")]
    public async Task SendFriendlyJson_NonEuBuyerNpI_DownloadsTaxIdentityAndTotals()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/US")) with
        {
            Buyer = ForeignBuyer("US", taxId: "123456789"),
            Items = [new InvoiceItem { Name = "US service", Quantity = 1, UnitPrice = 100m, VatRate = "np I" }]
        };
        var xml = await SendFeatureInvoiceAsync(invoice);
        var identity = xml.Root!.Element(InvoiceNs + "Podmiot2")!.Element(InvoiceNs + "DaneIdentyfikacyjne")!;
        Assert.Equal("US", identity.Element(InvoiceNs + "KodKraju")?.Value);
        Assert.Equal("123456789", identity.Element(InvoiceNs + "NrID")?.Value);
        var fa = xml.Root.Element(InvoiceNs + "Fa")!;
        Assert.Equal(100m, (decimal)fa.Element(InvoiceNs + "P_13_8")!);
        Assert.Equal(100m, (decimal)fa.Element(InvoiceNs + "P_15")!);
        Assert.Equal("np I", fa.Element(InvoiceNs + "FaWiersz")!.Element(InvoiceNs + "P_12")?.Value);
    }

    [Fact]
    [Trait("Cadence", "Nightly")]
    public async Task SendFriendlyJson_LineDiscount_DownloadsDiscountedLineAndTotals()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/DISCOUNT")) with
        {
            Items = [new InvoiceItem { Name = "Discounted service", Quantity = 3, UnitPrice = 33.33m, Discount = 0.10m, VatRate = 23 }]
        };
        var xml = await SendFeatureInvoiceAsync(invoice);
        var fa = xml.Root!.Element(InvoiceNs + "Fa")!;
        var line = fa.Element(InvoiceNs + "FaWiersz")!;
        // Independent arithmetic, without InvoiceCalculator/InvoiceXmlBuilder as the oracle.
        var net = decimal.Round(3m * 33.33m - 0.10m, 2, MidpointRounding.AwayFromZero);
        var vat = decimal.Round(net * 0.23m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(0.10m, (decimal)line.Element(InvoiceNs + "P_10")!);
        Assert.Equal(net, (decimal)line.Element(InvoiceNs + "P_11")!);
        Assert.Equal(net, (decimal)fa.Element(InvoiceNs + "P_13_1")!);
        Assert.Equal(vat, (decimal)fa.Element(InvoiceNs + "P_14_1")!);
        Assert.Equal(net + vat, (decimal)fa.Element(InvoiceNs + "P_15")!);
    }

    [Fact]
    [Trait("Cadence", "Nightly")]
    public async Task SendFriendlyJson_EuBuyerWdt_DownloadsWdtSummary()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/WDT")) with
        {
            Buyer = ForeignBuyer("DE", euVatNumber: "DE123456789"),
            Items = [new InvoiceItem { Name = "Goods delivered to Germany", Quantity = 1, UnitPrice = 100m, VatRate = "0 WDT" }]
        };
        var xml = await SendFeatureInvoiceAsync(invoice);
        var fa = xml.Root!.Element(InvoiceNs + "Fa")!;
        Assert.Equal(100m, (decimal)fa.Element(InvoiceNs + "P_13_6_2")!);
        Assert.Equal(100m, (decimal)fa.Element(InvoiceNs + "P_15")!);
        Assert.Equal("0 WDT", fa.Element(InvoiceNs + "FaWiersz")!.Element(InvoiceNs + "P_12")?.Value);
    }

    private static readonly XNamespace InvoiceNs = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static BuyerData ForeignBuyer(string country, string? euVatNumber = null, string? taxId = null) => new()
    {
        EuVatNumber = euVatNumber, TaxId = taxId, Name = "Integration foreign buyer",
        Address = new AddressData { Country = country, Street = "Test Street 1", City = "Test City" }
    };

    private async Task<XDocument> SendFeatureInvoiceAsync(InvoiceRequest invoice)
    {
        // Refuse live sends unless the gateway explicitly confirms the TEST environment.
        using var status = await Http.GetAsync($"{BaseUrl}/ksef/status");
        await AssertOkAsync(status);
        var system = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TEST", system.GetProperty("data").GetProperty("environment").GetString());

        // Exactly one POST/session per scenario; never retry a send, including on 429.
        using var response = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
        var text = await response.Content.ReadAsStringAsync();
        output.WriteLine($"{invoice.InvoiceNumber}: HTTP {(int)response.StatusCode}\n{text}");
        await AssertOkAsync(response);
        var body = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.True(body.GetProperty("success").GetBoolean(), text);
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
        var number = body.GetProperty("data").GetProperty("ksefNumber").GetString();
        Assert.False(string.IsNullOrWhiteSpace(number), text);

        // Only retry lookup's 404 indexing lag; do not poll errors or metadata.
        using var download = await RetryUntilAsync(
            () => Http.GetAsync($"{BaseUrl}/ksef/invoice/{number}"),
            r => r.StatusCode != HttpStatusCode.NotFound);
        await AssertOkAsync(download);
        return XDocument.Parse(await download.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendRawXml_ReturnsKsefNumber()
    {
        var invoice = SampleInvoice(UniqueNumber("FV/INT/XML"));
        var xml = InvoiceXmlBuilder.Build(invoice);
        var resp = await Http.PostAsync($"{BaseUrl}/ksef/send",
            new StringContent(xml, Encoding.UTF8, "application/xml"));

        await AssertOkAsync(resp);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
    }

    [Fact]
    public async Task SendXmlJsJson_ReturnsKsefNumber()
    {
        // Regression: POST /ksef/send/json used to call the gateway's own /ksef/send over HTTP
        // without X-Api-Key, so the API-key middleware answered 401 on every request.
        var xml = InvoiceXmlBuilder.Build(SampleInvoice(UniqueNumber("FV/INT/XMLJS")));
        var xmlJs = new JsonObject { [XDocument.Parse(xml).Root!.Name.LocalName] = ToXmlJs(XDocument.Parse(xml).Root!) };

        var resp = await Http.PostAsync($"{BaseUrl}/ksef/send/json",
            new StringContent(xmlJs.ToJsonString(), Encoding.UTF8, "application/json"));

        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        await AssertOkAsync(resp);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("ksefNumber").GetString()));
    }

    /// <summary>Minimal XML to xml-js "compact" converter (_attributes/_text, repeated elements become arrays).</summary>
    private static JsonObject ToXmlJs(XElement el)
    {
        var obj = new JsonObject();
        var attrs = el.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        if (attrs.Count > 0)
        {
            var a = new JsonObject();
            foreach (var at in attrs) a[at.Name.LocalName] = at.Value;
            obj["_attributes"] = a;
        }
        foreach (var group in el.Elements().GroupBy(e => e.Name.LocalName))
        {
            var items = group.Select(ToXmlJs).ToList();
            obj[group.Key] = items.Count == 1 ? items[0] : new JsonArray(items.Select(i => (JsonNode)i).ToArray());
        }
        if (!el.HasElements && !string.IsNullOrEmpty(el.Value)) obj["_text"] = el.Value;
        return obj;
    }

    [Fact]
    public async Task GetInvoiceXml_AfterSend_ReturnsXml()
    {
        var ksefNumber = (await fixture.Invoice.Value).KsefNumber;
        var resp = await RetryUntilAsync(
            () => Http.GetAsync($"{BaseUrl}/ksef/invoice/{ksefNumber}"),
            r => r.StatusCode == HttpStatusCode.OK);

        await AssertOkAsync(resp);
        var xml = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<Faktura", xml);
        Assert.Contains("http://crd.gov.pl/wzor/2025/06/25/13775/", xml);
    }

    [Fact]
    public async Task GetInvoicePdf_AfterSend_ReturnsPdf()
    {
        var ksefNumber = (await fixture.Invoice.Value).KsefNumber;
        var resp = await RetryUntilAsync(
            () => Http.GetAsync($"{BaseUrl}/ksef/invoice/{ksefNumber}/pdf"),
            r => r.StatusCode == HttpStatusCode.OK);

        await AssertOkAsync(resp);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000, $"PDF suspiciously small: {bytes.Length} bytes");
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public async Task ListReceivedInvoices_AfterSelfInvoice_FindsItAsBuyer()
    {
        var shared = await fixture.Invoice.Value;
        Assert.Contains(shared.InvoiceNumber, await fixture.Received.Value);
    }

    [Fact]
    public async Task ListIssuedInvoices_AfterSend_FindsItAsSeller()
    {
        var shared = await fixture.Invoice.Value;
        Assert.Contains(shared.InvoiceNumber, await fixture.Issued.Value);
    }

    [Fact]
    public async Task ListNewReceivedInvoices_SinceNow_ReturnsEmptyWithCheckpoint()
    {
        // "since" this close to real time is always later than KSeF's own
        // PermanentStorageHwmDate (data isn't durably complete there yet) - exercises the
        // gateway's graceful handling of KSeF's 21183 rejection (nothing new yet, not an error).
        var since = DateTimeOffset.UtcNow.ToString("O");
        using var resp = await MetadataQuery.RequestAsync(() => Http.GetAsync($"{BaseUrl}/ksef/invoices/received/new?since={Uri.EscapeDataString(since)}"));

        await AssertOkAsync(resp);
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

        await AssertOkAsync(resp);
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

    public sealed record SharedInvoice(string InvoiceNumber, string KsefNumber, string From, string To);

    // xUnit creates one fixture per class. Lazy tasks are shared even if test order changes.
    public sealed class SharedInvoiceFixture
    {
        public Lazy<Task<SharedInvoice>> Invoice { get; }
        private readonly Func<string, Task<HttpResponseMessage>> request;
        public Lazy<Task<List<string?>>> Received { get; }
        public Lazy<Task<List<string?>>> Issued { get; }

        public SharedInvoiceFixture() : this(SendSharedInvoice, url => Http.GetAsync(url)) { }

        internal SharedInvoiceFixture(Func<Task<SharedInvoice>> send, Func<string, Task<HttpResponseMessage>> request)
        {
            Invoice = new(send);
            this.request = request;
            Received = new(() => Query("received"));
            Issued = new(() => Query("issued"));
        }

        private async Task<List<string?>> Query(string role)
        {
            var shared = await Invoice.Value;
            return await MetadataQuery.ReadAsync(() => request(
                $"{BaseUrl}/ksef/invoices/{role}?from={Uri.EscapeDataString(shared.From)}&to={Uri.EscapeDataString(shared.To)}"));
        }

        private static async Task<SharedInvoice> SendSharedInvoice()
        {
            var now = DateTimeOffset.UtcNow;
            var today = now.ToString("yyyy-MM-dd");
            var invoice = SampleInvoice(UniqueNumber("FV/INT/SHARED")) with
            {
                IssueDate = today, SaleDate = today,
                Buyer = new BuyerData { Nip = SellerNip, Name = "Integration Test Seller",
                    Address = new AddressData { Street = "ul. Testowa 1", City = "00-001 Warszawa" } }
            };
            using var response = await Http.PostAsJsonAsync($"{BaseUrl}/ksef/invoice", invoice);
            await AssertOkAsync(response);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("success").GetBoolean());
            Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
            var number = body.GetProperty("data").GetProperty("ksefNumber").GetString();
            Assert.False(string.IsNullOrEmpty(number));
            // Give TEST indexing time once, without spending metadata query permits polling.
            await Task.Delay(TimeSpan.FromSeconds(20));
            return new(invoice.InvoiceNumber, number!, now.AddDays(-1).ToString("O"), now.AddDays(1).ToString("O"));
        }
    }

    [Fact]
    public async Task PublishedImage_HealthStatusSendAndPdf()
    {
        using var health = await Http.GetAsync($"{BaseUrl}/health");
        await AssertOkAsync(health);
        var liveness = await health.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", liveness.GetProperty("status").GetString());
        // The shared send waits for authentication; the status check then verifies it.
        var shared = await fixture.Invoice.Value;
        using var status = await Http.GetAsync($"{BaseUrl}/ksef/status");
        await AssertOkAsync(status);
        var system = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TEST", system.GetProperty("data").GetProperty("environment").GetString());
        Assert.Contains(system.GetProperty("data").GetProperty("contexts").EnumerateArray(),
            c => c.GetProperty("authenticated").GetBoolean());
        using var pdf = await Http.GetAsync($"{BaseUrl}/ksef/invoice/{shared.KsefNumber}/pdf");
        await AssertOkAsync(pdf);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
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

}
