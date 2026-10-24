using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection.Extensions;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.DI;
using KSeFGateway.Api.Auth;

namespace KSeFGateway.Api.Middleware;

/// <summary>Shared by every SDK client/DI scope in this process (one replica, one egress IP).</summary>
public sealed class KsefOperationLimiter(TimeProvider clock)
{
    private readonly ConcurrentDictionary<(string Context, string Operation), SlidingWindowCounter> _counters = new();

    public SlidingWindowCounter Counter(string nip, EndpointRateLimit limit, string method) =>
        _counters.GetOrAdd((limit.Key == "anonymous" ? "ip" : nip,
            limit.Key.StartsWith("other/") ? method + " " + limit.Key : limit.Key), _ => new(clock));
}

/// <summary>Counts every SDK HTTP attempt, including each page, status poll and retry.</summary>
public sealed class KsefRateLimitHandler(KsefOperationLimiter limiter, TokenPool pool, TimeProvider clock, KSeFClientOptions options, IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Batch parts and export files may use signed URLs on external storage.
        if (Uri.Compare(request.RequestUri, new Uri(options.BaseUrl), UriComponents.SchemeAndServer,
                UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
            return await base.SendAsync(request, cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        // The SDK's API version is part of its transport URL, not the resource category.
        if (path.StartsWith("/v2/", StringComparison.OrdinalIgnoreCase)) path = path[3..];
        var limit = KsefRateLimits.GetLimit(path);
        var nip = limit.Key == "anonymous" ? "ip"
            : httpContextAccessor.HttpContext?.Items[TokenPool.RateLimitContextKey] as string
                ?? pool.FindNipByAccessToken(request.Headers.Authorization?.Parameter)
                ?? throw new InvalidOperationException("Cannot identify KSeF context for rate limiting.");
        var counter = limiter.Counter(nip, limit, request.Method.Method);
        if (counter.TryConsume(limit) is { } denial)
            throw new KsefRateLimitException($"Rate limit for {limit.Key}", denial.RetryAfterSeconds, null, null!);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            if (retry?.Delta is { } delay) counter.DeferUntil(clock.GetUtcNow() + delay);
            else if (retry?.Date is { } deadline) counter.DeferUntil(deadline);
        }
        return response;
    }
}

public static class KsefRateLimitRegistration
{
    public static IHttpClientBuilder AddKsefOperationRateLimiting(this IHttpClientBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<KsefOperationLimiter>();
        builder.Services.AddTransient<KsefRateLimitHandler>();
        return builder.AddHttpMessageHandler<KsefRateLimitHandler>();
    }
}
