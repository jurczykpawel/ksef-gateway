using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using KSeFGateway.Api.Licensing;

namespace KSeFGateway.Api.Tests.Licensing;

public class LicenseServiceTests
{
    private const string JwksUrl = "https://sellf.example.com/api/licenses/jwks?seller=test";
    private const string RevocationUrl = "https://sellf.example.com/api/licenses/revoked?seller=test";
    private const string Kid = "test-kid";

    private static (string PublicKeyPem, ECDsa PrivateKey) GenerateKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfoPem(), ecdsa);
    }

    private static string SignToken(ECDsa privateKey, object claims)
    {
        var payloadJson = JsonSerializer.Serialize(claims);
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var sig = privateKey.SignData(Encoding.UTF8.GetBytes(payloadB64), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return $"{payloadB64}.{Base64UrlEncode(sig)}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static object ValidClaims(long? exp = null, string product = LicenseService.ProductSlug, string order = "order-1") => new
    {
        v = 1,
        kid = Kid,
        product,
        email = "buyer@example.com",
        order,
        tier = "unlimited",
        iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        exp,
    };

    private static LicenseService MakeService(
        string? licenseToken,
        Func<HttpRequestMessage, HttpResponseMessage>? jwksResponder = null,
        Func<HttpRequestMessage, HttpResponseMessage>? revocationResponder = null)
    {
        var jwksHandler = new FakeHttpMessageHandler(jwksResponder ?? (_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var revocationHandler = new FakeHttpMessageHandler(revocationResponder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"order_hashes":[]}"""),
        }));

        var jwksClient = new JwksClient(new HttpClient(jwksHandler), JwksUrl, NullLogger<JwksClient>.Instance);
        var revocationClient = new RevocationClient(new HttpClient(revocationHandler), RevocationUrl, NullLogger<RevocationClient>.Instance);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(licenseToken is null ? [] : new Dictionary<string, string?> { ["GATEWAY_LICENSE"] = licenseToken })
            .Build();

        return new LicenseService(jwksClient, revocationClient, config, NullLogger<LicenseService>.Instance);
    }

    private static HttpResponseMessage JwksOk(string publicKeyPem) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            keys = new[] { new { kid = Kid, alg = "ES256", pem = publicKeyPem } },
        })),
    };

    [Fact]
    public async Task RefreshAsync_NoLicenseConfigured_IsFreeTier()
    {
        var service = MakeService(licenseToken: null);

        await service.RefreshAsync();

        Assert.False(service.IsLicensed);
        Assert.Equal(LicenseService.FreeMaxNips, service.MaxNips);
    }

    [Fact]
    public async Task RefreshAsync_ValidLicense_IsUnlimited()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims());
        var service = MakeService(token, jwksResponder: _ => JwksOk(publicKeyPem));

        await service.RefreshAsync();

        Assert.True(service.IsLicensed);
        Assert.Equal(int.MaxValue, service.MaxNips);
        Assert.Equal("order-1", service.Claims!.Order);
    }

    [Fact]
    public async Task RefreshAsync_MalformedToken_IsFreeTier()
    {
        var service = MakeService("not-a-valid-token-at-all");

        await service.RefreshAsync();

        Assert.False(service.IsLicensed);
        Assert.Equal(LicenseService.FreeMaxNips, service.MaxNips);
    }

    [Fact]
    public async Task RefreshAsync_WrongProduct_IsFreeTier()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims(product: "some-other-product"));
        var service = MakeService(token, jwksResponder: _ => JwksOk(publicKeyPem));

        await service.RefreshAsync();

        Assert.False(service.IsLicensed);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredLicense_IsFreeTier()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims(exp: DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()));
        var service = MakeService(token, jwksResponder: _ => JwksOk(publicKeyPem));

        await service.RefreshAsync();

        Assert.False(service.IsLicensed);
    }

    [Fact]
    public async Task RefreshAsync_RevokedLicense_IsFreeTierEvenThoughSignatureValid()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims(order: "revoked-order"));
        var revokedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("revoked-order"))).ToLowerInvariant();
        var service = MakeService(
            token,
            jwksResponder: _ => JwksOk(publicKeyPem),
            revocationResponder: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"order_hashes":["{{revokedHash}}"]}"""),
            });

        await service.RefreshAsync();

        Assert.False(service.IsLicensed);
    }

    [Fact]
    public async Task RefreshAsync_JwksUnreachable_FailsClosedToFreeTier()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims());
        // jwksResponder defaults to 503 (unreachable) via MakeService's default
        var service = MakeService(token);

        await service.RefreshAsync();

        Assert.False(service.IsLicensed);
        Assert.Equal(LicenseService.FreeMaxNips, service.MaxNips);
    }

    [Fact]
    public async Task RefreshAsync_KidNotInJwks_IsFreeTier()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims());
        var service = MakeService(token, jwksResponder: _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"keys":[{"kid":"different-kid","alg":"ES256","pem":"whatever"}]}"""),
        });

        await service.RefreshAsync();

        Assert.False(service.IsLicensed);
    }
}
