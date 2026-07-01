using System.Text.Json;
using KSeF.Client.Core.Exceptions;
using KSeFGateway.Api.Models;

namespace KSeFGateway.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (KsefRateLimitException ex)
        {
            _logger.LogWarning("KSeF rate limit hit: {Message}", ex.Message);
            context.Response.StatusCode = 429;
            if (ex.RetryAfterSeconds.HasValue)
                context.Response.Headers.RetryAfter = ex.RetryAfterSeconds.Value.ToString();

            await WriteResponse(context, ApiResponse.Fail($"KSeF rate limit exceeded. Retry after {ex.RetryAfterSeconds}s"));
        }
        catch (KsefApiException ex)
        {
            _logger.LogError(ex, "KSeF API error: {Message}", ex.Message);
            context.Response.StatusCode = 502; // Bad Gateway - upstream error
            await WriteResponse(context, ApiResponse.Fail($"KSeF API error: {ex.Message}"));
        }
        catch (KsefCircuitBreakerOpenException ex)
        {
            _logger.LogWarning("KSeF circuit breaker open: {Message}", ex.Message);
            context.Response.StatusCode = 503;
            if (ex.RetryAfter.HasValue)
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)).ToString();

            await WriteResponse(context, ApiResponse.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            await WriteResponse(context, ApiResponse.Fail("Internal server error"));
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task WriteResponse(HttpContext context, ApiResponse response)
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, response, JsonOptions);
    }
}
