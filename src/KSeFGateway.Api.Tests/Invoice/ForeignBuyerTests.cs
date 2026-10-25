using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

public class ForeignBuyerTests
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    internal static JsonObject Input(string identity, string country = "DE")
    {
        var json = InvoiceSpecialRatesTests.Input();
        json["buyer"] = JsonNode.Parse($$"""
            {"nip":null,"name":"Foreign buyer","address":{"street":"Test 1","city":"Test City","country":"{{country}}"},{{identity}}}
            """);
        return json;
    }

    [Theory]
    [InlineData("\"nip\":\"5265877635\"", "PL", "NIP", "5265877635", null)]
    [InlineData("\"euVatNumber\":\"DE123456789\"", "DE", "NrVatUE", "123456789", "DE")]
    [InlineData("\"euVatNumber\":\"EL123456789\"", "GR", "NrVatUE", "123456789", "EL")]
    [InlineData("\"euVatNumber\":\"XI123456789\"", "GB", "NrVatUE", "123456789", "XI")]
    [InlineData("\"euVatNumber\":\"PL5265877635\"", "PL", "NrVatUE", "5265877635", "PL")]
    [InlineData("\"taxId\":\"US-123456\"", "US", "NrID", "US-123456", "US")]
    [InlineData("\"taxId\":\"123456\"", "PL", "NrID", "123456", "PL")]
    [InlineData("\"noId\":true", "PL", "BrakID", "1", null)]
    [InlineData("\"noId\":true", "US", "BrakID", "1", null)]
    public void Identity_ProducesSchemaValidBuyer(string identity, string country, string element, string value, string? code)
    {
        var json = Input(identity, country);
        var req = InvoiceSpecialRatesTests.Read(json);
        Assert.Empty(InvoiceRequestValidator.Validate(req));
        var xml = InvoiceXmlBuilder.Build(req);
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml));
        var buyer = XDocument.Parse(xml).Root!.Element(Ns + "Podmiot2")!;
        var data = buyer.Element(Ns + "DaneIdentyfikacyjne")!;
        Assert.Equal(value, data.Element(Ns + element)!.Value);
        Assert.Equal(code, data.Element(Ns + (element == "NrVatUE" ? "KodUE" : "KodKraju"))?.Value);
        Assert.Equal(code is null ? 2 : 3, data.Elements().Count());
        Assert.Equal("2", buyer.Element(Ns + "JST")!.Value);
        Assert.Equal("2", buyer.Element(Ns + "GV")!.Value);
        Assert.Equal("1234567890", ContextResolver.ExtractSellerNip(xml));
        Assert.Equal("1234567890", ContextResolver.ExtractSellerNip(json.ToJsonString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountryDictionaries_MatchEveryCodeInOfficialXsd(bool eu)
    {
        XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
        var schema = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Schemas",
            eu ? "schemat_FA(3)_v1-0E.xsd" : "KodyKrajow_v10-0E.xsd"));
        var type = schema.Root!.Elements(xsd + "simpleType").Single(e =>
            (string?)e.Attribute("name") == (eu ? "TKodyKrajowUE" : "TKodKraju"));
        var codes = type.Descendants(xsd + "enumeration").Select(e => (string)e.Attribute("value")!).ToHashSet();
        for (char first = 'A'; first <= 'Z'; first++)
        for (char second = 'A'; second <= 'Z'; second++)
        {
            var code = $"{first}{second}";
            var json = eu ? Input($"\"euVatNumber\":\"{code}123\"") : Input("\"taxId\":\"123\"", code);
            var field = eu ? "buyer.euVatNumber" : "buyer.address.country";
            var accepted = !InvoiceRequestValidator.Validate(InvoiceSpecialRatesTests.Read(json)).Any(e => e.Field == field);
            Assert.True(codes.Contains(code) == accepted, $"XSD dictionary mismatch for {code} (EU={eu})");
        }
    }

    [Theory]
    [InlineData("\"taxId\":\"US-123456\"", "US")]
    [InlineData("\"noId\":true", "PL")]
    public void Export_DoesNotRequireEuIdentityOrForeignAddress(string identity, string country)
    {
        var json = Input(identity, country);
        json["items"]![0]!["vatRate"] = "0 EX";
        var req = InvoiceSpecialRatesTests.Read(json);
        Assert.Empty(InvoiceRequestValidator.Validate(req));
        var xml = InvoiceXmlBuilder.Build(req);
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml));
        Assert.Equal("100.00", XDocument.Parse(xml).Descendants(Ns + "P_13_6_3").Single().Value);
    }

    [Fact]
    public void Nip_CanBeOmittedFromJson()
    {
        var json = Input("\"noId\":true", "PL");
        json["buyer"]!.AsObject().Remove("nip");
        Assert.Empty(InvoiceRequestValidator.Validate(InvoiceSpecialRatesTests.Read(json)));
    }

    [Theory]
    [InlineData("\"noId\":false", "PL", "buyer")]
    [InlineData("\"noId\":true,\"taxId\":\"123\"", "US", "buyer")]
    [InlineData("\"nip\":\"5265877635\",\"euVatNumber\":\"DE123\"", "DE", "buyer")]
    [InlineData("\"euVatNumber\":\"GB123\"", "GB", "buyer.euVatNumber")]
    [InlineData("\"euVatNumber\":\"GR123\"", "GR", "buyer.euVatNumber")]
    [InlineData("\"euVatNumber\":\"DE\"", "DE", "buyer.euVatNumber")]
    [InlineData("\"euVatNumber\":\"de123\"", "DE", "buyer.euVatNumber")]
    [InlineData("\"euVatNumber\":\"DE1234567890123\"", "DE", "buyer.euVatNumber")]
    [InlineData("\"euVatNumber\":\"DEa 1\"", "DE", "buyer.euVatNumber")]
    [InlineData("\"taxId\":\"123\"", "ZZ", "buyer.address.country")]
    [InlineData("\"taxId\":\" \"", "US", "buyer.taxId")]
    [InlineData("\"noId\":true,\"nip\":\"\"", "PL", "buyer")]
    public void InvalidIdentity_ReportsSpecificField(string identity, string country, string field)
    {
        var errors = InvoiceRequestValidator.Validate(InvoiceSpecialRatesTests.Read(Input(identity, country)));
        Assert.Contains(errors, e => e.Field == field && !string.IsNullOrWhiteSpace(e.Message));
    }

    [Theory]
    [InlineData("0 WDT", "6_2")]
    [InlineData("0 EX", "6_3")]
    public void ZeroRates_DeserializeRoundtripAndProduceExactSummaries(string rate, string suffix)
    {
        var json = Input("\"euVatNumber\":\"DE123456789\"");
        var items = new JsonArray();
        foreach (var r in new[] { "0", "0 WDT", "0 EX", "0 WDT", "0 EX" })
        {
            var item = json["items"]![0]!.DeepClone();
            item["unitPrice"] = 1.005m;
            item["vatRate"] = r == "0" ? JsonValue.Create(0) : JsonValue.Create(r);
            items.Add(item);
        }
        json["items"] = items;
        var req = InvoiceSpecialRatesTests.Read(json);
        Assert.Empty(InvoiceRequestValidator.Validate(req));
        Assert.Contains($"\"{rate}\"", JsonSerializer.Serialize(req));
        var xml = InvoiceXmlBuilder.Build(req);
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml));
        var fa = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;
        Assert.Equal("2.02", fa.Element(Ns + ("P_13_" + suffix))!.Value);
        Assert.Equal("1.01", fa.Element(Ns + "P_13_6_1")!.Value);
        Assert.Equal("5.05", fa.Element(Ns + "P_15")!.Value);
        Assert.DoesNotContain(fa.Elements(), e => e.Name.LocalName.StartsWith("P_14"));
        Assert.Equal(new[] { "0 KR", "0 WDT", "0 EX", "0 WDT", "0 EX" },
            fa.Elements(Ns + "FaWiersz").Select(e => e.Element(Ns + "P_12")!.Value));
    }

    [Theory]
    [InlineData("\"noId\":true")]
    [InlineData("\"taxId\":\"123\"")]
    [InlineData("\"nip\":\"5265877635\"")]
    public void Wdt_RequiresEuVatIdentity(string identity)
    {
        var json = Input(identity);
        // Use the domain type so the old converter does not hide missing business validation.
        var req = InvoiceSpecialRatesTests.Read(json);
        req = req with { Items = [req.Items[0] with { VatRate = "0 WDT" }] };
        Assert.Contains(InvoiceRequestValidator.Validate(req), e => e.Field == "buyer.euVatNumber");
    }
}
