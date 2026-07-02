using KSeFGateway.Api.Endpoints;
using Microsoft.Extensions.Configuration;

namespace KSeFGateway.Api.Tests.Endpoints;

public class PdfServiceTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData("ksef-pdf:10000", "http://ksef-pdf:10000")]                                     // Render fromService hostport - no scheme
    [InlineData("http://ksef-pdf:3000", "http://ksef-pdf:3000")]                                 // local compose
    [InlineData("https://ksef-pdf-prod.onrender.com", "https://ksef-pdf-prod.onrender.com")]     // public HTTPS (free tier)
    [InlineData("https://ksef-pdf-prod.onrender.com/", "https://ksef-pdf-prod.onrender.com")]    // trailing slash trimmed
    public void BaseUrl_NormalizesScheme(string input, string expected)
    {
        var cfg = Config(new() { ["PDF_SERVICE_URL"] = input });
        Assert.Equal(expected, PdfService.BaseUrl(cfg));
    }

    [Fact]
    public void BaseUrl_DefaultsToLocalCompose_WhenUnset()
    {
        Assert.Equal("http://ksef-pdf:3000", PdfService.BaseUrl(Config(new())));
    }

    [Fact]
    public void Authorize_AddsSecretHeader_WhenConfigured()
    {
        var req = new HttpRequestMessage();
        PdfService.Authorize(req, Config(new() { ["PDF_SERVICE_SECRET"] = "s3cret" }));

        Assert.True(req.Headers.Contains(PdfService.SecretHeader));
        Assert.Equal("s3cret", req.Headers.GetValues(PdfService.SecretHeader).Single());
    }

    [Fact]
    public void Authorize_AddsNoHeader_WhenSecretUnset()
    {
        var req = new HttpRequestMessage();
        PdfService.Authorize(req, Config(new()));

        Assert.False(req.Headers.Contains(PdfService.SecretHeader));
    }
}
