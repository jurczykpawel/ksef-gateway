using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using KSeFGateway.Api.Licensing;

namespace KSeFGateway.Api.Tests.Licensing;

public class RevocationClientTests
{
    private const string BaseUrl = "https://sellf.example.com/api/licenses/revoked?seller=test";
    private const string Order = "order-123";

    private static string OrderHashHex =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Order))).ToLowerInvariant();

    private static RevocationClient MakeClient(FakeHttpMessageHandler handler, TimeSpan? freshTtl = null, TimeSpan? staleTtl = null)
    {
        var http = new HttpClient(handler);
        return new RevocationClient(http, BaseUrl, NullLogger<RevocationClient>.Instance, freshTtl, staleTtl);
    }

    [Fact]
    public async Task IsRevokedAsync_HashInReturnedBucket_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"order_hashes":["{{OrderHashHex}}"]}"""),
        });
        var client = MakeClient(handler);

        var revoked = await client.IsRevokedAsync(Order);

        Assert.True(revoked);
    }

    [Fact]
    public async Task IsRevokedAsync_HashNotInReturnedBucket_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"order_hashes":["0000000000000000000000000000000000000000000000000000000000000000"]}"""),
        });
        var client = MakeClient(handler);

        var revoked = await client.IsRevokedAsync(Order);

        Assert.False(revoked);
    }

    [Fact]
    public async Task IsRevokedAsync_SendsLowercaseHexPrefix()
    {
        string? capturedUrl = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"order_hashes":[]}""") };
        });
        var client = MakeClient(handler);

        await client.IsRevokedAsync(Order);

        Assert.NotNull(capturedUrl);
        var prefix = OrderHashHex[..4];
        Assert.Contains($"prefix={prefix}", capturedUrl);
        Assert.Equal(prefix, prefix.ToLowerInvariant());
    }

    [Fact]
    public async Task IsRevokedAsync_ServerUnreachableAndNoCache_FailsOpenReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = MakeClient(handler);

        var revoked = await client.IsRevokedAsync(Order);

        Assert.False(revoked);
    }

    [Fact]
    public async Task IsRevokedAsync_NetworkExceptionAndNoCache_FailsOpenReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var client = MakeClient(handler);

        var revoked = await client.IsRevokedAsync(Order);

        Assert.False(revoked);
    }

    [Fact]
    public async Task IsRevokedAsync_RefetchFailsButWithinStaleGrace_ServesStaleCache()
    {
        var succeed = true;
        var handler = new FakeHttpMessageHandler(_ => succeed
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"order_hashes":["{{OrderHashHex}}"]}""") }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = MakeClient(handler, freshTtl: TimeSpan.FromMilliseconds(20), staleTtl: TimeSpan.FromSeconds(30));

        var first = await client.IsRevokedAsync(Order);
        Assert.True(first);

        await Task.Delay(50);
        succeed = false;
        var second = await client.IsRevokedAsync(Order);

        Assert.True(second); // stale cache says this order is still revoked
    }

    [Fact]
    public async Task IsRevokedAsync_WithinFreshTtl_DoesNotRefetch()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"order_hashes":[]}"""),
        });
        var client = MakeClient(handler, freshTtl: TimeSpan.FromMinutes(5));

        await client.IsRevokedAsync(Order);
        await client.IsRevokedAsync(Order);

        Assert.Equal(1, handler.CallCount);
    }
}
