using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KSeFGateway.Api.Licensing;

/// <summary>
/// Checks a license's `order` claim against Sellf's k-anonymity revocation list
/// (GET /api/licenses/revoked?seller=&lt;uuid&gt;&amp;prefix=&lt;hex&gt;). Fails OPEN: if the
/// endpoint is unreachable and there's no usable cache, treats the order as NOT revoked -
/// an outage must never turn a valid license invalid.
/// </summary>
public class RevocationClient
{
    private const int PrefixLength = 4;

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly ILogger<RevocationClient> _logger;
    private readonly TimeSpan _freshTtl;
    private readonly TimeSpan _staleTtl;

    private readonly Dictionary<string, (HashSet<string> Hashes, DateTimeOffset CachedAt)> _cache = new();

    public RevocationClient(HttpClient http, string revocationUrl, ILogger<RevocationClient> logger, TimeSpan? freshTtl = null, TimeSpan? staleTtl = null)
    {
        _http = http;
        _baseUrl = revocationUrl;
        _logger = logger;
        _freshTtl = freshTtl ?? TimeSpan.FromMinutes(5);
        _staleTtl = staleTtl ?? TimeSpan.FromDays(7);
    }

    public async Task<bool> IsRevokedAsync(string order, CancellationToken cancellationToken = default)
    {
        var orderHash = Sha256HexLower(order);
        var prefix = orderHash[..PrefixLength];

        if (_cache.TryGetValue(prefix, out var cached) && DateTimeOffset.UtcNow - cached.CachedAt < _freshTtl)
            return cached.Hashes.Contains(orderHash);

        try
        {
            var response = await _http.GetAsync($"{_baseUrl}&prefix={prefix}", cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<RevocationResponse>(json);
            var hashes = new HashSet<string>(parsed?.OrderHashes ?? []);
            _cache[prefix] = (hashes, DateTimeOffset.UtcNow);
            return hashes.Contains(orderHash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch revocation CRL for prefix {Prefix}", prefix);
        }

        if (_cache.TryGetValue(prefix, out var stale) && DateTimeOffset.UtcNow - stale.CachedAt < _staleTtl)
            return stale.Hashes.Contains(orderHash);

        return false;
    }

    private static string Sha256HexLower(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}

internal record RevocationResponse([property: JsonPropertyName("order_hashes")] List<string> OrderHashes);
