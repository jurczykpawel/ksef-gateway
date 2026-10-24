using KSeFGateway.Api.Auth;

namespace KSeFGateway.Api.Tests.Auth;

public class KsefEnvironmentTests
{
    [Theory]
    [InlineData(null, "TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData("", "TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData(" \t\r\n", "TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData("TEST", "TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData(" tEsT ", "TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData("DEMO", "DEMO", "https://api-demo.ksef.mf.gov.pl")]
    [InlineData(" dEmO ", "DEMO", "https://api-demo.ksef.mf.gov.pl")]
    [InlineData("PRODUCTION", "PRODUCTION", "https://api.ksef.mf.gov.pl")]
    [InlineData(" production ", "PRODUCTION", "https://api.ksef.mf.gov.pl")]
    [InlineData("PRD", "PRODUCTION", "https://api.ksef.mf.gov.pl")]
    [InlineData(" pRd ", "PRODUCTION", "https://api.ksef.mf.gov.pl")]
    [InlineData("PROD", "PRODUCTION", "https://api.ksef.mf.gov.pl")]
    [InlineData(" pRoD ", "PRODUCTION", "https://api.ksef.mf.gov.pl")]
    public void Parse_ReturnsCanonicalNameAndBaseUrl(string? input, string name, string baseUrl)
    {
        var environment = KsefEnvironment.Parse(input);

        Assert.Equal(name, environment.Name);
        Assert.Equal(baseUrl, environment.BaseUrl);
    }

    [Theory]
    [InlineData("PRODUCITON")]
    [InlineData("sandbox")]
    [InlineData(" TESTING ")]
    [InlineData("PROD UCTION")]
    public void Parse_UnknownValue_ThrowsWithAllowedValues(string input)
    {
        var error = Assert.Throws<InvalidOperationException>(() => KsefEnvironment.Parse(input));

        Assert.Equal(
            $"Invalid KSEF_ENV '{input}'. Allowed values: TEST, DEMO, PRODUCTION, PRD, PROD (case-insensitive; surrounding whitespace is ignored).",
            error.Message);
    }
}
