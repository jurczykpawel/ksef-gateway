using System.Text.Json;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Middleware;

public class ApiKeyMiddlewareTests
{
    private static IConfiguration Config(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GATEWAY_API_KEY"] = apiKey })
            .Build();

    private static async Task<(int StatusCode, JsonElement? Body, bool NextCalled)> Invoke(
        string? configuredKey, string path = "/ksef/status", string? providedKey = null)
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = new ApiKeyMiddleware(next, NullLogger<ApiKeyMiddleware>.Instance, Config(configuredKey));
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (providedKey is not null)
            context.Request.Headers[ApiKeyMiddleware.HeaderName] = providedKey;

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
    public async Task NoKeyConfigured_RejectsWith503()
    {
        var (statusCode, body, nextCalled) = await Invoke(configuredKey: null, providedKey: "anything");

        Assert.Equal(503, statusCode);
        Assert.False(nextCalled);
        Assert.False(body!.Value.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task NoKeyConfigured_HealthEndpointStillPasses()
    {
        var (statusCode, _, nextCalled) = await Invoke(configuredKey: null, path: "/health");

        Assert.Equal(200, statusCode); // DefaultHttpContext defaults to 200; middleware never touched it
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task KeyConfigured_NoHeaderProvided_RejectsWith401()
    {
        var (statusCode, body, nextCalled) = await Invoke(configuredKey: "s3cret", providedKey: null);

        Assert.Equal(401, statusCode);
        Assert.False(nextCalled);
        Assert.False(body!.Value.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task KeyConfigured_WrongHeaderProvided_RejectsWith401()
    {
        var (statusCode, _, nextCalled) = await Invoke(configuredKey: "s3cret", providedKey: "wrong");

        Assert.Equal(401, statusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task KeyConfigured_CorrectHeaderProvided_PassesThrough()
    {
        var (statusCode, _, nextCalled) = await Invoke(configuredKey: "s3cret", providedKey: "s3cret");

        Assert.Equal(200, statusCode);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task KeyConfigured_HealthEndpointBypassesCheckEvenWithoutHeader()
    {
        var (statusCode, _, nextCalled) = await Invoke(configuredKey: "s3cret", path: "/health", providedKey: null);

        Assert.Equal(200, statusCode);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task KeyConfigured_DifferentLengthHeaderProvided_RejectsWith401()
    {
        var (statusCode, _, nextCalled) = await Invoke(configuredKey: "s3cret", providedKey: "s3cret-but-longer");

        Assert.Equal(401, statusCode);
        Assert.False(nextCalled);
    }
}
