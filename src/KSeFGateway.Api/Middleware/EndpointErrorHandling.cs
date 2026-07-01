using System.Reflection;
using System.Runtime.ExceptionServices;
using KSeF.Client.Core.Exceptions;
using KSeFGateway.Api.Models;

namespace KSeFGateway.Api.Middleware;

/// <summary>
/// Shared exception handling for endpoint handlers.
///
/// KSeF API errors are rethrown unwrapped (from TargetInvocationException, when the SDK was
/// invoked via reflection) so ErrorHandlingMiddleware can turn them into the right response:
/// KsefRateLimitException (which derives from KsefApiException) becomes 429 + Retry-After,
/// KsefCircuitBreakerOpenException (the SDK's own client-side circuit breaker tripping after
/// repeated upstream failures) becomes 503 + Retry-After, any other KsefApiException becomes
/// 502. Everything else becomes a generic 500 with the exception's message - each individual
/// handler no longer needs to duplicate this catch logic.
/// </summary>
public static class EndpointErrorHandling
{
    public static async Task<IResult> Guard(
        Func<Task<IResult>> action,
        ILogger? logger = null,
        string logContext = "Request failed")
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var real = ex is TargetInvocationException { InnerException: not null } wrapped
                ? wrapped.InnerException!
                : ex;

            if (real is KsefApiException or KsefCircuitBreakerOpenException)
                ExceptionDispatchInfo.Capture(real).Throw();

            logger?.LogError(real, "{Context}: {Message}", logContext, real.Message);
            return Results.Json(ApiResponse.Fail(real.Message), statusCode: 500);
        }
    }
}
