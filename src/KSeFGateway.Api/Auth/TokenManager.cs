using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Authorization;

namespace KSeFGateway.Api.Auth;

public class TokenManager : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TokenManager> _logger;
    private readonly AuthState _state = new();
    private readonly string _ksefToken;
    private readonly string _ksefNip;

    public TokenManager(
        IServiceProvider services,
        IConfiguration config,
        ILogger<TokenManager> logger)
    {
        _services = services;
        _logger = logger;
        _ksefToken = config["KSEF_TOKEN"]
            ?? throw new InvalidOperationException("KSEF_TOKEN is required");
        _ksefNip = config["KSEF_NIP"]
            ?? throw new InvalidOperationException("KSEF_NIP is required");
    }

    public string? GetCurrentAccessToken() => _state.IsAuthenticated ? _state.AccessToken : null;
    public AuthState GetState() => _state;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial auth - don't crash the host if it fails
        try
        {
            await AuthenticateAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial authentication failed. Gateway will start without KSeF access. Will retry in 30s.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = _state.IsAuthenticated
                    ? CalculateRefreshDelay()
                    : TimeSpan.FromSeconds(30); // Retry auth every 30s if not authenticated

                _logger.LogInformation(
                    _state.IsAuthenticated
                        ? "Next token refresh in {Delay}. Expires at {ExpiresAt}"
                        : "Not authenticated. Retrying in {Delay}. ExpiresAt={ExpiresAt}",
                    delay, _state.AccessTokenExpiresAt);

                await Task.Delay(delay, stoppingToken);
                await RefreshOrReauthenticateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token refresh failed, retrying in 30s");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        _logger.LogInformation("Authenticating with KSeF for NIP {Nip}", _ksefNip);

        using var scope = _services.CreateScope();
        var authCoordinator = scope.ServiceProvider.GetRequiredService<IAuthCoordinator>();
        var cryptoService = scope.ServiceProvider.GetRequiredService<ICryptographyService>();

        var result = await authCoordinator.AuthKsefTokenAsync(
            contextIdentifierType: AuthenticationTokenContextIdentifierType.Nip,
            contextIdentifierValue: _ksefNip,
            tokenKsef: _ksefToken,
            cryptographyService: cryptoService,
            cancellationToken: ct);

        UpdateStateFromAuth(result);
        _logger.LogInformation("Authenticated successfully. Token expires at {ExpiresAt}", _state.AccessTokenExpiresAt);
    }

    private async Task RefreshOrReauthenticateAsync(CancellationToken ct)
    {
        if (_state.CanRefresh)
        {
            try
            {
                _logger.LogInformation("Refreshing access token");
                using var scope = _services.CreateScope();
                var authCoordinator = scope.ServiceProvider.GetRequiredService<IAuthCoordinator>();

                var result = await authCoordinator.RefreshAccessTokenAsync(
                    _state.RefreshToken!, ct);

                // RefreshAccessTokenAsync returns TokenInfo with new access token
                _state.AccessToken = result.Token;
                _state.AccessTokenExpiresAt = result.ValidUntil;

                _logger.LogInformation("Token refreshed. New expiry: {ExpiresAt}", _state.AccessTokenExpiresAt);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token refresh failed, falling back to full re-authentication");
            }
        }

        await AuthenticateAsync(ct);
    }

    private void UpdateStateFromAuth(AuthenticationOperationStatusResponse result)
    {
        _state.AccessToken = result.AccessToken?.Token;
        _state.RefreshToken = result.RefreshToken?.Token;
        _state.AccessTokenExpiresAt = result.AccessToken?.ValidUntil;
        _state.RefreshTokenExpiresAt = result.RefreshToken?.ValidUntil;
    }

    private TimeSpan CalculateRefreshDelay()
    {
        if (_state.AccessTokenExpiresAt is null)
            return TimeSpan.FromMinutes(1);

        var remaining = _state.AccessTokenExpiresAt.Value - DateTimeOffset.UtcNow;
        var delay = remaining * 0.8;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(10);
    }
}
