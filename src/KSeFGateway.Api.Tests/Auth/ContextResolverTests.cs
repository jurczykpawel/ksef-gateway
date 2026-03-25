using KSeFGateway.Api.Auth;

namespace KSeFGateway.Api.Tests.Auth;

public class ContextResolverTests
{
    [Fact]
    public void ExtractSellerNip_FromXml_ReturnsNip()
    {
        var xml = @"<?xml version=""1.0""?><Faktura><Podmiot1><DaneIdentyfikacyjne><NIP>1234567890</NIP></DaneIdentyfikacyjne></Podmiot1></Faktura>";
        Assert.Equal("1234567890", ContextResolver.ExtractSellerNip(xml));
    }

    [Fact]
    public void ExtractSellerNip_FromFriendlyJson_ReturnsNip()
    {
        var json = @"{ ""seller"": { ""nip"": ""9876543210"", ""name"": ""Test"" } }";
        Assert.Equal("9876543210", ContextResolver.ExtractSellerNip(json));
    }

    [Fact]
    public void ExtractSellerNip_FromXmlJsJson_ReturnsNip()
    {
        var json = @"{ ""Faktura"": { ""Podmiot1"": { ""DaneIdentyfikacyjne"": { ""NIP"": { ""_text"": ""5555555555"" } } } } }";
        Assert.Equal("5555555555", ContextResolver.ExtractSellerNip(json));
    }

    [Fact]
    public void ExtractSellerNip_FromEmptyBody_ReturnsNull()
    {
        Assert.Null(ContextResolver.ExtractSellerNip(""));
    }

    [Fact]
    public void ExtractSellerNip_FromUnrelatedJson_ReturnsNull()
    {
        Assert.Null(ContextResolver.ExtractSellerNip(@"{ ""foo"": ""bar"" }"));
    }

    [Fact]
    public void ExtractSellerNip_IgnoresBuyerNip_ReturnsSellerOnly()
    {
        // Podmiot1 (seller) comes first, Podmiot2 (buyer) should not match
        var xml = @"<Faktura><Podmiot1><DaneIdentyfikacyjne><NIP>1111111111</NIP></DaneIdentyfikacyjne></Podmiot1><Podmiot2><DaneIdentyfikacyjne><NIP>2222222222</NIP></DaneIdentyfikacyjne></Podmiot2></Faktura>";
        Assert.Equal("1111111111", ContextResolver.ExtractSellerNip(xml));
    }
}
