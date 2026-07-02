using KSeFGateway.Api.Models;

namespace KSeFGateway.Api.Middleware;

/// <summary>
/// Optional defense-in-depth for gateways placed behind a trusted proxy (Cloudflare, a reverse
/// proxy, an API gateway). When <c>TRUSTED_PROXY_SECRET</c> is set, every request except
/// <c>/health</c> must carry a secret header - <see cref="DefaultHeaderName"/>, overridable via
/// <c>TRUSTED_PROXY_HEADER</c> - that only the proxy injects. Requests that reach the origin
/// directly, bypassing the proxy (for example a platform's always-public <c>*.onrender.com</c>
/// URL), lack the header and are refused, so a proxy-side IP allowlist can't simply be sidestepped.
///
/// Opt-in: if <c>TRUSTED_PROXY_SECRET</c> is unset this middleware is a transparent no-op and the
/// gateway behaves exactly as before (only <see cref="ApiKeyMiddleware"/> guards callers).
/// </summary>
public class TrustedProxyMiddleware
{
    public const string DefaultHeaderName = "X-Trusted-Proxy-Secret";

    private readonly RequestDelegate _next;
    private readonly string? _secret;
    private readonly string _headerName;

    public TrustedProxyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _secret = config["TRUSTED_PROXY_SECRET"];
        var header = config["TRUSTED_PROXY_HEADER"];
        _headerName = string.IsNullOrWhiteSpace(header) ? DefaultHeaderName : header.Trim();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Opt-in: with no secret configured the check is disabled and behaviour is unchanged.
        if (string.IsNullOrEmpty(_secret))
        {
            await _next(context);
            return;
        }

        // /health must stay reachable straight at the origin: platform health checks (e.g. Render's)
        // hit the origin directly, not through the proxy, so they never carry the injected header.
        if (context.Request.Path == "/health")
        {
            await _next(context);
            return;
        }

        var provided = context.Request.Headers[_headerName].ToString();
        if (string.IsNullOrEmpty(provided) || !SecretComparison.ConstantTimeEquals(provided, _secret))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
                "This gateway only accepts traffic forwarded by its trusted proxy."));
            return;
        }

        await _next(context);
    }
}
