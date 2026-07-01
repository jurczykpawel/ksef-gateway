using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KSeFGateway.Api.Licensing;

public enum LicenseVerifyReason
{
    Malformed,
    Signature,
    Expired,
    WrongProduct,
}

public record LicenseVerifyResult(bool Valid, LicenseClaims? Claims, LicenseVerifyReason? Reason);

/// <summary>
/// Offline verification of a Sellf-issued product-license token, format
/// payloadB64url.sigB64url - mirrors admin-panel/src/lib/license-keys/format.ts's
/// verifyLicense exactly: ECDSA P-256/SHA-256 over the ASCII bytes of the payload
/// string (not the decoded JSON), signature DER-encoded (Node's createSign default).
/// </summary>
public static class LicenseVerifier
{
    /// <summary>
    /// Reads the `kid` claim without verifying the signature, so the caller knows which
    /// JWKS key to fetch/select before calling Verify() - standard "peek kid, then verify"
    /// two-step, same as any JWT library. Never trust anything else read this way.
    /// </summary>
    public static string? TryGetKid(string token)
    {
        var payloadB64 = SplitPayload(token);
        return payloadB64 is null ? null : TryDecodeClaims(payloadB64)?.Kid;
    }

    public static LicenseVerifyResult Verify(string token, string publicKeyPem, string expectedProduct, DateTimeOffset? now = null)
    {
        var payloadB64 = SplitPayload(token);
        if (payloadB64 is null)
            return Fail(LicenseVerifyReason.Malformed);

        var dot = token.IndexOf('.');
        var sigB64 = token[(dot + 1)..];

        byte[] sigBytes;
        try
        {
            sigBytes = Base64UrlDecode(sigB64);
        }
        catch
        {
            return Fail(LicenseVerifyReason.Malformed);
        }

        bool sigValid;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadB64);
            sigValid = ecdsa.VerifyData(payloadBytes, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch
        {
            return Fail(LicenseVerifyReason.Signature);
        }

        if (!sigValid)
            return Fail(LicenseVerifyReason.Signature);

        var claims = TryDecodeClaims(payloadB64);

        if (claims is null
            || claims.V != 1
            || string.IsNullOrEmpty(claims.Kid)
            || string.IsNullOrEmpty(claims.Product)
            || string.IsNullOrEmpty(claims.Email)
            || string.IsNullOrEmpty(claims.Order))
            return Fail(LicenseVerifyReason.Malformed);

        if (!string.Equals(claims.Product, expectedProduct, StringComparison.Ordinal))
            return Fail(LicenseVerifyReason.WrongProduct);

        var nowSeconds = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        if (claims.Exp is not null && claims.Exp < nowSeconds)
            return Fail(LicenseVerifyReason.Expired);

        return new LicenseVerifyResult(true, claims, null);
    }

    private static LicenseVerifyResult Fail(LicenseVerifyReason reason) => new(false, null, reason);

    private static string? SplitPayload(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;
        var dot = token.IndexOf('.');
        return dot <= 0 || dot == token.Length - 1 ? null : token[..dot];
    }

    private static LicenseClaims? TryDecodeClaims(string payloadB64)
    {
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
            return JsonSerializer.Deserialize<LicenseClaims>(payloadJson);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };
        return Convert.FromBase64String(padded);
    }
}
