using System.Collections.Concurrent;
using KSeFGateway.Api.Models;

namespace KSeFGateway.Api.Middleware;

/// <summary>
/// Client-side rate limiter that proactively prevents hitting KSeF's rate limits.
/// Uses sliding window counters per SDK endpoint pattern.
/// Returns 429 with Retry-After BEFORE calling KSeF (avoids escalating penalties).
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly ConcurrentDictionary<string, SlidingWindowCounter> _counters = new();

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only rate-limit /ksef/* endpoints (not /health, /scalar, etc.)
        var path = context.Request.Path.Value;
        if (path is null || !path.StartsWith("/ksef/"))
        {
            await _next(context);
            return;
        }

        var limit = KsefRateLimits.GetLimit(path);
        var counter = _counters.GetOrAdd(limit.Key, _ => new SlidingWindowCounter());

        var denial = counter.TryConsume(limit);
        if (denial is { } d)
        {
            _logger.LogWarning(
                "Rate limit would be exceeded for {Key}: {Window} limit {Limit} (current: {Current}). Retry after {RetryAfter}s",
                limit.Key, d.Window, d.Limit, d.Current, d.RetryAfterSeconds);

            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = d.RetryAfterSeconds.ToString();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail($"Rate limit for {limit.Key}: {d.Current}/{d.Limit} per {d.Window}. Retry after {d.RetryAfterSeconds}s"));
            return;
        }

        await _next(context);
    }
}

public record RateLimitDenial(string Window, int Limit, int Current, int RetryAfterSeconds);

public record EndpointRateLimit(string Key, int PerSecond, int PerMinute, int PerHour);

public class SlidingWindowCounter
{
    private readonly ConcurrentQueue<DateTimeOffset> _timestamps = new();
    private readonly object _lock = new();

    public RateLimitDenial? TryConsume(EndpointRateLimit limit)
    {
        var now = DateTimeOffset.UtcNow;
        Cleanup(now);

        lock (_lock)
        {
            var timestamps = _timestamps.ToArray();

            // Check per-second
            var lastSecond = timestamps.Count(t => t > now.AddSeconds(-1));
            if (lastSecond >= limit.PerSecond)
                return new RateLimitDenial("second", limit.PerSecond, lastSecond, 1);

            // Check per-minute
            var lastMinute = timestamps.Count(t => t > now.AddMinutes(-1));
            if (lastMinute >= limit.PerMinute)
            {
                var oldestInWindow = timestamps.Where(t => t > now.AddMinutes(-1)).Min();
                var retryAfter = (int)Math.Ceiling((oldestInWindow.AddMinutes(1) - now).TotalSeconds);
                return new RateLimitDenial("minute", limit.PerMinute, lastMinute, Math.Max(1, retryAfter));
            }

            // Check per-hour
            var lastHour = timestamps.Count(t => t > now.AddHours(-1));
            if (lastHour >= limit.PerHour)
            {
                var oldestInWindow = timestamps.Where(t => t > now.AddHours(-1)).Min();
                var retryAfter = (int)Math.Ceiling((oldestInWindow.AddHours(1) - now).TotalSeconds);
                return new RateLimitDenial("hour", limit.PerHour, lastHour, Math.Max(1, retryAfter));
            }

            // Within limits - record this request
            _timestamps.Enqueue(now);
            return null;
        }
    }

    private void Cleanup(DateTimeOffset now)
    {
        var cutoff = now.AddHours(-1);
        while (_timestamps.TryPeek(out var oldest) && oldest < cutoff)
            _timestamps.TryDequeue(out _);
    }
}

/// <summary>
/// KSeF API rate limits from official documentation.
/// https://github.com/CIRFMF/ksef-docs/blob/main/limity/limity-api.md
/// </summary>
public static class KsefRateLimits
{
    private static readonly EndpointRateLimit InvoiceQuery = new("invoices/query", 8, 16, 20);
    private static readonly EndpointRateLimit InvoiceExport = new("invoices/exports", 4, 8, 20);
    private static readonly EndpointRateLimit InvoiceExportStatus = new("invoices/exports/status", 10, 60, 600);
    private static readonly EndpointRateLimit InvoiceGet = new("invoices/get", 8, 16, 64);

    private static readonly EndpointRateLimit SessionOnlineOpen = new("sessions/online/open", 10, 30, 120);
    private static readonly EndpointRateLimit SessionOnlineSend = new("sessions/online/send", 10, 30, 180);
    private static readonly EndpointRateLimit SessionOnlineClose = new("sessions/online/close", 10, 30, 120);

    private static readonly EndpointRateLimit SessionBatchOpen = new("sessions/batch/open", 10, 20, 60);
    private static readonly EndpointRateLimit SessionBatchClose = new("sessions/batch/close", 10, 20, 60);

    private static readonly EndpointRateLimit SessionStatusInvoice = new("sessions/status/invoice", 30, 120, 1200);
    private static readonly EndpointRateLimit SessionStatusList = new("sessions/list", 5, 10, 60);
    private static readonly EndpointRateLimit SessionStatusInvoices = new("sessions/status/invoices", 10, 20, 200);

    private static readonly EndpointRateLimit Default = new("default", 10, 30, 120);

    public static EndpointRateLimit GetLimit(string path)
    {
        // Workflow endpoints map to their underlying SDK calls
        if (path.Contains("/ksef/send"))
            return SessionOnlineSend; // Most restrictive operation in the flow

        if (path.Contains("/ksef/invoice") && path.EndsWith("/pdf"))
            return InvoiceGet;

        // Auto-discovered SDK endpoints
        if (path.Contains("/invoice-download/query-invoice-metadata"))
            return InvoiceQuery;
        if (path.Contains("/invoice-download/export-invoices"))
            return InvoiceExport;
        if (path.Contains("/invoice-download/get-invoice-export-status"))
            return InvoiceExportStatus;
        if (path.Contains("/invoice-download/get-invoice"))
            return InvoiceGet;

        if (path.Contains("/online-session/open"))
            return SessionOnlineOpen;
        if (path.Contains("/online-session/send"))
            return SessionOnlineSend;
        if (path.Contains("/online-session/close"))
            return SessionOnlineClose;

        if (path.Contains("/batch-session/open"))
            return SessionBatchOpen;
        if (path.Contains("/batch-session/close"))
            return SessionBatchClose;

        if (path.Contains("/session-status/get-session-invoice/"))
            return SessionStatusInvoice;
        if (path.Contains("/session-status/get-sessions"))
            return SessionStatusList;
        if (path.Contains("/session-status/get-session-invoices"))
            return SessionStatusInvoices;

        if (path.Contains("/ksef/invoice/"))
            return InvoiceGet;

        return Default;
    }
}
