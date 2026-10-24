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
        var previous = ErrorCorrelation.Current.Value;
        var correlationId = context.TraceIdentifier;
        ErrorCorrelation.Current.Value = correlationId;
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        try
        {
            await _next(context);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON {CorrelationId}", correlationId);
            context.Response.StatusCode = 400;
            await WriteResponse(context, ApiResponse.Fail("Invalid JSON request body"));
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Upstream timeout {CorrelationId}", correlationId);
            context.Response.StatusCode = 504;
            await WriteResponse(context, ApiResponse.Fail("PDF service timeout"));
        }
        catch (BadHttpRequestException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await WriteResponse(context, ApiResponse.Fail(ex.Message));
        }
        catch (KsefRateLimitException ex)
        {
            _logger.LogWarning(ex, "KSeF rate limit hit {CorrelationId}: {Message}", correlationId, ex.Message);
            context.Response.StatusCode = 429;
            if (ex.RetryAfterSeconds.HasValue)
                context.Response.Headers.RetryAfter = ex.RetryAfterSeconds.Value.ToString();

            await WriteResponse(context, ApiResponse.Fail($"KSeF rate limit exceeded. Retry after {ex.RetryAfterSeconds}s"));
        }
        catch (KsefApiException ex)
        {
            _logger.LogError(ex, "KSeF API error {CorrelationId}: {Message}", correlationId, ex.Message);
            context.Response.StatusCode = 502; // Bad Gateway - upstream error
            await WriteResponse(context, ApiResponse.Fail($"KSeF API error: {ex.Message}"));
        }
        catch (KsefCircuitBreakerOpenException ex)
        {
            _logger.LogWarning(ex, "KSeF circuit breaker open {CorrelationId}: {Message}", correlationId, ex.Message);
            context.Response.StatusCode = 503;
            if (ex.RetryAfter.HasValue)
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)).ToString();

            await WriteResponse(context, ApiResponse.Fail(ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception {CorrelationId}", correlationId);
            context.Response.StatusCode = 500;
            await WriteResponse(context, ApiResponse.Fail("Internal server error"));
        }
        finally
        {
            ErrorCorrelation.Current.Value = previous;
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
