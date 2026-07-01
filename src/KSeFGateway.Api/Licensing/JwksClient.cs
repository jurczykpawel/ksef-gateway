using System.Text.Json;

namespace KSeFGateway.Api.Licensing;

/// <summary>
/// Fetches and caches Sellf's public JWKS (GET /api/licenses/jwks?seller=&lt;uuid&gt;).
/// Fails closed: if the endpoint is unreachable and there's no usable cache (fresh or
/// within the stale-serve grace window), returns null - callers must treat that as
/// "cannot verify any license right now", never as "license is valid".
/// </summary>
public class JwksClient
{
    private readonly HttpClient _http;
    private readonly string _jwksUrl;
    private readonly ILogger<JwksClient> _logger;
    private readonly TimeSpan _freshTtl;
    private readonly TimeSpan _staleTtl;

    private IReadOnlyList<JwksKey>? _cachedKeys;
    private DateTimeOffset _cachedAt;

    public JwksClient(HttpClient http, string jwksUrl, ILogger<JwksClient> logger, TimeSpan? freshTtl = null, TimeSpan? staleTtl = null)
    {
        _http = http;
        _jwksUrl = jwksUrl;
        _logger = logger;
        _freshTtl = freshTtl ?? TimeSpan.FromMinutes(5);
        _staleTtl = staleTtl ?? TimeSpan.FromDays(7);
    }

    public async Task<IReadOnlyList<JwksKey>?> GetKeysAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedKeys is not null && DateTimeOffset.UtcNow - _cachedAt < _freshTtl)
            return _cachedKeys;

        try
        {
            var response = await _http.GetAsync(_jwksUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<JwksResponse>(json);
            if (parsed?.Keys is { Count: > 0 })
            {
                _cachedKeys = parsed.Keys;
                _cachedAt = DateTimeOffset.UtcNow;
                return _cachedKeys;
            }

            _logger.LogWarning("JWKS fetch from {Url} returned no keys", _jwksUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch JWKS from {Url}", _jwksUrl);
        }

        if (_cachedKeys is not null && DateTimeOffset.UtcNow - _cachedAt < _staleTtl)
            return _cachedKeys;

        return null;
    }
}
