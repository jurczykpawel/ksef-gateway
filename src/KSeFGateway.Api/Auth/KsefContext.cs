namespace KSeFGateway.Api.Auth;

/// <summary>
/// Represents a single KSeF authentication context (one firm/NIP).
/// </summary>
public record KsefContext
{
    public required string Nip { get; init; }
    public required string Token { get; init; }
    public string? Label { get; init; }  // Optional friendly name
}
