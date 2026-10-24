using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KSeFGateway.Api.Tests.Integration;

public class MetadataQueryTests
{
    [Fact]
    public async Task OneSuccessfulQuery_DoesNotPollForIndexing()
    {
        var calls = 0;
        var result = await MetadataQuery.ReadAsync(() => {
            calls++;
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":{\"invoices\":[{\"invoiceNumber\":\"shared\"}]}}"));
        });
        Assert.Equal(new[] { "shared" }, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ShortRetryAfter_WaitsExactlyOnceThenQueries()
    {
        var calls = 0;
        var waits = new List<TimeSpan>();
        var result = await MetadataQuery.ReadAsync(() => {
            calls++;
            var response = Response(calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK,
                "{\"data\":{\"invoices\":[]}}");
            if (calls == 1) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return Task.FromResult(response);
        }, d => { waits.Add(d); return Task.CompletedTask; });
        Assert.Empty(result);
        Assert.Equal(2, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, waits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExhaustedQuota_ReportsUnblockTimeWithoutRetry(bool delta)
    {
        var now = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        var calls = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MetadataQuery.ReadAsync(() => {
            calls++;
            var response = Response(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = delta ? new RetryConditionHeaderValue(TimeSpan.FromMinutes(39))
                : new RetryConditionHeaderValue(now.AddMinutes(39));
            return Task.FromResult(response);
        }, _ => throw new Exception("must not wait"), () => now));
        Assert.Equal(1, calls);
        Assert.Equal("limit KSeF TEST wyczerpany, spróbuj po 12:39 UTC", error.Message);
    }

    [Fact]
    public async Task RepeatedShortThrottles_HaveBoundedTotalWait()
    {
        var calls = 0;
        var waits = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => MetadataQuery.ReadAsync(() => {
            calls++;
            var response = Response(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(15));
            return Task.FromResult(response);
        }, _ => { waits++; return Task.CompletedTask; }));
        Assert.Equal(2, calls);
        Assert.Equal(1, waits);
    }

    [Fact]
    public async Task UpstreamError_IsNotRetried()
    {
        var calls = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() => MetadataQuery.ReadAsync(() => {
            calls++;
            return Task.FromResult(Response(HttpStatusCode.BadGateway, "{}"));
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SharedFixture_GetXmlGetPdfLists_UsesOneSendAndTwoMetadataCalls()
    {
        var sends = 0;
        var queries = new List<string>();
        var fixture = new GatewayIntegrationTests.SharedInvoiceFixture(() => {
            sends++;
            return Task.FromResult(new GatewayIntegrationTests.SharedInvoice("shared", "number", "from", "to"));
        }, url => {
            queries.Add(url);
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":{\"invoices\":[{\"invoiceNumber\":\"shared\"}]}}"));
        });
        // XML/PDF and buyer/seller tests may access the fixture in any order.
        await Task.WhenAll(fixture.Invoice.Value, fixture.Invoice.Value,
            fixture.Received.Value, fixture.Issued.Value, fixture.Received.Value, fixture.Issued.Value);
        Assert.Equal(1, sends);
        Assert.Equal(2, queries.Count);
        Assert.Single(queries, url => url.Contains("/received?"));
        Assert.Single(queries, url => url.Contains("/issued?"));
        Assert.Equal(new[] { "shared" }, await fixture.Received.Value);
        Assert.Equal(new[] { "shared" }, await fixture.Issued.Value);
    }

    [Fact]
    public async Task MissingRetryAfter_FailsClearlyWithoutWaiting()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MetadataQuery.RequestAsync(
            () => Task.FromResult(Response(HttpStatusCode.TooManyRequests, "{}")),
            _ => throw new Exception("must not wait")));
        Assert.Contains("limit KSeF TEST wyczerpany, brak Retry-After", error.Message);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
}
