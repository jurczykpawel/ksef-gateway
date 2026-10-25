using System.Net;
using System.Net.Http.Headers;
using Xunit.Sdk;

namespace KSeFGateway.Api.Tests.Integration;

public class InvoiceSubmissionDiagnosticsTests
{
    [Fact]
    public async Task SendQuotaExhausted_PreservesBodyAndReportsUnblockTime()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("session quota exhausted")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.Parse("2026-10-25T12:39:00Z"));
        var error = await Assert.ThrowsAsync<FailException>(() => GatewayIntegrationTests.AssertOkAsync(response));
        Assert.Contains("limit KSeF TEST wyczerpany, spróbuj po 12:39 UTC", error.Message);
        Assert.Contains("429", error.Message);
        Assert.Contains("session quota exhausted", error.Message);
    }

    [Fact]
    public async Task SendQuotaWithoutRetryAfter_PreservesBodyAndReportsMissingHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("upstream rejection")
        };
        var error = await Assert.ThrowsAsync<FailException>(() => GatewayIntegrationTests.AssertOkAsync(response));
        Assert.Contains("limit KSeF TEST wyczerpany, brak Retry-After", error.Message);
        Assert.Contains("upstream rejection", error.Message);
    }

    [Theory]
    [InlineData(2, "12:00")]
    [InlineData(2340, "12:39")]
    public async Task SendQuotaDelta_ReportsTimeWithoutWaitingOrRetrying(int seconds, string time)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("quota")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        var error = await Assert.ThrowsAsync<FailException>(() => GatewayIntegrationTests.AssertOkAsync(response,
            () => DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        Assert.Equal($"limit KSeF TEST wyczerpany, spróbuj po {time} UTC; HTTP 429: quota", error.Message);
    }

    [Fact]
    public async Task BusinessRejection_PreservesExactResponse()
    {
        const string body = "{\"code\":450,\"description\":\"Błąd biznesowy\"}";
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(body) };
        var error = await Assert.ThrowsAsync<FailException>(() => GatewayIntegrationTests.AssertOkAsync(response));
        Assert.Equal($"Expected 200 OK but got 502 BadGateway: {body}", error.Message);
    }
}
