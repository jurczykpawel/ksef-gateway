namespace KSeFGateway.Api.Middleware;

/// <summary>Prevents storage and content sniffing of API responses, including errors.</summary>
public class SensitiveResponseHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (path != "/health" && !path.StartsWithSegments("/scalar") && !path.StartsWithSegments("/openapi"))
        {
            // Apply last, including when downstream error handling replaces the response.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return Task.CompletedTask;
            });
        }

        return next(context);
    }
}
