namespace KSeFGateway.Api.Auth;

public class AuthState
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    public bool IsAuthenticated => AccessToken is not null && AccessTokenExpiresAt > DateTimeOffset.UtcNow;
    public bool CanRefresh => RefreshToken is not null && RefreshTokenExpiresAt > DateTimeOffset.UtcNow;

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        AccessTokenExpiresAt = null;
        RefreshTokenExpiresAt = null;
    }
}
