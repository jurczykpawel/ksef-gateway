using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Discovery;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace KSeFGateway.Api.Tests.Middleware;

public class OperationRateLimitTests
{
    [Fact]
    public async Task Poll_FivePages_ConsumesFiveQueryPermits()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 15; i++)
        {
            await f.Operation("/invoices/query/metadata");
            f.Clock.Advance(TimeSpan.FromSeconds(61));
        }
        var poll = await f.Gateway.GetAsync("/ksef/invoices/received/new?since=2026-01-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, poll.StatusCode);
        Assert.Equal(20, f.Transport.Calls);
        await Assert.ThrowsAsync<KSeF.Client.Core.Exceptions.KsefRateLimitException>(() => f.Operation("/invoices/query/metadata"));
        Assert.Equal(20, f.Transport.Calls);
    }

    [Theory]
    [InlineData("/ksef/send")]
    [InlineData("/KSEF/send")]
    [InlineData("/ksef/online-session/open-online-session")]
    public async Task FriendlyAndRawSend_ShareSessionOpenBudget(string path)
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 120; i++) await f.Operation("/sessions/online");
        var response = await f.Gateway.PostAsync(path, new StringContent(path.Contains("online-session") ? "{}" : "<Faktura/>", Encoding.UTF8, path.Contains("online-session") ? "application/json" : "application/xml"));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(int.Parse(response.Headers.GetValues("Retry-After").Single()) > 0);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Retry after", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(120, f.Transport.Calls);
    }

    [Fact]
    public async Task TwoNips_ContextBudgetsAreIndependent()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 20; i++)
        {
            await f.Operation("/invoices/query/metadata");
            f.Clock.Advance(TimeSpan.FromSeconds(61));
        }
        await f.Operation("/invoices/query/metadata", "second-token");
        await Assert.ThrowsAsync<KSeF.Client.Core.Exceptions.KsefRateLimitException>(() => f.Operation("/invoices/query/metadata"));
        Assert.Equal(21, f.Transport.Calls);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/ksef/status")]
    [InlineData("/KSEF/status")]
    [InlineData("/ksef/contexts")]
    public async Task Diagnostics_DoNotConsumeKsefBudget(string path)
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 40; i++)
            Assert.Equal(HttpStatusCode.OK, (await f.Gateway.GetAsync(path)).StatusCode);
        Assert.Equal(0, f.Transport.Calls);
    }

    [Fact]
    public async Task UpstreamRetryAfter_BlocksFurtherOperationsUntilDeadline()
    {
        await using var f = await Fixture.Create();
        f.Transport.Reply = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return response;
        };
        // Direct transport is used so no SDK retry policy masks the original response.
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Operation("/invoices/query/metadata"));
        var denial = await Assert.ThrowsAsync<KSeF.Client.Core.Exceptions.KsefRateLimitException>(() => f.Operation("/invoices/query/metadata"));
        Assert.Equal(42, denial.RetryAfterSeconds);
        Assert.Equal(1, f.Transport.Calls);
        f.Clock.Advance(TimeSpan.FromSeconds(42));
        f.Transport.Reply = null;
        await f.Operation("/invoices/query/metadata");
        Assert.Equal(2, f.Transport.Calls);
    }

    [Fact]
    public async Task StatusPolling_DenialReachesCallerWith429()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 1200; i++) await f.Operation("/sessions/ref/invoices/invoice");
        var response = await f.Gateway.PostAsync("/ksef/send", new StringContent("<Faktura/>", Encoding.UTF8, "application/xml"));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1203, f.Transport.Calls); // open + send + close; status attempt was rejected locally
    }

    [Fact]
    public async Task ExternalStorage_DoesNotConsumeKsefBudget()
    {
        await using var f = await Fixture.Create();
        await f.Operation("https://storage.invalid/upload", null);
        Assert.Equal(1, f.Transport.Calls);
    }

    [Fact]
    public async Task AnonymousBudget_IsSharedAcrossNips()
    {
        await using var f = await Fixture.Create();
        f.Transport.Step = TimeSpan.Zero;
        for (var i = 0; i < 60; i++) await f.Operation("/auth/challenge", i % 2 == 0 ? "first-token" : "second-token");
        var denial = await Assert.ThrowsAsync<KSeF.Client.Core.Exceptions.KsefRateLimitException>(() => f.Operation("/auth/challenge"));
        Assert.Equal(1, denial.RetryAfterSeconds);
        Assert.Equal(60, f.Transport.Calls);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Operation("/auth/challenge", null);
        Assert.Equal(61, f.Transport.Calls);
    }

    [Fact]
    public async Task ConcurrentOperations_CannotOverspendSecondWindow()
    {
        await using var f = await Fixture.Create();
        f.Transport.Step = TimeSpan.Zero;
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            try { await f.Operation("/invoices/query/metadata"); return true; }
            catch (KSeF.Client.Core.Exceptions.KsefRateLimitException) { return false; }
        })));
        Assert.Equal(8, results.Count(success => success));
        Assert.Equal(8, f.Transport.Calls);
    }

    [Fact]
    public async Task TokenRefresh_DoesNotLoseInFlightContext()
    {
        await using var f = await Fixture.Create();
        var response = await f.Gateway.GetAsync("/test/token-refresh");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, f.Transport.Calls);
    }

    private sealed class Fixture(WebApplication app, HttpClient gateway, HttpClient upstream, Transport transport, ManualClock clock) : IAsyncDisposable
    {
        public HttpClient Gateway => gateway;
        public Transport Transport => transport;
        public ManualClock Clock => clock;
        public async Task Operation(string path, string? token = "first-token")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path.StartsWith("https://") ? path : "/v2" + path);
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await upstream.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        public static async Task<Fixture> Create()
        {
            var b = WebApplication.CreateBuilder();
            b.WebHost.UseTestServer();
            b.Configuration.Sources.Clear();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KSEF_CONTEXTS_FILE"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"),
                ["KSEF_NIP"] = "1234567890", ["KSEF_TOKEN"] = "unused"
            });
            b.Services.AddSingleton(KsefEnvironment.Parse("TEST"));
            var clock = new ManualClock();
            var transport = new Transport(clock);
            b.Services.AddSingleton<TimeProvider>(clock);
            string clientName = "";
            b.Services.AddKSeFClient(o => o.BaseUrl = "https://unused.invalid", http =>
            {
                http.AddKsefOperationRateLimiting();
                clientName = http.Name;
                http.ConfigurePrimaryHttpMessageHandler(() => transport);
            });
            // All remote traffic is replaced by Transport; no auth/hosted services are started.
            b.Services.AddSingleton<LicenseService>();
            b.Services.AddSingleton(sp => new JwksClient(new HttpClient(transport), "https://unused.invalid", sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JwksClient>>()));
            b.Services.AddSingleton(sp => new RevocationClient(new HttpClient(transport), "https://unused.invalid", sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RevocationClient>>()));
            b.Services.AddSingleton<ContextProvider>();
            b.Services.AddSingleton<TokenPool>();
            var crypto = new Mock<ICryptographyService>();
            crypto.Setup(c => c.GetEncryptionData()).Returns(() => new() { EncryptionInfo = new() { EncryptedSymmetricKey = "key", InitializationVector = "iv" } });
            crypto.Setup(c => c.EncryptBytesWithAES256(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>())).Returns(new byte[] { 1 });
            crypto.Setup(c => c.GetMetaData(It.IsAny<byte[]>())).Returns(() => new() { HashSHA = "hash", FileSize = 1 });
            b.Services.AddSingleton(crypto.Object);
            var app = b.Build();
            var pool = app.Services.GetRequiredService<TokenPool>();
            var first = pool.GetState("1234567890");
            first.AccessToken = "first-token";
            first.AccessTokenExpiresAt = DateTimeOffset.MaxValue;
            // A second active state models a licensed context without involving license networking.
            var states = (System.Collections.Concurrent.ConcurrentDictionary<string, AuthState>)typeof(TokenPool)
                .GetField("_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pool)!;
            states["9876543210"] = new AuthState { AccessToken = "second-token", AccessTokenExpiresAt = DateTimeOffset.MaxValue };
            app.MapGet("/test/token-refresh", async () =>
            {
                var token = await pool.GetAccessTokenAsync("1234567890");
                pool.GetState("1234567890").AccessToken = "rotated-token";
                using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, "/v2/invoices/ksef/number");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(request);
                return Results.StatusCode((int)response.StatusCode);
            });
            app.UseMiddleware<ErrorHandlingMiddleware>();
            app.UseMiddleware<RateLimitMiddleware>();
            app.MapDiscoveredEndpoints(SdkReflector.DiscoverEndpoints().Where(e => e.GroupName == "online-session").ToList());
            app.MapInvoiceDownloadEndpoints();
            app.MapWorkflowEndpoints();
            app.MapHealthEndpoints(64, 64);
            await app.StartAsync();
            return new Fixture(app, app.GetTestClient(), app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName), transport, clock);
        }
        public async ValueTask DisposeAsync() { gateway.Dispose(); upstream.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class Transport(ManualClock clock) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;
        public Func<HttpResponseMessage>? Reply { get; set; }
        public TimeSpan Step { get; set; } = TimeSpan.FromSeconds(2);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            clock.Advance(Step);
            if (Reply != null) return Task.FromResult(Reply());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"hasMore":true,"invoices":[],"permanentStorageHwmDate":"2026-01-02T00:00:00Z","referenceNumber":"session","ksefNumber":"accepted","status":{"code":200,"description":"accepted"}}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
