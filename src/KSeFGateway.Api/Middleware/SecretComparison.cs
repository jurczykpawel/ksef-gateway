using System.Security.Cryptography;
using System.Text;

namespace KSeFGateway.Api.Middleware;

/// <summary>
/// Constant-time secret comparison shared by the caller-facing auth middlewares
/// (<see cref="ApiKeyMiddleware"/> and <see cref="TrustedProxyMiddleware"/>) so a timing
/// side-channel can't be used to recover a configured secret byte by byte.
/// </summary>
internal static class SecretComparison
{
    public static bool ConstantTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        // Comparing length up front leaks the length itself via timing, not the content -
        // an accepted trade-off since CryptographicOperations.FixedTimeEquals requires equal-length inputs.
        if (bytesA.Length != bytesB.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
