using System.Text.Json;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Endpoints;

public class HealthEndpointsTests
{
    public static TheoryData<string, string?, string> Environments => new()
    {
        { "/health", " prod ", "PRODUCTION" },
        { "/health", "TEST", "TEST" },
        { "/health", "DEMO", "DEMO" },
        { "/health", "PRODUCTION", "PRODUCTION" },
        { "/health", null, "TEST" },
        { "/health", " \t", "TEST" },
        { "/ksef/status", " pRd ", "PRODUCTION" },
        { "/ksef/status", "TEST", "TEST" },
        { "/ksef/status", "DEMO", "DEMO" },
        { "/ksef/status", "PRODUCTION", "PRODUCTION" },
        { "/ksef/status", null, "TEST" },
        { "/ksef/status", " \t", "TEST" },
    };

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Environment_ReturnsCanonicalName(string path, string? input, string expected)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KSEF_ENV"] = input,
            ["KSEF_CONTEXTS_FILE"] = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")
        });
        builder.Services.AddSingleton(KsefEnvironment.Parse(input));
        using var httpClient = new HttpClient();
        builder.Services.AddSingleton(new LicenseService(
            new JwksClient(httpClient, "https://unused.invalid/jwks", NullLogger<JwksClient>.Instance),
            new RevocationClient(httpClient, "https://unused.invalid/revoked", NullLogger<RevocationClient>.Instance),
            builder.Configuration, NullLogger<LicenseService>.Instance));
        builder.Services.AddSingleton<ContextProvider>();
        builder.Services.AddSingleton<TokenPool>();
        await using var app = builder.Build();
        app.MapHealthEndpoints(64, 20);

        // Invoke the real mapped delegate without starting the host or background services.
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText == path);
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);

        Assert.Equal(200, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        if (path == "/health")
        {
            Assert.Single(body.RootElement.EnumerateObject());
            Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
            return;
        }
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(expected, data.GetProperty("environment").GetString());
        Assert.Equal(64, data.GetProperty("discoveredEndpoints").GetInt32());
        Assert.Equal(20, data.GetProperty("exposedEndpoints").GetInt32());
    }
}
