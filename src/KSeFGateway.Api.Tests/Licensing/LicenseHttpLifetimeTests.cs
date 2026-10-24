using System.Net;
using KSeFGateway.Api.Licensing;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Licensing;

public class LicenseHttpLifetimeTests
{
    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task Fetch_DisposesResponse(bool jwks, HttpStatusCode status)
    {
        var content = new TrackingContent(jwks ? "{\"keys\":[]}" : "{\"order_hashes\":[]}");
        using var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(status) { Content = content }));
        if (jwks)
            await new JwksClient(http, LicenseService.DefaultJwksUrl, NullLogger<JwksClient>.Instance).GetKeysAsync();
        else
            await new RevocationClient(http, LicenseService.DefaultRevocationUrl, NullLogger<RevocationClient>.Instance).IsRevokedAsync("order");
        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Client_HasTenSecondTimeout(bool jwks)
    {
        using var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        if (jwks)
            _ = new JwksClient(http, LicenseService.DefaultJwksUrl, NullLogger<JwksClient>.Instance);
        else
            _ = new RevocationClient(http, LicenseService.DefaultRevocationUrl, NullLogger<RevocationClient>.Instance);
        Assert.Equal(TimeSpan.FromSeconds(10), http.Timeout);
    }

    private sealed class TrackingContent(string value) : StringContent(value)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
