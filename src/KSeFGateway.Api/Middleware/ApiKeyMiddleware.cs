using KSeFGateway.Api.Models;

namespace KSeFGateway.Api.Middleware;

/// <summary>
/// Requires a shared-secret API key (<see cref="HeaderName"/>) on every request except
/// <c>/health</c>. The gateway has no other caller-facing protection - it only authenticates
/// itself to KSeF, never the caller to itself - so this fails closed: if GATEWAY_API_KEY isn't
/// configured, every protected request is rejected rather than silently left open.
/// </summary>
public class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly string? _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        _apiKey = config["GATEWAY_API_KEY"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/health")
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError(
                "GATEWAY_API_KEY is not configured - rejecting all non-health requests. " +
                "Set GATEWAY_API_KEY to enable the gateway's API.");
            await Reject(context, 503,
                "Gateway is not configured with an API key (GATEWAY_API_KEY) - refusing all requests until it is set.");
            return;
        }

        var provided = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(provided) || !SecretComparison.ConstantTimeEquals(provided, _apiKey))
        {
            await Reject(context, 401, $"Missing or invalid {HeaderName} header.");
            return;
        }

        await _next(context);
    }

    private static async Task Reject(HttpContext context, int statusCode, string error)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(error));
    }
}
