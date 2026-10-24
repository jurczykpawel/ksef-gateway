using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Discovery;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KSeFGateway.Api.Tests.Auth;

public class ClientApiKeyTests
{
    private const string A = "1234567890", B = "0987654321", Unknown = "9999999999";
    private static string Hash(string key) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    [Theory]
    [InlineData(B)]
    [InlineData(Unknown)]
    public async Task Client_CannotSelectOtherNip(string nip)
    {
        await using var host = await Host.Create();
        var response = await host.Send("client-a", "/ksef/invoice", nip, "{}");
        await Forbidden(response, nip);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_CannotUseOtherSellerEvenWithOwnHeader(bool header)
    {
        await using var host = await Host.Create();
        var response = await host.Send("client-a", "/ksef/send", header ? A : null,
            $"<Faktura><Podmiot1><NIP>{B}</NIP></Podmiot1></Faktura>", "application/xml");
        await Forbidden(response, B);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/pdf")]
    public async Task Client_CannotUseOtherKsefNumber(string suffix)
    {
        await using var host = await Host.Create();
        await Forbidden(await host.Send("client-a", $"/ksef/invoice/{B}-20261024-ABCDEFGHIJKL-AB{suffix}", A), B);
    }

    [Theory]
    [InlineData("/ksef/contexts")]
    [InlineData("/ksef/status")]
    public async Task Diagnostics_ClientSeesOnlyOwnNip_AdminSeesAll(string path)
    {
        await using var host = await Host.Create();
        var client = await host.Send("client-b", path);
        Assert.Equal(HttpStatusCode.OK, client.StatusCode);
        var text = await client.Content.ReadAsStringAsync();
        Assert.Contains(B, text);
        Assert.DoesNotContain(A, text);
        Assert.DoesNotContain("license", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin@example.invalid", text);
        Assert.DoesNotContain("apiKeys", text);
        var admin = await host.Send("admin", path);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        var all = await admin.Content.ReadAsStringAsync();
        Assert.Contains(A, all);
        Assert.Contains(B, all);
        if (path.EndsWith("status")) Assert.Contains("license", all);
    }

    [Theory]
    [InlineData("client-b", null, 200, B)]
    [InlineData("shared", null, 400, null)]
    [InlineData("shared", B, 200, B)]
    [InlineData("admin", null, 200, A)]
    [InlineData("wrong", null, 401, null)]
    public async Task DefaultContext_IsScopedToKey(string key, string? header, int status, string? expected)
    {
        await using var host = await Host.Create();
        var response = await host.Send(key, "/resolve", header);
        Assert.Equal(status, (int)response.StatusCode);
        if (expected != null) Assert.Equal(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sdk_UnsafeRouteIsHiddenFromClientButAvailableToAdmin()
    {
        await using var host = await Host.Create();
        var route = SdkReflector.DiscoverEndpoints().First(e => e.GroupName == "ksef-token").Route;
        Assert.Equal(HttpStatusCode.NotFound, (await host.Send("client-a", route, A, "{}")).StatusCode);
        // No cached token: reaching this handler returns 503, without contacting KSeF.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Send("admin", route, A, "{}")).StatusCode);
        var safe = SdkReflector.DiscoverEndpoints().Single(e => e.MethodName == "get-invoice").Route;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Send("client-a", safe, A, JsonSerializer.Serialize(new { ksefNumber = A + "-20261024-ABCDEFGHIJKL-AB" }))).StatusCode);
    }

    [Fact]
    public async Task MissingAdminKey_FailsClosedForClientToo()
    {
        await using var host = await Host.Create(adminKey: null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Send("client-a", "/ksef/status")).StatusCode);
    }

    [Theory]
    [InlineData("plaintext-secret")]
    [InlineData("sha256:abc")]
    [InlineData("sha256:gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData(null)]
    public async Task InvalidHash_RejectsStartup(string? hash)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var host = await Host.Create(hash: hash);
        });
        Assert.Contains("apiKeys", error.Message);
        Assert.Contains("sha256:", error.Message);
        Assert.DoesNotContain("plaintext-secret", error.Message);
    }

    [Fact]
    public async Task FriendlyInvoice_ChecksSellerDespiteOwnHeader()
    {
        await using var host = await Host.Create();
        var invoice = KSeFGateway.Api.Tests.Invoice.InvoiceSpecialRatesTests.Input();
        invoice["seller"]!["nip"] = B;
        await Forbidden(await host.Send("client-a", "/ksef/invoice", A, invoice.ToJsonString()), B);
    }

    [Theory]
    [InlineData("<Faktura xmlns:f='urn:test'><f:Podmiot1><f:DaneIdentyfikacyjne><f:NIP>0987654321</f:NIP></f:DaneIdentyfikacyjne></f:Podmiot1></Faktura>", "application/xml", "/ksef/send")]
    [InlineData("{\"seller\":{\"n\\u0069p\":\"0987654321\"}}", "application/json", "/ksef/send/json")]
    [InlineData("{\"seller\":{\"nip\":\"1234567890\"},\"seller\":{\"nip\":\"0987654321\"}}", "application/json", "/ksef/send/json")]
    [InlineData("{\"f:Faktura\":{\"f:Podmiot1\":{\"f:DaneIdentyfikacyjne\":{\"f:NIP\":{\"_text\":\"0987654321\"}}}}}", "application/json", "/ksef/send/json")]
    public async Task Seller_ParsedStructurally(string body, string media, string path)
    {
        await using var host = await Host.Create();
        await Forbidden(await host.Send("client-a", path, A, body, media), B);
    }

    [Fact]
    public async Task Sdk_RejectsOtherKsefNumberBeforeAuthentication()
    {
        await using var host = await Host.Create();
        var route = SdkReflector.DiscoverEndpoints().Single(e => e.MethodName == "get-invoice").Route;
        await Forbidden(await host.Send("client-a", route, A,
            JsonSerializer.Serialize(new { ksefNumber = B + "-20261024-ABCDEFGHIJKL-AB" })), B);
    }

    [Fact]
    public async Task JsonConversion_RechecksSellerInFinalXml()
    {
        await using var host = await Host.Create(pdfXml: $"<Faktura><Podmiot1><NIP>{B}</NIP></Podmiot1></Faktura>");
        await Forbidden(await host.Send("client-a", "/ksef/send/json", A,
            "{\"Faktura\":{\"Podmiot1\":{\"DaneIdentyfikacyjne\":{\"NIP\":{\"_text\":1234567890}}}}}"), B);
    }

    [Fact]
    public void ClientHashComparison_UsesSharedConstantTimePrimitive()
    {
        var method = typeof(ContextProvider).GetMethod("FindNipsForApiKey");
        Assert.NotNull(method);
        var helper = typeof(ApiKeyMiddleware).Assembly.GetType("KSeFGateway.Api.Middleware.SecretComparison")!
            .GetMethod("ConstantTimeEquals")!;
        Assert.True(Calls(method!, helper), "Client hashes must use SecretComparison.ConstantTimeEquals");
        Assert.True(Calls(helper, typeof(CryptographicOperations).GetMethod("FixedTimeEquals")!),
            "SecretComparison must use CryptographicOperations.FixedTimeEquals");
    }

    private static bool Calls(MethodInfo caller, MethodInfo target)
    {
        var il = caller.GetMethodBody()!.GetILAsByteArray()!;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6f)) continue; // call / callvirt
            try { if (caller.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)) == target) return true; }
            catch (ArgumentException) { }
        }
        return false;
    }

    private static async Task Forbidden(HttpResponseMessage response, string nip)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal($"API key is not authorized for NIP {nip}", body.GetProperty("error").GetString());
    }

    private sealed class Host(WebApplication app, string file) : IAsyncDisposable
    {
        public static async Task<Host> Create(string? adminKey = "admin", string? hash = "valid", string? pdfXml = null)
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new[] {
                new { nip = A, token = "fake", apiKeys = new[] { hash == "valid" ? Hash("client-a") : hash, Hash("shared") } },
                new { nip = B, token = "fake", apiKeys = new string?[] { Hash("client-b").ToUpperInvariant().Replace("SHA256:", "sha256:"), Hash("shared") } }
            }));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["KSEF_CONTEXTS_FILE"] = file, ["GATEWAY_API_KEY"] = adminKey });
            using var http = new HttpClient(new RejectNetwork());
            var license = new LicenseService(new JwksClient(http, "https://unused.invalid", NullLogger<JwksClient>.Instance),
                new RevocationClient(http, "https://unused.invalid", NullLogger<RevocationClient>.Instance),
                builder.Configuration, NullLogger<LicenseService>.Instance);
            typeof(LicenseService).GetField("_claims", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(license,
                new LicenseClaims(1, "test", LicenseService.ProductSlug, "admin@example.invalid", "order", null, 0, null));
            ContextProvider provider;
            try { provider = new ContextProvider(builder.Configuration, NullLogger<ContextProvider>.Instance, license); }
            catch { File.Delete(file); license.Dispose(); throw; }
            builder.Services.AddSingleton(provider).AddSingleton(license).AddSingleton(KsefEnvironment.Parse("TEST"));
            builder.Services.AddSingleton<TokenPool>(sp => new OfflinePool(sp, provider));
            builder.Services.AddSingleton(Mock.Of<IKSeFClient>()).AddSingleton(Mock.Of<ICryptographyService>());
            builder.Services.AddHttpClient().ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => pdfXml == null ? new RejectNetwork() : new PdfReply(pdfXml)));
            var app = builder.Build();
            if (pdfXml != null)
            {
                var states = (System.Collections.Concurrent.ConcurrentDictionary<string, AuthState>)typeof(TokenPool)
                    .GetField("_states", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(app.Services.GetRequiredService<TokenPool>())!;
                states[A] = new AuthState { AccessToken = "cached", AccessTokenExpiresAt = DateTimeOffset.MaxValue };
            }
            app.UseMiddleware<ErrorHandlingMiddleware>();
            app.UseMiddleware<ApiKeyMiddleware>();
            app.UseMiddleware<RateLimitMiddleware>();
            app.MapWorkflowEndpoints();
            app.MapHealthEndpoints(64, 64);
            app.MapDiscoveredEndpoints(SdkReflector.DiscoverEndpoints());
            app.MapGet("/resolve", (HttpContext context) => ContextResolver.ResolveNip(context, provider)!);
            await app.StartAsync();
            return new Host(app, file);
        }

        public async Task<HttpResponseMessage> Send(string key, string path, string? nip = null, string? body = null, string media = "application/json")
        {
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
            request.Headers.Add("X-Api-Key", key);
            if (nip != null) request.Headers.Add("X-KSeF-NIP", nip);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, media);
            return await app.GetTestClient().SendAsync(request);
        }

        public async ValueTask DisposeAsync() { await app.DisposeAsync(); File.Delete(file); }
    }

    private sealed class OfflinePool(IServiceProvider services, ContextProvider provider)
        : TokenPool(services, provider, NullLogger<TokenPool>.Instance)
    {
        protected override Task<KSeF.Client.Core.Models.Authorization.AuthenticationOperationStatusResponse> AuthenticateAsync(string nip, CancellationToken ct) =>
            Task.FromResult<KSeF.Client.Core.Models.Authorization.AuthenticationOperationStatusResponse>(null!);
    }
    private sealed class PdfReply(string xml) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Tests must not access the network");
    }
}
