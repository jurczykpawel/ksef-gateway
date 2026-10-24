namespace KSeFGateway.Api.Auth;

/// <summary>Request-local client scope, installed only after API-key authentication.</summary>
internal static class ApiKeyAccess
{
    private static readonly object ScopeKey = new();

    public static IReadOnlySet<string>? AllowedNips(HttpContext context) =>
        context.Items.TryGetValue(ScopeKey, out var value) ? (IReadOnlySet<string>)value! : null;

    public static void SetClient(HttpContext context, IReadOnlySet<string> nips) => context.Items[ScopeKey] = nips;

    public static void Authorize(HttpContext context, string nip)
    {
        if (AllowedNips(context) is { } allowed && !allowed.Contains(nip))
            throw new BadHttpRequestException($"API key is not authorized for NIP {nip}", StatusCodes.Status403Forbidden);
    }

    public static IReadOnlyList<KsefContext> VisibleContexts(HttpContext context, ContextProvider provider) =>
        provider.GetAll().Where(c => AllowedNips(context) is not { } allowed || allowed.Contains(c.Nip)).ToArray();

    public static string? DefaultNip(HttpContext context, ContextProvider provider) =>
        AllowedNips(context) is { } allowed
            ? allowed.Count == 1 ? allowed.Single() : null
            : provider.GetDefault()?.Nip;
}
