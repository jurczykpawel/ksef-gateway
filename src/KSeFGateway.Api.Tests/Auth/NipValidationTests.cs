using System.Reflection;
using System.Text.Json;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace KSeFGateway.Api.Tests.Auth;

public class NipValidationTests
{
    private const string ConfiguredNip = "1234567890"; // Deliberately no checksum validation.

    private static ContextProvider Provider(bool configured = true)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KSEF_CONTEXTS_FILE"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"),
            ["KSEF_NIP"] = configured ? ConfiguredNip : null,
            ["KSEF_TOKEN"] = configured ? "test-token" : null
        }).Build();
        using var http = new HttpClient(new RejectNetwork());
        var license = new LicenseService(
            new JwksClient(http, "https://unused.invalid", NullLogger<JwksClient>.Instance),
            new RevocationClient(http, "https://unused.invalid", NullLogger<RevocationClient>.Instance),
            config, NullLogger<LicenseService>.Instance);
        return new ContextProvider(config, NullLogger<ContextProvider>.Instance, license);
    }

    public static TheoryData<string[]> InvalidHeaders => new()
    {
        new[] { "abcdefghij" }, new[] { "123456789" }, new[] { "12345678901" },
        new[] { " 1234567890" }, new[] { "1234567890 " }, new[] { "" },
        new[] { new string('9', 100_000) }, new[] { ConfiguredNip, ConfiguredNip },
        new[] { ConfiguredNip, "" }, new[] { "1234567890\n" }, new[] { "１２３４５６７８９０" }
    };

    [Theory]
    [MemberData(nameof(InvalidHeaders))]
    public void Resolver_InvalidHeader_Returns400WithoutEcho(string[] values)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ContextResolver.NipHeader] = new StringValues(values);
        var error = Assert.Throws<BadHttpRequestException>(() => ContextResolver.ResolveNip(context, Provider()));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("NIP must contain exactly 10 ASCII digits; X-KSeF-NIP must have a single value.", error.Message);
    }

    [Fact]
    public void Resolver_UnknownHeader_Returns404()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ContextResolver.NipHeader] = "9876543210";
        var error = Assert.Throws<BadHttpRequestException>(() => ContextResolver.ResolveNip(context, Provider()));
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("No context configured for NIP 9876543210", error.Message);
    }

    [Theory]
    [InlineData("<Podmiot1><NIP>9876543210</NIP></Podmiot1>")]
    [InlineData("{\"seller\":{\"nip\":\"9876543210\"}}")]
    [InlineData("{\"Podmiot1\":{\"NIP\":{\"_text\":\"9876543210\"}}}")]
    public void Resolver_UnknownSeller_Returns404(string body)
    {
        var error = Assert.Throws<BadHttpRequestException>(() => ContextResolver.ResolveNip(new DefaultHttpContext(), Provider(), body));
        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public void Resolver_PreservesHeaderBodyDefaultPriority()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ContextResolver.NipHeader] = ConfiguredNip;
        Assert.Equal(ConfiguredNip, ContextResolver.ResolveNip(context, Provider(), "{\"seller\":{\"nip\":\"9876543210\"}}"));
        context.Request.Headers.Clear();
        Assert.Equal(ConfiguredNip, ContextResolver.ResolveNip(context, Provider(), "{\"seller\":{\"nip\":\"1234567890\"}}"));
        Assert.Equal(ConfiguredNip, ContextResolver.ResolveNip(context, Provider()));
        Assert.Null(ContextResolver.ResolveNip(context, Provider(false)));
    }

    [Theory]
    [InlineData("bad", 400)]
    [InlineData("9876543210", 404)]
    public async Task Pool_RejectsBeforeAllocatingOrLogging(string nip, int status)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var logger = new RecordingLogger<TokenPool>();
        using var pool = new TokenPool(services, Provider(), logger);
        var error = await Assert.ThrowsAsync<BadHttpRequestException>(() => pool.GetAccessTokenAsync(nip));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(status, Assert.Throws<BadHttpRequestException>(() => pool.GetState(nip)).StatusCode);
        Assert.Empty(pool.GetAllStates());
        Assert.Empty(AuthLocks(pool));
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task Pool_ConfiguredNip_ReturnsCachedToken()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var pool = new TokenPool(services, Provider(), NullLogger<TokenPool>.Instance);
        pool.GetState(ConfiguredNip).AccessToken = "cached";
        pool.GetState(ConfiguredNip).AccessTokenExpiresAt = DateTimeOffset.MaxValue;
        Assert.Equal("cached", await pool.GetAccessTokenAsync(ConfiguredNip));
        Assert.Single(pool.GetAllStates());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipeline_1000RejectedNips_NoPoolOrLimiterGrowth(bool validFormat)
    {
        var provider = Provider();
        using var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();
        using var pool = new TokenPool(services, provider, NullLogger<TokenPool>.Instance);
        var reachedEndpoint = false;
        var limiter = new RateLimitMiddleware(async context =>
        {
            reachedEndpoint = true;
            var nip = ContextResolver.ResolveNip(context, provider)!;
            await pool.GetAccessTokenAsync(nip);
        }, NullLogger<RateLimitMiddleware>.Instance);
        var errors = new ErrorHandlingMiddleware(limiter.InvokeAsync, NullLogger<ErrorHandlingMiddleware>.Instance);
        for (var i = 0; i < 1000; i++)
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = "/ksef/invoices/received";
            context.Request.Headers[ContextResolver.NipHeader] = validFormat ? $"9{i:D9}" : $"invalid-{i}";
            context.Response.Body = new MemoryStream();
            await errors.InvokeAsync(context);
            Assert.Equal(validFormat ? 404 : 400, context.Response.StatusCode);
            context.Response.Body.Position = 0;
            using var json = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.False(json.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(validFormat ? "No context configured for NIP" : "10 ASCII digits", json.RootElement.GetProperty("error").GetString());
        }
        Assert.False(reachedEndpoint);
        Assert.Empty(pool.GetAllStates());
        Assert.Empty(AuthLocks(pool));
        var counters = typeof(RateLimitMiddleware).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Empty((System.Collections.IEnumerable)counters.GetValue(limiter)!);
    }

    [Fact]
    public async Task Guard_ContextError_ReachesErrorMiddleware()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        async Task Endpoint(HttpContext _)
        {
            var result = await EndpointErrorHandling.Guard(() =>
            {
                var request = new DefaultHttpContext();
                request.Request.Headers[ContextResolver.NipHeader] = "9876543210";
                ContextResolver.ResolveNip(request, Provider());
                return Task.FromResult<IResult>(Results.Ok());
            });
            context.Response.StatusCode = ((IStatusCodeHttpResult)result).StatusCode ?? 200;
        }
        var errors = new ErrorHandlingMiddleware(Endpoint, NullLogger<ErrorHandlingMiddleware>.Instance);
        await errors.InvokeAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
    }

    private static System.Collections.IEnumerable AuthLocks(TokenPool pool) =>
        (System.Collections.IEnumerable)typeof(TokenPool).GetField("_authLocks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Tests must not access the network");
    }
}
