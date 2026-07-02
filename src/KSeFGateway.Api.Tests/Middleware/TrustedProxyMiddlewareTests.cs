using System.Text.Json;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Middleware;

public class TrustedProxyMiddlewareTests
{
    private static IConfiguration Config(string? secret, string? header = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRUSTED_PROXY_SECRET"] = secret,
                ["TRUSTED_PROXY_HEADER"] = header,
            })
            .Build();

    private static async Task<(int StatusCode, JsonElement? Body, bool NextCalled)> Invoke(
        string? configuredSecret,
        string? configuredHeader = null,
        string path = "/ksef/status",
        (string Name, string Value)? providedHeader = null)
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = new TrustedProxyMiddleware(
            next, Config(configuredSecret, configuredHeader), NullLogger<TrustedProxyMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (providedHeader is not null)
            context.Request.Headers[providedHeader.Value.Name] = providedHeader.Value.Value;

        await middleware.InvokeAsync(context);

        JsonElement? body = null;
        if (context.Response.Body.Length > 0)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        }

        return (context.Response.StatusCode, body, nextCalled);
    }

    [Fact]
    public async Task NoSecretConfigured_IsNoOp_PassesThrough()
    {
        // Feature is opt-in: with no secret, requests flow through untouched (even without the header).
        var (statusCode, _, nextCalled) = await Invoke(configuredSecret: null);

        Assert.Equal(200, statusCode); // DefaultHttpContext defaults to 200; middleware never touched it
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task WhitespaceOnlySecret_IsTreatedAsDisabled_PassesThrough()
    {
        // A stray-whitespace secret must NOT arm the feature with a trivially guessable value.
        var (statusCode, _, nextCalled) = await Invoke(configuredSecret: "   ");

        Assert.Equal(200, statusCode);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SecretConfigured_ValidHeader_PassesThrough()
    {
        var (statusCode, _, nextCalled) = await Invoke(
            configuredSecret: "s3cret",
            providedHeader: (TrustedProxyMiddleware.DefaultHeaderName, "s3cret"));

        Assert.Equal(200, statusCode);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SecretConfigured_MissingHeader_RejectsWith403()
    {
        var (statusCode, body, nextCalled) = await Invoke(configuredSecret: "s3cret");

        Assert.Equal(403, statusCode);
        Assert.False(nextCalled);
        Assert.False(body!.Value.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task SecretConfigured_WrongHeader_RejectsWith403()
    {
        var (statusCode, _, nextCalled) = await Invoke(
            configuredSecret: "s3cret",
            providedHeader: (TrustedProxyMiddleware.DefaultHeaderName, "wrong"));

        Assert.Equal(403, statusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task SecretConfigured_HealthEndpoint_BypassesCheck()
    {
        // Platform health checks hit the origin directly, without the proxy-injected header.
        var (statusCode, _, nextCalled) = await Invoke(configuredSecret: "s3cret", path: "/health");

        Assert.Equal(200, statusCode);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task CustomHeaderName_IsHonored()
    {
        var (statusCode, _, nextCalled) = await Invoke(
            configuredSecret: "s3cret",
            configuredHeader: "CF-Edge-Token",
            providedHeader: ("CF-Edge-Token", "s3cret"));

        Assert.Equal(200, statusCode);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task CustomHeaderConfigured_DefaultHeaderIgnored_RejectsWith403()
    {
        // With a custom header name set, sending the secret under the default header must not pass.
        var (statusCode, _, nextCalled) = await Invoke(
            configuredSecret: "s3cret",
            configuredHeader: "CF-Edge-Token",
            providedHeader: (TrustedProxyMiddleware.DefaultHeaderName, "s3cret"));

        Assert.Equal(403, statusCode);
        Assert.False(nextCalled);
    }
}
