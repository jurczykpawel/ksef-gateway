using System.Text.Json.Serialization;

namespace KSeFGateway.Api.Licensing;

/// <summary>
/// Claims carried by a Sellf-issued product-license token. Mirrors
/// admin-panel/src/lib/license-keys/format.ts's LicenseClaims exactly - field
/// names and JSON casing must match what Sellf actually signs.
/// </summary>
public record LicenseClaims(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("order")] string Order,
    [property: JsonPropertyName("tier")] string? Tier,
    [property: JsonPropertyName("iat")] long Iat,
    [property: JsonPropertyName("exp")] long? Exp
);
