using System.Collections.Concurrent;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.Authorization;

namespace KSeFGateway.Api.Auth;

/// <summary>
/// Manages KSeF auth lifecycle for multiple NIP contexts.
/// Lazy auth: authenticates on first request, not at startup.
/// Background refresh for active contexts.
/// </summary>
public class TokenPool : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ContextProvider _contextProvider;
    private readonly ILogger<TokenPool> _logger;
    private readonly ConcurrentDictionary<string, AuthState> _states = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _authLocks = new();

    public TokenPool(
        IServiceProvider services,
        ContextProvider contextProvider,
        ILogger<TokenPool> logger)
    {
        _services = services;
        _contextProvider = contextProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets access token for a given NIP. Authenticates lazily if needed.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(string nip, CancellationToken ct = default)
    {
        var state = _states.GetOrAdd(nip, _ => new AuthState());

        if (state.IsAuthenticated)
            return state.AccessToken;

        // Lazy auth with lock to prevent concurrent auth for same NIP
        var authLock = _authLocks.GetOrAdd(nip, _ => new SemaphoreSlim(1, 1));
        await authLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (state.IsAuthenticated)
                return state.AccessToken;

            await AuthenticateContextAsync(nip, state, ct);
            return state.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for NIP {Nip}", nip);
            return null;
        }
        finally
        {
            authLock.Release();
        }
    }

    /// <summary>
    /// Gets access token for the default context. Backward compatible.
    /// </summary>
    public async Task<string?> GetDefaultAccessTokenAsync(CancellationToken ct = default)
    {
        var defaultCtx = _contextProvider.GetDefault();
        return defaultCtx != null ? await GetAccessTokenAsync(defaultCtx.Nip, ct) : null;
    }

    /// <summary>
    /// Gets auth state for a given NIP (for health/status endpoints).
    /// </summary>
    public AuthState GetState(string nip) =>
        _states.GetOrAdd(nip, _ => new AuthState());

    /// <summary>
    /// Gets default auth state. Backward compatible.
    /// </summary>
    public AuthState GetDefaultState()
    {
        var defaultCtx = _contextProvider.GetDefault();
        return defaultCtx != null ? GetState(defaultCtx.Nip) : new AuthState();
    }

    public IReadOnlyDictionary<string, AuthState> GetAllStates() =>
        _states.ToDictionary(kv => kv.Key, kv => kv.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Eagerly auth the default context for fast first request
        var defaultCtx = _contextProvider.GetDefault();
        if (defaultCtx != null)
        {
            try
            {
                var state = _states.GetOrAdd(defaultCtx.Nip, _ => new AuthState());
                await AuthenticateContextAsync(defaultCtx.Nip, state, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initial auth failed for default NIP {Nip}. Will retry lazily.", defaultCtx.Nip);
            }
        }

        // Background refresh loop for all active contexts
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                await RefreshActiveContextsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background refresh cycle failed");
            }
        }
    }

    private async Task RefreshActiveContextsAsync(CancellationToken ct)
    {
        foreach (var (nip, state) in _states)
        {
            if (!state.IsAuthenticated && !state.CanRefresh)
                continue;

            // Refresh at 80% of TTL
            if (state.AccessTokenExpiresAt.HasValue)
            {
                var remaining = state.AccessTokenExpiresAt.Value - DateTimeOffset.UtcNow;
                var ttl = TimeSpan.FromMinutes(15); // Assume 15min if unknown
                if (remaining > ttl * 0.2)
                    continue; // Not yet time to refresh
            }

            try
            {
                if (state.CanRefresh)
                {
                    _logger.LogInformation("Refreshing token for NIP {Nip}", nip);
                    using var scope = _services.CreateScope();
                    var authCoordinator = scope.ServiceProvider.GetRequiredService<IAuthCoordinator>();
                    var result = await authCoordinator.RefreshAccessTokenAsync(state.RefreshToken!, ct);
                    state.AccessToken = result.Token;
                    state.AccessTokenExpiresAt = result.ValidUntil;
                }
                else
                {
                    await AuthenticateContextAsync(nip, state, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Refresh failed for NIP {Nip}, will re-auth on next request", nip);
                state.Clear();
            }
        }
    }

    private async Task AuthenticateContextAsync(string nip, AuthState state, CancellationToken ct)
    {
        var context = _contextProvider.GetByNip(nip)
            ?? throw new InvalidOperationException($"No KSeF context configured for NIP {nip}");

        _logger.LogInformation("Authenticating with KSeF for NIP {Nip}", nip);

        using var scope = _services.CreateScope();
        var authCoordinator = scope.ServiceProvider.GetRequiredService<IAuthCoordinator>();
        var cryptoService = scope.ServiceProvider.GetRequiredService<ICryptographyService>();

        var result = await authCoordinator.AuthKsefTokenAsync(
            contextIdentifierType: AuthenticationTokenContextIdentifierType.Nip,
            contextIdentifierValue: nip,
            tokenKsef: context.Token,
            cryptographyService: cryptoService,
            encryptionMethod: EncryptionMethodEnum.Rsa,
            cancellationToken: ct);

        state.AccessToken = result.AccessToken?.Token;
        state.RefreshToken = result.RefreshToken?.Token;
        state.AccessTokenExpiresAt = result.AccessToken?.ValidUntil;
        state.RefreshTokenExpiresAt = result.RefreshToken?.ValidUntil;

        _logger.LogInformation("Authenticated NIP {Nip}. Expires at {ExpiresAt}", nip, state.AccessTokenExpiresAt);
    }
}
