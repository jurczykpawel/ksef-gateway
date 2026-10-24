using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Authorization;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Discovery;
using KSeFGateway.Api.Licensing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KSeFGateway.Api.Tests.Endpoints;

public class HostRegressionTests
{
    // Each explicit route must also have a row in CONTRIBUTING.md.
    private static readonly string[] ExplicitRoutes = [
        "/health", "/ksef/status", "/ksef/contexts", "/ksef/send", "/ksef/send/json",
        "/ksef/invoice", "/ksef/invoice/{ksefNumber}", "/ksef/invoice/{ksefNumber}/pdf",
        "/ksef/invoices/received", "/ksef/invoices/received/new", "/ksef/invoices/issued",
        "/openapi/{documentName}.json", "/scalar/{documentName?}", "/scalar/favicon.svg", "/scalar/scalar.aspnetcore.js", "/scalar/scalar.js"
    ];

    [Fact]
    public void EveryMappedRoute_HasCoverageEntry()
    {
        using var host = new GatewayHost();
        using var http = host.CreateClient();
        var sdk = SdkEndpointPolicy.Select(SdkReflector.DiscoverEndpoints(), null).Select(e => e.Route).ToHashSet();
        var actual = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText!).Distinct().ToArray();
        Assert.True(ExplicitRoutes.Order().SequenceEqual(actual.Where(r => !sdk.Contains(r)).Order()),
            "Explicit routes: " + string.Join(", ", actual.Where(r => !sdk.Contains(r)).Order()));
        Assert.Empty(sdk.Except(actual));
        var docs = "/docs/CONTRIBUTING.md";
        if (!File.Exists(docs))
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "CONTRIBUTING.md"))) root = root.Parent;
            docs = Path.Combine(root?.FullName ?? throw new DirectoryNotFoundException("Mount CONTRIBUTING.md"), "CONTRIBUTING.md");
        }
        var matrix = File.ReadAllText(docs);
        foreach (var route in actual) Assert.Contains($"| `{route}` |", matrix);
    }

    [Fact]
    public async Task Health_HasOnlyPublicLivenessField()
    {
        using var host = new GatewayHost();
        using var http = host.CreateClient();
        using var response = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Fields(body, "status");
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("/ksef/status")]
    [InlineData("/ksef/contexts")]
    public async Task SystemContracts_ExposeTwoContextsWithoutSecrets(string path)
    {
        using var host = new GatewayHost();
        using var http = host.Client();
        // Authenticate both fake contexts, exactly as request handlers do.
        var pool = host.Services.GetRequiredService<TokenPool>();
        await pool.GetAccessTokenAsync("1111111111");
        await pool.GetAccessTokenAsync("2222222222");
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Fields(body, "success", "data", "error");
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("error").ValueKind);
        var data = body.GetProperty("data");
        if (path == "/ksef/status")
        {
            Fields(data, "discoveredEndpoints", "exposedEndpoints", "mode", "environment", "contexts", "license");
            Assert.Equal(SdkReflector.DiscoverEndpoints().Count, data.GetProperty("discoveredEndpoints").GetInt32());
            Assert.Equal(SdkEndpointPolicy.Select(SdkReflector.DiscoverEndpoints(), null).Count, data.GetProperty("exposedEndpoints").GetInt32());
            Assert.Equal("TEST", data.GetProperty("environment").GetString());
            Assert.Equal("multi-nip", data.GetProperty("mode").GetString());
            var license = data.GetProperty("license");
            Fields(license, "licensed", "maxNips", "activeNips", "email", "expiresAt");
            Assert.True(license.GetProperty("licensed").GetBoolean());
            Assert.Equal(2, license.GetProperty("activeNips").GetInt32());
            Assert.Equal(JsonValueKind.Null, license.GetProperty("maxNips").ValueKind);
            Assert.Equal("test@example.invalid", license.GetProperty("email").GetString());
            Assert.Equal(JsonValueKind.Null, license.GetProperty("expiresAt").ValueKind);
            data = data.GetProperty("contexts");
        }
        Assert.Equal(2, data.GetArrayLength());
        for (var i = 0; i < 2; i++)
        {
            var ctx = data[i];
            Fields(ctx, "nip", "label", "authenticated", "tokenExpiresAt", "isDefault");
            Assert.Equal(i == 0 ? "1111111111" : "2222222222", ctx.GetProperty("nip").GetString());
            Assert.Equal(i == 0 ? "A" : "B", ctx.GetProperty("label").GetString());
            Assert.True(ctx.GetProperty("authenticated").GetBoolean());
            Assert.Equal(DateTimeOffset.Parse("9999-01-01T00:00:00Z"), ctx.GetProperty("tokenExpiresAt").GetDateTimeOffset());
            Assert.Equal(i == 0, ctx.GetProperty("isDefault").GetBoolean());
        }
    }

    [Fact]
    public async Task FreeLicense_HostShowsOneContextAndRejectsSecondNip()
    {
        using var host = new GatewayHost(licensed: false);
        using var http = host.Client();
        var status = await http.GetFromJsonAsync<JsonElement>("/ksef/status");
        var license = status.GetProperty("data").GetProperty("license");
        Assert.False(license.GetProperty("licensed").GetBoolean());
        Assert.Equal(1, license.GetProperty("maxNips").GetInt32());
        Assert.Equal(1, license.GetProperty("activeNips").GetInt32());
        var contexts = await http.GetFromJsonAsync<JsonElement>("/ksef/contexts");
        var active = Assert.Single(contexts.GetProperty("data").EnumerateArray());
        Assert.Equal("1111111111", active.GetProperty("nip").GetString());
        http.DefaultRequestHeaders.Add("X-KSeF-NIP", "2222222222");
        using var response = await http.GetAsync("/ksef/invoice/invoice");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ErrorEnvelope(response);
        Assert.Contains("license", body.GetProperty("error").GetString());
        host.Sdk.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "1111111111")]
    [InlineData("2222222222", "2222222222")]
    public async Task TwoContexts_HeaderAndDefault_SelectCorrectToken(string? header, string expected)
    {
        using var host = new GatewayHost();
        host.Sdk.Setup(x => x.GetInvoiceAsync("invoice", "access-" + expected, It.IsAny<CancellationToken>())).ReturnsAsync("<Faktura/>");
        using var http = host.Client();
        if (header != null) http.DefaultRequestHeaders.Add("X-KSeF-NIP", header);
        using var response = await http.GetAsync("/ksef/invoice/invoice");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<Faktura/>", await response.Content.ReadAsStringAsync());
        host.Sdk.VerifyAll();
        host.Sdk.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("9999999999", 404)]
    [InlineData("bad", 400)]
    public async Task InvalidContext_HasErrorEnvelopeWithoutSdk(string nip, int expected)
    {
        using var host = new GatewayHost();
        using var http = host.Client();
        http.DefaultRequestHeaders.Add("X-KSeF-NIP", nip);
        using var response = await http.GetAsync("/ksef/invoice/invoice");
        Assert.Equal(expected, (int)response.StatusCode);
        await ErrorEnvelope(response);
        host.Sdk.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DownloadCancellation_ReachesSdkWithout500()
    {
        using var host = new GatewayHost();
        using var http = host.Client();
        using var abort = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Sdk.Setup(x => x.GetInvoiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string number, string token, CancellationToken ct) => {
                Assert.True(ct.CanBeCanceled);
                entered.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { cancelled.SetResult(); throw; }
                return "unreachable";
            });
        var request = http.GetAsync("/ksef/invoice/invoice", abort.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("rate", 429, "42")]
    [InlineData("circuit", 503, "17")]
    [InlineData("api", 502, null)]
    [InlineData("internal", 500, null)]
    public async Task SdkFailure_ThroughHandler_HasHttpContract(string kind, int status, string? retry)
    {
        using var host = new GatewayHost();
        Exception error = kind switch {
            "rate" => new KsefRateLimitException("limited", 42, null, null!),
            "circuit" => new KsefCircuitBreakerOpenException("circuit open", TimeSpan.FromSeconds(17)),
            "api" => new KsefApiException("upstream failed", HttpStatusCode.InternalServerError, null!, null!),
            _ => new InvalidOperationException("private detail")
        };
        host.Sdk.Setup(x => x.GetInvoiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);
        using var http = host.Client();
        using var response = await http.GetAsync("/ksef/invoice/invoice");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(retry, response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var body = await ErrorEnvelope(response);
        Assert.DoesNotContain("private detail", body.ToString());
    }

    [Theory]
    [InlineData("/ksef/status")]
    [InlineData("/ksef/contexts")]
    [InlineData("/ksef/invoices/received")]
    [InlineData("/ksef/invoices/received/new")]
    [InlineData("/ksef/invoices/issued")]
    [InlineData("/ksef/invoice/invoice")]
    [InlineData("/ksef/invoice/invoice/pdf")]
    [InlineData("/ksef/invoice")]
    [InlineData("/ksef/send")]
    [InlineData("/ksef/send/json")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task ProtectedRoutes_MissingAndWrongKey_Return401(string path)
    {
        using var host = new GatewayHost();
        using var http = host.CreateClient();
        foreach (var key in new[] { "", "wrong" })
        {
            http.DefaultRequestHeaders.Remove("X-Api-Key");
            if (key != "") http.DefaultRequestHeaders.Add("X-Api-Key", key);
            using var request = new HttpRequestMessage(path is "/ksef/invoice" or "/ksef/send" or "/ksef/send/json" ? HttpMethod.Post : HttpMethod.Get, path);
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        host.Sdk.VerifyNoOtherCalls();
    }

    private static void Fields(JsonElement value, params string[] expected) =>
        Assert.Equal(expected.Order(), value.EnumerateObject().Select(p => p.Name).Order());

    private static async Task<JsonElement> ErrorEnvelope(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Fields(body, "success", "data", "error", "correlationId");
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), body.GetProperty("correlationId").GetString());
        return body;
    }

    private sealed class GatewayHost(bool licensed = true) : WebApplicationFactory<Program>
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"host-contexts-{Guid.NewGuid():N}.json");
        public Mock<IKSeFClient> Sdk { get; } = new(MockBehavior.Strict);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            File.WriteAllText(path, """[{"nip":"1111111111","label":"A","token":"secret-a"},{"nip":"2222222222","label":"B","certificateContent":"secret-cert","privateKeyContent":"secret-key","privateKeyPassword":"secret-pass"}]""");
            builder.ConfigureAppConfiguration((_, c) => {
                c.Sources.Clear();
                c.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["GATEWAY_API_KEY"] = "host-key", ["KSEF_ENV"] = "TEST", ["KSEF_CONTEXTS_FILE"] = path
                });
            });
            builder.ConfigureServices(services => {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IKSeFClient>();
                services.AddSingleton(Sdk.Object);
                services.RemoveAll<LicenseService>();
                services.AddSingleton(sp => LicensedService(licensed));
                services.RemoveAll<TokenPool>();
                services.AddSingleton<TokenPool, FakePool>();
            });
        }
        public HttpClient Client()
        {
            var http = CreateClient();
            http.DefaultRequestHeaders.Add("X-Api-Key", "host-key");
            return http;
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); File.Delete(path); }
    }

    private sealed class FakePool(IServiceProvider services, ContextProvider provider)
        : TokenPool(services, provider, NullLogger<TokenPool>.Instance)
    {
        protected override Task<AuthenticationOperationStatusResponse> AuthenticateAsync(string nip, CancellationToken ct) =>
            Task.FromResult(JsonSerializer.Deserialize<AuthenticationOperationStatusResponse>(
                JsonSerializer.Serialize(new { AccessToken = new { Token = "access-" + nip, ValidUntil = "9999-01-01T00:00:00Z" } }))!);
    }

    private static LicenseService LicensedService(bool licensed)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new {
            v = 1, kid = "host", product = LicenseService.ProductSlug, email = "test@example.invalid",
            order = "host", tier = "unlimited", iat = 1, exp = (long?)null
        }));
        var token = payload + "." + B64(key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GATEWAY_LICENSE"] = licensed ? token : null }).Build();
        var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kid = "host", alg = "ES256", pem = key.ExportSubjectPublicKeyInfoPem() } } });
        var http = new HttpClient(new LocalLicenseHttp(jwks));
        return new LicenseService(new JwksClient(http, "https://local.invalid/jwks", NullLogger<JwksClient>.Instance),
            new RevocationClient(http, "https://local.invalid/revoked", NullLogger<RevocationClient>.Instance), config, NullLogger<LicenseService>.Instance);
    }

    private sealed class LocalLicenseHttp(string jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri!.AbsolutePath == "/jwks" ? jwks : "{\"order_hashes\":[]}") });
    }
}
