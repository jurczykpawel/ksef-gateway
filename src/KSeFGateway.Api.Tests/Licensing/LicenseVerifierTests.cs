using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KSeFGateway.Api.Licensing;

namespace KSeFGateway.Api.Tests.Licensing;

public class LicenseVerifierTests
{
    private const string ExpectedProduct = "ksef-gateway-multi-nip";

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

    private static object ValidClaims(long? exp = null, string product = ExpectedProduct) => new
    {
        v = 1,
        kid = "test-kid",
        product,
        email = "buyer@example.com",
        order = "order-123",
        tier = "unlimited",
        iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        exp,
    };

    [Fact]
    public void Verify_ValidSignatureNoExpiry_ReturnsValid()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims());

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.True(result.Valid);
        Assert.NotNull(result.Claims);
        Assert.Equal("order-123", result.Claims!.Order);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Verify_ValidSignatureFutureExpiry_ReturnsValid()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var futureExp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var token = SignToken(privateKey, ValidClaims(exp: futureExp));

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.True(result.Valid);
    }

    [Fact]
    public void Verify_ExpiredClaim_ReturnsExpired()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var pastExp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var token = SignToken(privateKey, ValidClaims(exp: pastExp));

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.Expired, result.Reason);
    }

    [Fact]
    public void Verify_WrongProductClaim_ReturnsWrongProduct()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims(product: "some-other-product"));

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.WrongProduct, result.Reason);
    }

    [Fact]
    public void Verify_SignedWithDifferentKey_ReturnsSignatureInvalid()
    {
        var (publicKeyPem, _) = GenerateKeyPair();
        var (_, otherPrivateKey) = GenerateKeyPair();
        var token = SignToken(otherPrivateKey, ValidClaims());

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.Signature, result.Reason);
    }

    [Fact]
    public void Verify_TamperedPayload_ReturnsSignatureInvalid()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims());
        var dot = token.IndexOf('.');
        var tampered = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"tampered\":true}")) + token[dot..];

        var result = LicenseVerifier.Verify(tampered, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.Signature, result.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-dot-here")]
    [InlineData(".sigwithoutpayload")]
    [InlineData("payloadwithoutsig.")]
    public void Verify_MalformedToken_ReturnsMalformed(string token)
    {
        var (publicKeyPem, _) = GenerateKeyPair();

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.Malformed, result.Reason);
    }

    [Fact]
    public void Verify_InvalidBase64Signature_ReturnsMalformed()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var token = SignToken(privateKey, ValidClaims());
        var dot = token.IndexOf('.');
        var withBadSig = token[..dot] + ".not-valid-base64!!!";

        var result = LicenseVerifier.Verify(withBadSig, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.Malformed, result.Reason);
    }

    [Fact]
    public void Verify_MissingRequiredClaim_ReturnsMalformed()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var incompleteClaims = new { v = 1, kid = "test-kid", product = ExpectedProduct };
        var token = SignToken(privateKey, incompleteClaims);

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.Malformed, result.Reason);
    }

    [Fact]
    public void Verify_WrongVersionClaim_ReturnsMalformed()
    {
        var (publicKeyPem, privateKey) = GenerateKeyPair();
        var claims = new
        {
            v = 2,
            kid = "test-kid",
            product = ExpectedProduct,
            email = "buyer@example.com",
            order = "order-123",
            tier = "unlimited",
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = (long?)null,
        };
        var token = SignToken(privateKey, claims);

        var result = LicenseVerifier.Verify(token, publicKeyPem, ExpectedProduct);

        Assert.False(result.Valid);
        Assert.Equal(LicenseVerifyReason.Malformed, result.Reason);
    }
}
