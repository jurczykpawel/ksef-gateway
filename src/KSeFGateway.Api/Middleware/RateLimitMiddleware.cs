using KSeFGateway.Api.Auth;

namespace KSeFGateway.Api.Middleware;

/// <summary>Validate caller context only. Budgets are consumed by the SDK transport.</summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/ksef", StringComparison.OrdinalIgnoreCase))
            ContextResolver.ValidateHeader(context, context.RequestServices.GetRequiredService<ContextProvider>());
        await _next(context);
    }
}

public record RateLimitDenial(string Window, int Limit, int Current, int RetryAfterSeconds);
public record EndpointRateLimit(string Key, int PerSecond, int PerMinute, int PerHour);

public class SlidingWindowCounter(TimeProvider? timeProvider = null)
{
    private readonly Queue<DateTimeOffset> _timestamps = new();
    private readonly object _lock = new();
    private DateTimeOffset _blockedUntil;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public void DeferUntil(DateTimeOffset deadline)
    {
        lock (_lock)
            if (deadline > _blockedUntil) _blockedUntil = deadline;
    }

    public RateLimitDenial? TryConsume(EndpointRateLimit limit)
    {
        lock (_lock)
        {
            var now = _clock.GetUtcNow();
            var retention = limit.PerHour != int.MaxValue ? TimeSpan.FromHours(1)
                : limit.PerMinute != int.MaxValue ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(1);
            while (_timestamps.TryPeek(out var oldest) && oldest <= now - retention)
                _timestamps.Dequeue();

            RateLimitDenial? denial = _blockedUntil > now
                ? new("Retry-After", 0, 0, Seconds(_blockedUntil - now)) : null;
            Check("second", TimeSpan.FromSeconds(1), limit.PerSecond);
            Check("minute", TimeSpan.FromMinutes(1), limit.PerMinute);
            Check("hour", TimeSpan.FromHours(1), limit.PerHour);
            if (denial != null) return denial;
            _timestamps.Enqueue(now);
            return null;

            void Check(string window, TimeSpan duration, int maximum)
            {
                if (maximum == int.MaxValue) return;
                var timestamps = _timestamps.Where(t => t > now - duration).ToArray();
                if (timestamps.Length < maximum) return;
                var retry = Seconds(timestamps[0] + duration - now);
                if (denial == null || retry > denial.RetryAfterSeconds)
                    denial = new(window, maximum, timestamps.Length, retry);
            }
        }
    }

    private static int Seconds(TimeSpan duration) => Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds));
}

/// <summary>Default production limits from CIRFMF limity/limity-api.md and API 2.8.0.</summary>
public static class KsefRateLimits
{
    // Paths here are upstream API resources, never caller-facing gateway URLs.
    public static EndpointRateLimit GetLimit(string path)
    {
        var parts = path.ToLowerInvariant().Trim('/').Split('/');
        return parts switch
        {
            ["invoices", "query", "metadata"] => new("invoices/query", 8, 16, 20),
            ["invoices", "exports"] => new("invoices/exports", 8, 16, 20),
            ["invoices", "exports", _] => new("invoices/exports/status", 10, 60, 600),
            ["invoices", "ksef", _] => new("invoices/get", 8, 16, 64),
            ["sessions", "online"] => new("sessions/online/open", 10, 30, 120),
            ["sessions", "online", _, "invoices"] => new("sessions/online/send", 10, 30, 180),
            ["sessions", "online", _, "close"] => new("sessions/online/close", 20, 60, 240),
            ["sessions", "batch"] => new("sessions/batch/open", 10, 20, 60),
            ["sessions", "batch", _, "close"] => new("sessions/batch/close", 20, 40, 120),
            ["sessions"] => new("sessions/list", 5, 10, 60),
            ["sessions", _, "invoices"] or ["sessions", _, "invoices", "failed"] => new("sessions/status/invoices", 10, 20, 200),
            ["sessions", _, "invoices", _] => new("sessions/status/invoice", 30, 120, 1200),
            ["sessions", ..] => new("sessions/misc", 10, 120, 1200),
            ["collective-identifiers", ..] => new("collective-identifiers", 20, 120, 240),
            ["auth", "sessions", ..] => Other(parts),
            ["auth", ..] or ["security", "public-key-certificates"] or ["peppol", "query"] => Anonymous(),
            ["testdata", "subject" or "person" or "permissions" or "attachment", ..] => Anonymous(),
            _ => Other(parts)
        };
    }

    private static EndpointRateLimit Anonymous() => new("anonymous", 60, int.MaxValue, int.MaxValue);

    private static EndpointRateLimit Other(string[] parts)
    {
        // Each remaining resource has its own budget; references must not create new buckets.
        var resource = parts switch
        {
            ["auth", "sessions", "current"] => "auth/sessions/current",
            ["auth", "sessions", _] => "auth/sessions/{id}",
            ["certificates", "enrollments", not "data"] => "certificates/enrollments/{id}",
            ["certificates", _, "revoke"] => "certificates/{id}/revoke",
            ["testdata", "certificates", _] => "testdata/certificates/{id}",
            ["permissions", "common" or "authorizations", "grants", _] => $"permissions/{parts[1]}/grants/{{id}}",
            ["permissions", "operations", _] => "permissions/operations/{id}",
            ["tokens", _] => "tokens/{id}",
            _ => string.Join('/', parts)
        };
        return new("other/" + resource, 10, 30, 120);
    }
}
