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
    [InlineData("/ksef/send", "sessions/online/send")]
    [InlineData("/ksef/send/json", "sessions/online/send")]
    [InlineData("/ksef/invoice/123-456/pdf", "invoices/get")]
    [InlineData("/ksef/invoice-download/get-invoice", "invoices/get")]
    [InlineData("/ksef/invoice-download/query-invoice-metadata", "invoices/query")]
    [InlineData("/ksef/invoice-download/export-invoices", "invoices/exports")]
    [InlineData("/ksef/online-session/open-online-session", "sessions/online/open")]
    [InlineData("/ksef/online-session/send-online-session-invoice", "sessions/online/send")]
    [InlineData("/ksef/online-session/close-online-session", "sessions/online/close")]
    [InlineData("/ksef/batch-session/open-batch-session", "sessions/batch/open")]
    [InlineData("/ksef/batch-session/close-batch-session", "sessions/batch/close")]
    [InlineData("/ksef/session-status/get-sessions", "sessions/list")]
    [InlineData("/ksef/ksef-token/generate-ksef-token", "default")]
    [InlineData("/ksef/status", "default")]
    public void GetLimit_ReturnsCorrectKey(string path, string expectedKey)
    {
        var limit = KsefRateLimits.GetLimit(path);
        Assert.Equal(expectedKey, limit.Key);
    }

    [Fact]
    public void GetLimit_InvoiceSend_Has30PerMinute()
    {
        var limit = KsefRateLimits.GetLimit("/ksef/send");
        Assert.Equal(30, limit.PerMinute);
        Assert.Equal(180, limit.PerHour);
    }

    [Fact]
    public void GetLimit_InvoiceGet_Has16PerMinute()
    {
        var limit = KsefRateLimits.GetLimit("/ksef/invoice-download/get-invoice");
        Assert.Equal(16, limit.PerMinute);
        Assert.Equal(64, limit.PerHour);
    }

    [Fact]
    public void GetLimit_Default_Has30PerMinute()
    {
        var limit = KsefRateLimits.GetLimit("/ksef/some-unknown-endpoint");
        Assert.Equal(30, limit.PerMinute);
        Assert.Equal(120, limit.PerHour);
    }
}
