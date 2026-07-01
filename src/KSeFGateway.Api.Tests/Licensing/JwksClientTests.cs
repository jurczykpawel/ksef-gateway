using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using KSeFGateway.Api.Licensing;

namespace KSeFGateway.Api.Tests.Licensing;

public class JwksClientTests
{
    private const string JwksUrl = "https://sellf.example.com/api/licenses/jwks?seller=test";

    private const string ValidJwksResponse = """
        {"keys":[{"kid":"abc123","alg":"ES256","pem":"-----BEGIN PUBLIC KEY-----\ntest\n-----END PUBLIC KEY-----\n"}]}
        """;

    private static JwksClient MakeClient(FakeHttpMessageHandler handler, TimeSpan? freshTtl = null, TimeSpan? staleTtl = null)
    {
        var http = new HttpClient(handler);
        return new JwksClient(http, JwksUrl, NullLogger<JwksClient>.Instance, freshTtl, staleTtl);
    }

    [Fact]
    public async Task GetKeysAsync_SuccessfulFetch_ReturnsKeys()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidJwksResponse),
        });
        var client = MakeClient(handler);

        var keys = await client.GetKeysAsync();

        Assert.NotNull(keys);
        Assert.Single(keys!);
        Assert.Equal("abc123", keys![0].Kid);
    }

    [Fact]
    public async Task GetKeysAsync_WithinFreshTtl_DoesNotRefetch()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidJwksResponse),
        });
        var client = MakeClient(handler, freshTtl: TimeSpan.FromMinutes(5));

        await client.GetKeysAsync();
        await client.GetKeysAsync();

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetKeysAsync_FetchFailsAndNoCache_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = MakeClient(handler);

        var keys = await client.GetKeysAsync();

        Assert.Null(keys);
    }

    [Fact]
    public async Task GetKeysAsync_NetworkExceptionAndNoCache_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var client = MakeClient(handler);

        var keys = await client.GetKeysAsync();

        Assert.Null(keys);
    }

    [Fact]
    public async Task GetKeysAsync_RefetchFailsButWithinStaleGrace_ServesStaleCache()
    {
        var succeed = true;
        var handler = new FakeHttpMessageHandler(_ => succeed
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidJwksResponse) }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = MakeClient(handler, freshTtl: TimeSpan.FromMilliseconds(20), staleTtl: TimeSpan.FromSeconds(30));

        var first = await client.GetKeysAsync();
        Assert.NotNull(first);

        await Task.Delay(50); // cross fresh TTL, still within stale grace
        succeed = false;
        var second = await client.GetKeysAsync();

        Assert.NotNull(second);
        Assert.Equal("abc123", second![0].Kid);
    }

    [Fact]
    public async Task GetKeysAsync_RefetchFailsAndPastStaleGrace_ReturnsNull()
    {
        var succeed = true;
        var handler = new FakeHttpMessageHandler(_ => succeed
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ValidJwksResponse) }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = MakeClient(handler, freshTtl: TimeSpan.FromMilliseconds(10), staleTtl: TimeSpan.FromMilliseconds(40));

        var first = await client.GetKeysAsync();
        Assert.NotNull(first);

        await Task.Delay(80); // cross both fresh TTL and stale grace
        succeed = false;
        var second = await client.GetKeysAsync();

        Assert.Null(second);
    }

    [Fact]
    public async Task GetKeysAsync_EmptyKeysArray_TreatedAsFetchFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"keys":[]}"""),
        });
        var client = MakeClient(handler);

        var keys = await client.GetKeysAsync();

        Assert.Null(keys);
    }
}

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(respond(request));
    }
}
