using System.Text.Json.Serialization;

namespace KSeFGateway.Api.Licensing;

public record JwksKey(
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("pem")] string Pem
);

internal record JwksResponse([property: JsonPropertyName("keys")] List<JwksKey> Keys);
