using System.Text.Json;
using KSeF.Client.Core.Exceptions;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Middleware;

public class ErrorHandlingMiddlewareTests
{
    private static async Task<(int StatusCode, string? RetryAfter, JsonElement Body)> Invoke(RequestDelegate next)
    {
        var middleware = new ErrorHandlingMiddleware(next, NullLogger<ErrorHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        var retryAfter = context.Response.Headers.RetryAfter.Count > 0
            ? context.Response.Headers.RetryAfter.ToString()
            : null;

        return (context.Response.StatusCode, retryAfter, body);
    }

    [Fact]
    public async Task RateLimitException_Returns429WithRetryAfter()
    {
        var (statusCode, retryAfter, body) = await Invoke(_ =>
            throw new KsefRateLimitException("too many requests", 42, null, null!));

        Assert.Equal(429, statusCode);
        Assert.Equal("42", retryAfter);
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CircuitBreakerOpenException_Returns503WithRetryAfter()
    {
        var (statusCode, retryAfter, body) = await Invoke(_ =>
            throw new KsefCircuitBreakerOpenException("circuit open", TimeSpan.FromSeconds(17)));

        Assert.Equal(503, statusCode);
        Assert.Equal("17", retryAfter);
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CircuitBreakerOpenException_WithoutRetryAfter_OmitsHeader()
    {
        var (statusCode, retryAfter, _) = await Invoke(_ =>
            throw new KsefCircuitBreakerOpenException("circuit open", null));

        Assert.Equal(503, statusCode);
        Assert.Null(retryAfter);
    }

    [Fact]
    public async Task KsefApiException_Returns502()
    {
        var (statusCode, _, body) = await Invoke(_ =>
            throw new KsefApiException("upstream failed", System.Net.HttpStatusCode.InternalServerError, null!, null!));

        Assert.Equal(502, statusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task UnhandledException_Returns500WithoutLeakingDetails()
    {
        var (statusCode, _, body) = await Invoke(_ => throw new InvalidOperationException("secret internals"));

        Assert.Equal(500, statusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("secret internals", body.GetProperty("error").GetString());
    }
}
