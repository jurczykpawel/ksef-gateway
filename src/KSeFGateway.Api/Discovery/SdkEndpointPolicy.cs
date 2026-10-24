namespace KSeFGateway.Api.Discovery;

/// <summary>Controls public SDK routes only; internal SDK calls remain unrestricted.</summary>
public static class SdkEndpointPolicy
{
    private static readonly HashSet<string> SafeGroups = new(StringComparer.Ordinal)
    {
        "online-session", "batch-session", "invoice-download", "session-status", "lighthouse"
    };

    public static IReadOnlyList<EndpointMetadata> Select(
        IReadOnlyList<EndpointMetadata> discovered, string? value)
    {
        var selection = value?.Trim();
        if (string.IsNullOrEmpty(selection) || selection == "safe")
            return discovered.Where(e => SafeGroups.Contains(e.GroupName)).ToArray();
        if (selection == "all")
            return discovered;

        var groups = discovered.Select(e => e.GroupName).ToHashSet(StringComparer.Ordinal);
        var methods = discovered.Select(e => $"{e.GroupName}/{e.MethodName}").ToHashSet(StringComparer.Ordinal);
        var names = selection.Split(',').Select(s => s.Trim()).ToHashSet(StringComparer.Ordinal);
        if (names.Any(name => !groups.Contains(name) && !methods.Contains(name)))
            throw new ArgumentException(
                $"Invalid KSEF_SDK_ENDPOINTS '{value}'. Use safe, all, or a comma-separated list. " +
                $"Available groups: {string.Join(", ", groups.Order())}. " +
                $"Available methods: {string.Join(", ", methods.Order())}.");

        return discovered.Where(e => names.Contains(e.GroupName) || names.Contains($"{e.GroupName}/{e.MethodName}")).ToArray();
    }
}
