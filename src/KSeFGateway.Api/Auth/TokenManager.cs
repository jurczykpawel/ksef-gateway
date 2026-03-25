namespace KSeFGateway.Api.Auth;

/// <summary>
/// Backward-compatible wrapper around TokenPool.
/// Uses the default context for single-NIP mode.
/// Existing code that injects TokenManager continues to work.
/// </summary>
public class TokenManager
{
    private readonly TokenPool _pool;
    private readonly ContextProvider _contextProvider;

    public TokenManager(TokenPool pool, ContextProvider contextProvider)
    {
        _pool = pool;
        _contextProvider = contextProvider;
    }

    public string? GetCurrentAccessToken() =>
        _pool.GetDefaultAccessTokenAsync().GetAwaiter().GetResult();

    public AuthState GetState() =>
        _pool.GetDefaultState();

    // Multi-NIP support
    public TokenPool Pool => _pool;
    public ContextProvider Contexts => _contextProvider;
}
