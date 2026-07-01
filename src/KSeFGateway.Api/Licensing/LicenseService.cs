namespace KSeFGateway.Api.Licensing;

/// <summary>
/// Verifies the gateway's own multi-NIP license (GATEWAY_LICENSE) against Sellf, mirroring
/// TokenPool's BackgroundService shape: an initial check plus periodic background refresh.
/// No license configured is not an error - it just means the free tier (1 NIP) applies.
/// </summary>
public class LicenseService : BackgroundService
{
    public const string ProductSlug = "ksef-gateway-multi-nip";
    public const int FreeMaxNips = 1;

    private const string SellerSellfSellerId = "83789f79-bdd7-4918-af1f-e56325fa5070";
    public const string DefaultJwksUrl = $"https://sellf.techskills.academy/api/licenses/jwks?seller={SellerSellfSellerId}";
    public const string DefaultRevocationUrl = $"https://sellf.techskills.academy/api/licenses/revoked?seller={SellerSellfSellerId}";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    private readonly JwksClient _jwksClient;
    private readonly RevocationClient _revocationClient;
    private readonly string? _licenseToken;
    private readonly ILogger<LicenseService> _logger;

    private volatile bool _isLicensed;
    private LicenseClaims? _claims;

    public LicenseService(JwksClient jwksClient, RevocationClient revocationClient, IConfiguration config, ILogger<LicenseService> logger)
    {
        _jwksClient = jwksClient;
        _revocationClient = revocationClient;
        _licenseToken = config["GATEWAY_LICENSE"];
        _logger = logger;
    }

    public bool IsLicensed => _isLicensed;
    public int MaxNips => _isLicensed ? int.MaxValue : FreeMaxNips;
    public LicenseClaims? Claims => _claims;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_licenseToken))
        {
            SetUnlicensed();
            return;
        }

        var kid = LicenseVerifier.TryGetKid(_licenseToken);
        if (kid is null)
        {
            _logger.LogWarning("GATEWAY_LICENSE is malformed - running in free tier (1 NIP)");
            SetUnlicensed();
            return;
        }

        var keys = await _jwksClient.GetKeysAsync(ct);
        var matchingKey = keys?.FirstOrDefault(k => k.Kid == kid);
        if (matchingKey is null)
        {
            _logger.LogWarning("Could not verify GATEWAY_LICENSE (JWKS unreachable or no matching key) - running in free tier (1 NIP)");
            SetUnlicensed();
            return;
        }

        var result = LicenseVerifier.Verify(_licenseToken, matchingKey.Pem, ProductSlug);
        if (!result.Valid)
        {
            _logger.LogWarning("GATEWAY_LICENSE is invalid ({Reason}) - running in free tier (1 NIP)", result.Reason);
            SetUnlicensed();
            return;
        }

        if (await _revocationClient.IsRevokedAsync(result.Claims!.Order, ct))
        {
            _logger.LogWarning("GATEWAY_LICENSE has been revoked - running in free tier (1 NIP)");
            SetUnlicensed();
            return;
        }

        _claims = result.Claims;
        _isLicensed = true;
        _logger.LogInformation("Multi-NIP license verified (order {Order})", result.Claims.Order);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "License refresh cycle failed");
            }
        }
    }

    private void SetUnlicensed()
    {
        _isLicensed = false;
        _claims = null;
    }
}
