using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

public class InvoiceSpecialRatesTests
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static JsonObject Input() => JsonNode.Parse("""
        {"invoiceNumber":"X/1","issueDate":"2026-10-01","saleDate":"2026-10-01",
        "seller":{"nip":"1234567890","name":"Seller","address":{"street":"A 1","city":"Warszawa"}},
        "buyer":{"nip":"5265877635","name":"Buyer","address":{"street":"B 1","city":"Krakow"}},
        "items":[{"name":"Service","quantity":1,"unitPrice":100,"vatRate":23}]}
        """)!.AsObject();
    internal static InvoiceRequest Read(JsonObject json) => json.Deserialize<InvoiceRequest>(Options)!;
    private static XElement Build(JsonObject json)
    {
        var invoice = Read(json);
        Assert.Empty(InvoiceRequestValidator.Validate(invoice));
        var xml = InvoiceXmlBuilder.Build(invoice);
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml));
        return XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;
    }
    internal static void Correction(JsonObject json)
    {
        json["type"] = "KOR";
        json["correctionReason"] = "Price correction";
        json["correctedInvoice"] = JsonNode.Parse("""
            {"issueDate":"2026-09-01","invoiceNumber":"OLD/1","outsideKsef":true}
            """);
    }

    [Theory]
    [InlineData("zw", "7", "zw")]
    [InlineData("np", "8", "np I")]
    [InlineData("np I", "8", "np I")]
    [InlineData("np II", "9", "np II")]
    public void SpecialRate_EmitsExactSummaryAndAnnotations(string rate, string suffix, string xmlRate)
    {
        var json = Input();
        json["items"]![0]!["vatRate"] = rate;
        json["exemptionBasis"] = "art. 113 ust. 1 ustawy";
        var fa = Build(json);
        Assert.Equal("100.00", fa.Element(Ns + ("P_13_" + suffix))!.Value);
        Assert.Equal("100.00", fa.Element(Ns + "P_15")!.Value);
        Assert.DoesNotContain(fa.Elements(), e => e.Name.LocalName.StartsWith("P_14_"));
        Assert.Equal(xmlRate, fa.Element(Ns + "FaWiersz")!.Element(Ns + "P_12")!.Value);
        var exemption = fa.Element(Ns + "Adnotacje")!.Element(Ns + "Zwolnienie")!;
        Assert.Equal(rate == "zw" ? new[] { "P_19", "P_19A" } : new[] { "P_19N" }, exemption.Elements().Select(e => e.Name.LocalName));
        if (rate == "zw") Assert.Equal("art. 113 ust. 1 ustawy", exemption.Element(Ns + "P_19A")!.Value);
    }

    [Theory]
    [InlineData(10, "12.30")]
    [InlineData(-10, "-12.30")]
    public void Correction_EmitsSignedDifferencesAndReference(int price, string gross)
    {
        var json = Input(); Correction(json);
        json["items"]![0]!["unitPrice"] = price;
        var fa = Build(json);
        Assert.Equal(gross, fa.Element(Ns + "P_15")!.Value);
        Assert.Equal(price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), fa.Element(Ns + "FaWiersz")!.Element(Ns + "P_11")!.Value);
        Assert.Null(fa.Element(Ns + "FaWiersz")!.Element(Ns + "StanPrzed"));
        var reference = fa.Element(Ns + "DaneFaKorygowanej")!;
        Assert.Equal(new[] { "DataWystFaKorygowanej", "NrFaKorygowanej", "NrKSeFN" }, reference.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(new[] { "RodzajFaktury", "PrzyczynaKorekty", "DaneFaKorygowanej", "FaWiersz" }, fa.Elements().SkipWhile(e => e.Name.LocalName != "RodzajFaktury").Select(e => e.Name.LocalName));
        Assert.Equal("2026-09-01", reference.Element(Ns + "DataWystFaKorygowanej")!.Value);
        Assert.Equal("OLD/1", reference.Element(Ns + "NrFaKorygowanej")!.Value);
        Assert.Equal("1", reference.Element(Ns + "NrKSeFN")!.Value);
    }

    [Theory]
    [InlineData("1234567890-20260901-123456789ABC-12")]
    [InlineData("1234567890-20260901-123456-789ABC-12")]
    [InlineData("M123456789-20260901-123456789ABC-12")]
    public void Correction_WithKsefReference_EmitsChoiceInSchemaOrder(string number)
    {
        var json = Input(); Correction(json);
        json["correctedInvoice"]!["outsideKsef"] = false;
        json["correctedInvoice"]!["ksefNumber"] = number;
        var reference = Build(json).Element(Ns + "DaneFaKorygowanej")!;
        Assert.Equal(new[] { "DataWystFaKorygowanej", "NrFaKorygowanej", "NrKSeF", "NrKSeFFaKorygowanej" }, reference.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(number, reference.Element(Ns + "NrKSeFFaKorygowanej")!.Value);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MixedRates_DeterministicInvoices_ValidateXsdAndTotals(bool correction)
    {
        var random = new Random(20261024);
        string[] rates = ["23", "8", "5", "0", "zw", "np", "np I", "np II"];
        for (int sample = 0; sample < 100; sample++)
        {
            var json = Input();
            if (correction) Correction(json);
            json["exemptionBasis"] = "art. 113 ust. 1 ustawy";
            var items = new JsonArray();
            var nets = new Dictionary<string, decimal>();
            foreach (var rate in rates)
            {
                var price = random.Next(0, 10000) / 100m * (correction && random.Next(2) == 0 ? -1 : 1);
                var quantity = random.Next(1, 100) / 10m;
                var item = Input()["items"]![0]!.DeepClone();
                item["unitPrice"] = price; item["quantity"] = quantity;
                item["vatRate"] = int.TryParse(rate, out var numeric) ? JsonValue.Create(numeric) : JsonValue.Create(rate);
                items.Add(item);
                var key = rate == "np" ? "np I" : rate;
                nets[key] = nets.GetValueOrDefault(key) + Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero);
            }
            json["items"] = items;
            var fa = Build(json);
            decimal gross = 0;
            foreach (var (rate, net) in nets)
            {
                var suffix = rate switch { "23" => "1", "8" => "2", "5" => "3", "0" => "6_1", "zw" => "7", "np I" => "8", _ => "9" };
                var vat = int.TryParse(rate, out var numeric) ? Math.Round(net * numeric / 100m, 2, MidpointRounding.AwayFromZero) : 0;
                Assert.Equal(net, (decimal)fa.Element(Ns + ("P_13_" + suffix))!);
                if (numeric > 0) Assert.Equal(vat, (decimal)fa.Element(Ns + ("P_14_" + suffix))!);
                else Assert.Null(fa.Element(Ns + ("P_14_" + suffix)));
                gross += net + vat;
            }
            Assert.Equal(gross, (decimal)fa.Element(Ns + "P_15")!);
        }
    }

    [Theory]
    [InlineData("2026-02-30", "OLD/1", null, false, "correctedInvoice.issueDate")]
    [InlineData("2026-09-01", "OLD/1", "bad", false, "correctedInvoice.ksefNumber")]
    [InlineData("2026-09-01", "OLD/1", "1234567890-20260901-123456789ABC-12", true, "correctedInvoice.ksefNumber")]
    public void Correction_InvalidReference_IsRejected(string date, string number, string? ksef, bool outside, string field)
    {
        var json = Input(); Correction(json);
        json["correctedInvoice"]!["issueDate"] = date;
        json["correctedInvoice"]!["invoiceNumber"] = number;
        json["correctedInvoice"]!["ksefNumber"] = ksef;
        json["correctedInvoice"]!["outsideKsef"] = outside;
        Assert.Contains(InvoiceRequestValidator.Validate(Read(json)), e => e.Field == field);
    }

    [Fact]
    public void Correction_NegativeQuantity_UsesSignedDifference()
    {
        var json = Input(); Correction(json);
        json["items"]![0]!["quantity"] = -2;
        var fa = Build(json);
        Assert.Equal("-200.00", fa.Element(Ns + "FaWiersz")!.Element(Ns + "P_11")!.Value);
        Assert.Equal("-246.00", fa.Element(Ns + "P_15")!.Value);
    }

    [Theory]
    [InlineData("0")] [InlineData("5")] [InlineData("8")] [InlineData("23")]
    [InlineData("\"zw\"")] [InlineData("\"np I\"")] [InlineData("\"np II\"")]
    [InlineData("\"np\"")]
    public void VatRate_JsonRoundTrip_PreservesWireTypeAndNormalizesAlias(string value)
    {
        var json = Input(); json["items"]![0]!["vatRate"] = JsonNode.Parse(value);
        var invoice = Read(json);
        var serialized = JsonSerializer.SerializeToNode(invoice, Options)!;
        Assert.Equal(value == "\"np\"" ? "\"np I\"" : value, serialized["items"]![0]!["vatRate"]!.ToJsonString());
        Assert.Equal(invoice.Items[0].VatRate, serialized.Deserialize<InvoiceRequest>(Options)!.Items[0].VatRate);
    }

    [Fact]
    public void Correction_ZeroQuantity_StillRejected()
    {
        var json = Input(); Correction(json); json["items"]![0]!["quantity"] = 0;
        Assert.Contains(InvoiceRequestValidator.Validate(Read(json)), e => e.Field == "items.0.quantity");
        Assert.Throws<ArgumentException>(() => InvoiceXmlBuilder.Build(Read(json)));
    }

    [Fact]
    public void OrdinaryInvoice_CorrectionData_IsRejected()
    {
        var json = Input(); Correction(json); json["type"] = "VAT";
        Assert.Contains(InvoiceRequestValidator.Validate(Read(json)), e => e.Field == "type");
        Assert.Throws<ArgumentException>(() => InvoiceXmlBuilder.Build(Read(json)));
    }

    [Theory]
    [InlineData("exemptionBasis")]
    [InlineData("correctedInvoice")]
    [InlineData("correctionReason")]
    [InlineData("correctedInvoice.issueDate")]
    [InlineData("correctedInvoice.invoiceNumber")]
    [InlineData("correctedInvoice.ksefNumber")]
    public void MissingRequiredData_ReturnsFieldErrorAndBuilderRejects(string field)
    {
        var json = Input(); Correction(json);
        if (field == "exemptionBasis") json["items"]![0]!["vatRate"] = "zw";
        else if (field.Contains('.'))
        {
            var name = field.Split('.')[1];
            if (name == "ksefNumber") json["correctedInvoice"]!["outsideKsef"] = false;
            else json["correctedInvoice"]![name] = "";
        }
        else json.Remove(field);
        var invoice = Read(json);
        Assert.Contains(InvoiceRequestValidator.Validate(invoice), e => e.Field == field);
        Assert.Throws<ArgumentException>(() => InvoiceXmlBuilder.Build(invoice));
    }

    [Theory]
    [InlineData("\"bad\"")] [InlineData("\"23\"")] [InlineData("null")]
    [InlineData("23.5")] [InlineData("true")] [InlineData("{}")] [InlineData("99")]
    public void InvalidVatRate_IsRejectedWithUsefulMessage(string value)
    {
        var json = Input(); json["items"]![0]!["vatRate"] = JsonNode.Parse(value);
        var exception = Assert.Throws<JsonException>(() => Read(json));
        Assert.Contains("VAT rate", exception.Message);
    }
}
