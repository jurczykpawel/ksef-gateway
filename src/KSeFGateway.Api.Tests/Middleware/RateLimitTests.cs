using KSeFGateway.Api.Middleware;

namespace KSeFGateway.Api.Tests.Middleware;

public class SlidingWindowCounterTests
{
    [Fact]
    public void TryConsume_FirstRequest_Succeeds()
    {
        var counter = new SlidingWindowCounter();
        var limit = new EndpointRateLimit("test", 10, 30, 120);

        var result = counter.TryConsume(limit);

        Assert.Null(result);
    }

    [Fact]
    public void TryConsume_ExceedsPerSecond_ReturnsDenial()
    {
        var counter = new SlidingWindowCounter();
        var limit = new EndpointRateLimit("test", 2, 100, 1000);

        counter.TryConsume(limit); // 1
        counter.TryConsume(limit); // 2
        var result = counter.TryConsume(limit); // 3 - should be denied

        Assert.NotNull(result);
        Assert.Equal("second", result.Window);
        Assert.Equal(2, result.Limit);
    }

    [Fact]
    public void TryConsume_ExceedsPerMinute_ReturnsDenial()
    {
        var counter = new SlidingWindowCounter();
        var limit = new EndpointRateLimit("test", 100, 3, 1000); // high per-second, low per-minute

        counter.TryConsume(limit);
        counter.TryConsume(limit);
        counter.TryConsume(limit);
        var result = counter.TryConsume(limit);

        Assert.NotNull(result);
        Assert.Equal("minute", result.Window);
        Assert.Equal(3, result.Limit);
    }

    [Fact]
    public void TryConsume_WithinLimits_AllSucceed()
    {
        var counter = new SlidingWindowCounter();
        var limit = new EndpointRateLimit("test", 10, 30, 120);

        for (int i = 0; i < 10; i++)
        {
            var result = counter.TryConsume(limit);
            Assert.Null(result);
        }
    }

    [Fact]
    public void TryConsume_RetryAfterSeconds_IsPositive()
    {
        var counter = new SlidingWindowCounter();
        var limit = new EndpointRateLimit("test", 1, 1, 1000);

        counter.TryConsume(limit);
        var result = counter.TryConsume(limit);

        Assert.NotNull(result);
        Assert.True(result.RetryAfterSeconds >= 1);
    }
}

public class KsefRateLimitsTests
{
    [Theory]
    [InlineData("/invoices/query/metadata", "invoices/query", 8, 16, 20)]
    [InlineData("/INVOICES/query/metadata", "invoices/query", 8, 16, 20)]
    [InlineData("/invoices/exports", "invoices/exports", 8, 16, 20)]
    [InlineData("/invoices/exports/ref", "invoices/exports/status", 10, 60, 600)]
    [InlineData("/invoices/ksef/number", "invoices/get", 8, 16, 64)]
    [InlineData("/sessions/online", "sessions/online/open", 10, 30, 120)]
    [InlineData("/sessions/online/ref/invoices", "sessions/online/send", 10, 30, 180)]
    [InlineData("/sessions/online/ref/close", "sessions/online/close", 20, 60, 240)]
    [InlineData("/sessions/batch", "sessions/batch/open", 10, 20, 60)]
    [InlineData("/sessions/batch/ref/close", "sessions/batch/close", 20, 40, 120)]
    [InlineData("/sessions/ref/invoices/invoice", "sessions/status/invoice", 30, 120, 1200)]
    [InlineData("/sessions/ref/invoices/failed", "sessions/status/invoices", 10, 20, 200)]
    [InlineData("/sessions/ref/invoices", "sessions/status/invoices", 10, 20, 200)]
    [InlineData("/sessions", "sessions/list", 5, 10, 60)]
    [InlineData("/sessions/ref/upo/upo", "sessions/misc", 10, 120, 1200)]
    [InlineData("/collective-identifiers/query", "collective-identifiers", 20, 120, 240)]
    [InlineData("/auth/challenge", "anonymous", 60, int.MaxValue, int.MaxValue)]
    [InlineData("/security/public-key-certificates", "anonymous", 60, int.MaxValue, int.MaxValue)]
    public void GetLimit_UsesOfficialOperationCategory(string path, string key, int second, int minute, int hour)
    {
        Assert.Equal(new EndpointRateLimit(key, second, minute, hour), KsefRateLimits.GetLimit(path));
    }
}
