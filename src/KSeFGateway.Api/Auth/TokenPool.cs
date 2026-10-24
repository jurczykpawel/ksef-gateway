using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Services;
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
    private readonly TimeProvider _timeProvider;
    private readonly ContextProvider _contextProvider;
    private readonly ILogger<TokenPool> _logger;
    private readonly ConcurrentDictionary<string, AuthState> _states = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _authLocks = new();

    public TokenPool(
        IServiceProvider services,
        ContextProvider contextProvider,
        ILogger<TokenPool> logger,
        TimeProvider? timeProvider = null)
    {
        _services = services;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _contextProvider = contextProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets access token for a given NIP. Authenticates lazily if needed.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(string nip, CancellationToken ct = default)
    {
        ContextResolver.ValidateNip(nip, _contextProvider);
        var state = _states.GetOrAdd(nip, _ => new AuthState(_timeProvider));

        if (state.IsAuthenticated)
            return state.AccessToken;

        // Lazy auth with lock to prevent concurrent auth for same NIP
        var authLock = _authLocks.GetOrAdd(nip, _ => new SemaphoreSlim(1, 1));
        await authLock.WaitAsync(ct);
        try
        {
            // Re-read the snapshot: eager auth or refresh may have replaced it while we waited.
            state = _states[nip];
            if (state.IsAuthenticated)
                return state.AccessToken;

            state = await AuthenticateContextAsync(nip, ct);
            return state.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
    public AuthState GetState(string nip)
    {
        ContextResolver.ValidateNip(nip, _contextProvider);
        return _states.GetOrAdd(nip, _ => new AuthState(_timeProvider));
    }

    /// <summary>
    /// Gets default auth state. Backward compatible.
    /// </summary>
    public AuthState GetDefaultState()
    {
        var defaultCtx = _contextProvider.GetDefault();
        return defaultCtx != null ? GetState(defaultCtx.Nip) : new AuthState(_timeProvider);
    }

    public IReadOnlyDictionary<string, AuthState> GetAllStates() =>
        _states.ToDictionary(kv => kv.Key, kv => kv.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Eagerly auth the default context for fast first request
        var defaultCtx = _contextProvider.GetDefault();
        if (defaultCtx != null)
            await GetAccessTokenAsync(defaultCtx.Nip, stoppingToken);

        // Background refresh loop for all active contexts
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _timeProvider, stoppingToken);
                await RefreshActiveContextsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Background refresh cycle failed");
            }
        }
    }

    protected async Task RefreshActiveContextsAsync(CancellationToken ct)
    {
        foreach (var nip in _states.Keys)
        {
            var authLock = _authLocks.GetOrAdd(nip, _ => new SemaphoreSlim(1, 1));
            await authLock.WaitAsync(ct);
            try
            {
                var state = _states[nip];
                // Status reads can create empty entries; only refresh previously active contexts.
                if (state.AccessToken is null && state.RefreshToken is null)
                    continue;
                if (!state.IsRefreshDue)
                    continue;

                if (state.CanRefresh)
                {
                    _logger.LogInformation("Refreshing token for NIP {Nip}", nip);
                    var result = await RefreshAsync(state.RefreshToken!, ct);
                    _states[nip] = state with
                    {
                        AccessToken = result.Token,
                        AccessTokenExpiresAt = result.ValidUntil,
                        AccessTokenAcquiredAt = _timeProvider.GetUtcNow()
                    };
                }
                else
                {
                    await AuthenticateContextAsync(nip, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep the last complete snapshot. Unexpired access tokens remain usable;
                // expired tokens trigger lazy re-authentication on the next request.
                _logger.LogWarning(ex, "Refresh failed for NIP {Nip}; keeping previous token state", nip);
            }
            finally
            {
                authLock.Release();
            }
        }
    }

    // Called only while holding the NIP's semaphore.
    private async Task<AuthState> AuthenticateContextAsync(string nip, CancellationToken ct)
    {
        var result = await AuthenticateAsync(nip, ct);
        var state = new AuthState(_timeProvider)
        {
            AccessToken = result.AccessToken?.Token,
            RefreshToken = result.RefreshToken?.Token,
            AccessTokenExpiresAt = result.AccessToken?.ValidUntil,
            RefreshTokenExpiresAt = result.RefreshToken?.ValidUntil,
            AccessTokenAcquiredAt = _timeProvider.GetUtcNow()
        };
        _states[nip] = state;

        _logger.LogInformation("Authenticated NIP {Nip}. Expires at {ExpiresAt}", nip, state.AccessTokenExpiresAt);
        return state;
    }

    protected virtual async Task<(string Token, DateTimeOffset ValidUntil)> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IAuthCoordinator>();
        var result = await coordinator.RefreshAccessTokenAsync(refreshToken, ct);
        return (result.Token, result.ValidUntil);
    }

    protected virtual async Task<AuthenticationOperationStatusResponse> AuthenticateAsync(string nip, CancellationToken ct)
    {
        var context = _contextProvider.GetByNip(nip)
            ?? throw new InvalidOperationException($"No KSeF context configured for NIP {nip}");

        _logger.LogInformation("Authenticating with KSeF for NIP {Nip} via {Method}",
            nip, context.UsesCertificate ? "certificate" : "token");

        using var scope = _services.CreateScope();
        var authCoordinator = scope.ServiceProvider.GetRequiredService<IAuthCoordinator>();

        return context.UsesCertificate
            ? await AuthenticateWithCertificateAsync(authCoordinator, nip, context, ct)
            : await AuthenticateWithTokenAsync(authCoordinator, scope, nip, context, ct);
    }

    private static Task<AuthenticationOperationStatusResponse> AuthenticateWithTokenAsync(
        IAuthCoordinator authCoordinator, IServiceScope scope, string nip, KsefContext context, CancellationToken ct)
    {
        var cryptoService = scope.ServiceProvider.GetRequiredService<ICryptographyService>();
        return authCoordinator.AuthKsefTokenAsync(
            contextIdentifierType: AuthenticationTokenContextIdentifierType.Nip,
            contextIdentifierValue: nip,
            tokenKsef: context.Token!,
            cryptographyService: cryptoService,
            encryptionMethod: EncryptionMethodEnum.Rsa,
            cancellationToken: ct);
    }

    private static async Task<AuthenticationOperationStatusResponse> AuthenticateWithCertificateAsync(
        IAuthCoordinator authCoordinator, string nip, KsefContext context, CancellationToken ct)
    {
        using var certificate = LoadCertificate(context);

        return await authCoordinator.AuthAsync(
            contextIdentifierType: AuthenticationTokenContextIdentifierType.Nip,
            contextIdentifierValue: nip,
            identifierType: AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject,
            xmlSigner: xml => Task.FromResult(SignatureService.Sign(xml, certificate)),
            cancellationToken: ct);
    }

    /// <summary>
    /// Loads the context's certificate from disk (PEM files) or from inline PEM content -
    /// content is for platforms without convenient file mounts (Lambda, Container Apps);
    /// prefer file-based (with the platform's own secret-file feature, e.g. Render Secret
    /// Files) wherever mounting a file is practical, since it keeps the key out of plain
    /// env var dumps.
    /// </summary>
    private static X509Certificate2 LoadCertificate(KsefContext context)
    {
        var hasPassword = !string.IsNullOrEmpty(context.PrivateKeyPassword);

        if (context.HasCertificatePath)
        {
            return hasPassword
                ? X509Certificate2.CreateFromEncryptedPemFile(context.CertificatePath!, context.PrivateKeyPassword, context.PrivateKeyPath)
                : X509Certificate2.CreateFromPemFile(context.CertificatePath!, context.PrivateKeyPath);
        }

        return hasPassword
            ? X509Certificate2.CreateFromEncryptedPem(context.CertificateContent!, context.PrivateKeyContent!, context.PrivateKeyPassword!)
            : X509Certificate2.CreateFromPem(context.CertificateContent!, context.PrivateKeyContent!);
    }
}
