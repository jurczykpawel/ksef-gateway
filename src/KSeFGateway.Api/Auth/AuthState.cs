namespace KSeFGateway.Api.Auth;

/// <summary>A complete, immutable token snapshot; TokenPool replaces it atomically.</summary>
public sealed record AuthState
{
    private readonly TimeProvider _timeProvider;

    public AuthState(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; init; }
    public DateTimeOffset? AccessTokenAcquiredAt { get; init; }
    public bool IsAuthenticated => AccessToken is not null && AccessTokenExpiresAt > _timeProvider.GetUtcNow();
    public bool CanRefresh => RefreshToken is not null && RefreshTokenExpiresAt > _timeProvider.GetUtcNow();

    internal bool IsRefreshDue => !IsAuthenticated || AccessTokenAcquiredAt is null ||
        _timeProvider.GetUtcNow() >= AccessTokenAcquiredAt.Value +
        (AccessTokenExpiresAt!.Value - AccessTokenAcquiredAt.Value) * 0.8;
}
