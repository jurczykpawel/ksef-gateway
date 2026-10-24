namespace KSeFGateway.Api.Auth;

public sealed record KsefEnvironment(string Name, string BaseUrl)
{
    public static KsefEnvironment Parse(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" or "TEST" => new("TEST", "https://api-test.ksef.mf.gov.pl"),
        "DEMO" => new("DEMO", "https://api-demo.ksef.mf.gov.pl"),
        "PRODUCTION" or "PRD" or "PROD" => new("PRODUCTION", "https://api.ksef.mf.gov.pl"),
        _ => throw new InvalidOperationException(
            $"Invalid KSEF_ENV '{value}'. Allowed values: TEST, DEMO, PRODUCTION, PRD, PROD (case-insensitive; surrounding whitespace is ignored).")
    };
}
