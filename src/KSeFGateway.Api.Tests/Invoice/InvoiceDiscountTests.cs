using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

public class InvoiceDiscountTests
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    [Theory]
    [InlineData(false, "3", "33.33", "0.10", "99.89", "22.97", "122.86")]
    [InlineData(false, "1.5", "0.11", "0.01", "0.16", "0.04", "0.20")]
    [InlineData(false, "1", "100", "100", "0.00", "0.00", "0.00")]
    [InlineData(true, "3", "33.33", "0.10", "99.89", "22.97", "122.86")]
    public void Discount_UpdatesLineAndTotals_ValidXsd(bool correction, string quantity, string price,
        string discount, string net, string vat, string gross)
    {
        var json = InvoiceSpecialRatesTests.Input();
        if (correction) InvoiceSpecialRatesTests.Correction(json);
        json["items"]![0]!["quantity"] = decimal.Parse(quantity, CultureInfo.InvariantCulture);
        json["items"]![0]!["unitPrice"] = decimal.Parse(price, CultureInfo.InvariantCulture);
        json["items"]![0]!["discount"] = decimal.Parse(discount, CultureInfo.InvariantCulture);
        var invoice = InvoiceSpecialRatesTests.Read(json);
        Assert.Empty(InvoiceRequestValidator.Validate(invoice));
        var xml = InvoiceXmlBuilder.Build(invoice);
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml));
        var fa = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;
        var row = fa.Element(Ns + "FaWiersz")!;
        Assert.Equal(discount.Contains('.') ? discount : discount + ".00", row.Element(Ns + "P_10")?.Value);
        Assert.Equal(net, row.Element(Ns + "P_11")!.Value);
        Assert.Equal(net, fa.Element(Ns + "P_13_1")!.Value);
        Assert.Equal(vat, fa.Element(Ns + "P_14_1")!.Value);
        Assert.Equal(gross, fa.Element(Ns + "P_15")!.Value);
        Assert.Null(row.Element(Ns + "StanPrzed"));
    }

    [Fact]
    public void Discount_MixedRates_SumsDiscountedRoundedLines()
    {
        var json = InvoiceSpecialRatesTests.Input();
        json["items"] = System.Text.Json.Nodes.JsonNode.Parse("""
        [{"name":"A","quantity":3,"unitPrice":33.33,"discount":0.10,"vatRate":23},
         {"name":"B","quantity":1,"unitPrice":0.5,"discount":0.01,"vatRate":23},
         {"name":"C","quantity":1,"unitPrice":10,"discount":0.5,"vatRate":8},
         {"name":"D","quantity":1,"unitPrice":10,"discount":0.1,"vatRate":5},
         {"name":"E","quantity":1,"unitPrice":10,"discount":1,"vatRate":0}]
        """);
        var xml = InvoiceXmlBuilder.Build(InvoiceSpecialRatesTests.Read(json));
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml));
        var fa = XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;
        foreach (var (field, value) in new[] { ("P_13_1", "100.38"), ("P_14_1", "23.09"),
            ("P_13_2", "9.50"), ("P_14_2", "0.76"), ("P_13_3", "9.90"), ("P_14_3", "0.50"),
            ("P_13_6_1", "9.00"), ("P_15", "153.13") })
            Assert.Equal(value, fa.Element(Ns + field)!.Value);
    }

    [Theory]
    [InlineData("invoice-simple.json")]
    [InlineData("invoice-exempt.json")]
    [InlineData("invoice-correction.json")]
    public void NoDiscount_PreservesExistingFixtureBytes(string file)
    {
        var root = Directory.Exists("/docs/examples") ? "/docs" : FindRoot();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "examples", file)))!.AsObject();
        string Bytes() => Regex.Replace(InvoiceXmlBuilder.Build(InvoiceSpecialRatesTests.Read(json)),
            @"<DataWytworzeniaFa>.*?</DataWytworzeniaFa>", "<DataWytworzeniaFa>2026-10-25T00:00:00Z</DataWytworzeniaFa>");
        var original = Bytes();
        Assert.DoesNotContain("<P_10>", original);
        foreach (var item in json["items"]!.AsArray()) item!["discount"] = 0;
        Assert.Equal(Encoding.UTF8.GetBytes(original), Encoding.UTF8.GetBytes(Bytes()));
        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Invoice", "Fixtures", Path.ChangeExtension(file, ".xml")));
        Assert.Equal(expected, Encoding.UTF8.GetBytes(original));
    }

    [Theory]
    [InlineData("-0.01", 1)] [InlineData("100.01", 1)] [InlineData("0.001", 1)]
    [InlineData("0.01", -1)]
    public void Discount_InvalidValue_RejectsDirectBuilderAndValidator(string value, int quantity)
    {
        var json = InvoiceSpecialRatesTests.Input();
        InvoiceSpecialRatesTests.Correction(json);
        json["items"]![0]!["quantity"] = quantity;
        json["items"]![0]!["discount"] = decimal.Parse(value, CultureInfo.InvariantCulture);
        var invoice = InvoiceSpecialRatesTests.Read(json);
        Assert.Contains(InvoiceRequestValidator.Validate(invoice), e => e.Field == "items.0.discount");
        Assert.Contains("items.0.discount", Assert.Throws<ArgumentException>(() => InvoiceXmlBuilder.Build(invoice)).Message);
    }

    [Theory]
    [InlineData("null")] [InlineData("0")] [InlineData("0.00")]
    public void Correction_NegativeLineWithoutDiscount_RemainsValid(string value)
    {
        var json = InvoiceSpecialRatesTests.Input();
        InvoiceSpecialRatesTests.Correction(json);
        json["items"]![0]!["quantity"] = -1;
        json["items"]![0]!["discount"] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var invoice = InvoiceSpecialRatesTests.Read(json);
        Assert.Empty(InvoiceRequestValidator.Validate(invoice));
        var xml = InvoiceXmlBuilder.Build(invoice);
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml));
        Assert.DoesNotContain("<P_10>", xml);
        Assert.Contains("<P_11>-100.00</P_11>", xml);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "examples"))) return dir.FullName;
        throw new DirectoryNotFoundException();
    }
}
