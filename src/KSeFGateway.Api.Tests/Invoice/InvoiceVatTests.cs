using System.Globalization;
using System.Xml.Linq;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

public class InvoiceVatTests
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static InvoiceItem Item(decimal quantity, decimal price, int rate) =>
        new() { Name = "VAT test", Quantity = quantity, UnitPrice = price, VatRate = rate };

    private static InvoiceRequest Invoice(params InvoiceItem[] items) => new()
    {
        InvoiceNumber = "FV/VAT/2026/001",
        IssueDate = "2026-03-24",
        SaleDate = "2026-03-24",
        Seller = new SellerData
        {
            Nip = "1234567890", Name = "Seller",
            Address = new AddressData { Street = "Testowa 1", City = "00-001 Warszawa" }
        },
        Buyer = new BuyerData
        {
            Nip = "5265877635", Name = "Buyer",
            Address = new AddressData { Street = "Testowa 2", City = "00-001 Warszawa" }
        },
        Items = items.ToList()
    };

    private static XElement Fa(string xml) => XDocument.Parse(xml).Root!.Element(Ns + "Fa")!;
    private static decimal Amount(XElement parent, string name) =>
        decimal.Parse(parent.Element(Ns + name)!.Value, CultureInfo.InvariantCulture);
    private static string Suffix(int rate) => rate switch { 23 => "1", 8 => "2", 5 => "3", 0 => "6_1", _ => throw new ArgumentOutOfRangeException(nameof(rate)) };

    [Fact]
    public void Build_TwoThreeGroszLines_GrossMatchesVatSummary()
    {
        var fa = Fa(InvoiceXmlBuilder.Build(Invoice(Item(1m, 0.03m, 23), Item(1m, 0.03m, 23))));
        Assert.Equal(0.06m, Amount(fa, "P_13_1"));
        Assert.Equal(0.01m, Amount(fa, "P_14_1"));
        Assert.Equal(0.07m, Amount(fa, "P_15"));
    }

    [Theory]
    [InlineData("0.05", "0.01")]
    [InlineData("0.25", "0.06")]
    [InlineData("1.50", "0.35")]
    [InlineData("3.50", "0.81")]
    [InlineData("0", "0")]
    public void Build_VatRounding_UsesCommercialRounding(string net, string vat)
    {
        var netValue = decimal.Parse(net, CultureInfo.InvariantCulture);
        var vatValue = decimal.Parse(vat, CultureInfo.InvariantCulture);
        var fa = Fa(InvoiceXmlBuilder.Build(Invoice(Item(1m, netValue, 23))));
        Assert.Equal(vatValue, Amount(fa, "P_14_1"));
        Assert.Equal(netValue + vatValue, Amount(fa, "P_15"));
    }

    [Theory]
    [InlineData("0.5", "0.05", "0.03")]
    [InlineData("1.5", "0.03", "0.05")]
    public void Build_LineNetMidpoint_RoundsAwayFromZero(string quantity, string price, string expected)
    {
        var fa = Fa(InvoiceXmlBuilder.Build(Invoice(Item(
            decimal.Parse(quantity, CultureInfo.InvariantCulture), decimal.Parse(price, CultureInfo.InvariantCulture), 23))));
        var net = decimal.Parse(expected, CultureInfo.InvariantCulture);
        Assert.Equal(net, Amount(fa.Element(Ns + "FaWiersz")!, "P_11"));
        Assert.Equal(net, Amount(fa, "P_13_1"));
    }

    [Fact]
    public void Build_DomesticZeroVat_ValidatesAgainstXsd()
    {
        var xml = InvoiceXmlBuilder.Build(Invoice(Item(1m, 100m, 0)));
        var errors = InvoiceXsdValidationTests.ValidateXml(xml);
        Assert.True(errors.Count == 0, $"XSD validation errors:\n{string.Join("\n", errors)}");
        var fa = Fa(xml);
        Assert.Equal(100m, Amount(fa, "P_13_6_1"));
        Assert.Null(fa.Element(Ns + "P_13_6"));
        Assert.Null(fa.Element(Ns + "P_14_6"));
        Assert.Equal("0 KR", fa.Element(Ns + "FaWiersz")!.Element(Ns + "P_12")!.Value);
        Assert.Equal(100m, Amount(fa, "P_15"));
    }

    public static IEnumerable<object[]> InvoiceCases => Enumerable.Range(0, 200).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(InvoiceCases))]
    public void Build_DeterministicInvoices_ValidateXsdAndDecimalTotals(int caseNumber)
    {
        var random = new Random(20261024 + caseNumber);
        int[] rates = [23, 8, 5, 0];
        var items = Enumerable.Range(0, random.Next(1, 9))
            .Select(_ => Item(random.Next(1, 1001) / 100m, random.Next(0, 10001) / 100m, rates[random.Next(rates.Length)]))
            .ToArray();
        var xml = InvoiceXmlBuilder.Build(Invoice(items));
        var errors = InvoiceXsdValidationTests.ValidateXml(xml);
        Assert.True(errors.Count == 0, $"Case {caseNumber}, XSD validation errors:\n{string.Join("\n", errors)}");
        var fa = Fa(xml);
        decimal gross = 0m;
        var rows = fa.Elements(Ns + "FaWiersz").ToArray();
        for (var i = 0; i < items.Length; i++)
            Assert.Equal(Math.Round(items[i].Quantity * items[i].UnitPrice, 2, MidpointRounding.AwayFromZero), Amount(rows[i], "P_11"));
        foreach (var group in items.GroupBy(i => i.VatRate))
        {
            var net = group.Sum(i => Math.Round(i.Quantity * i.UnitPrice, 2, MidpointRounding.AwayFromZero));
            var vat = Math.Round(net * group.Key / 100m, 2, MidpointRounding.AwayFromZero);
            Assert.Equal(net, Amount(fa, $"P_13_{Suffix(group.Key)}"));
            if (group.Key != 0)
                Assert.Equal(vat, Amount(fa, $"P_14_{Suffix(group.Key)}"));
            else
                Assert.DoesNotContain(fa.Elements(), e => e.Name.LocalName.StartsWith("P_14_6"));
            gross += net + vat;
        }
        Assert.Equal(gross, Amount(fa, "P_15"));
    }

    [Theory]
    [InlineData(0, "123.00")]
    [InlineData(1, "369.00")]
    [InlineData(2, "177.00")]
    [InlineData(3, "94.50")]
    [InlineData(4, "32.40")]
    public void Build_OrdinaryInvoices_PreservePreviousAmounts(int example, string expectedGross)
    {
        InvoiceItem[] items = example switch
        {
            0 => [Item(1m, 100m, 23)],
            1 => [Item(2m, 50m, 23), Item(1m, 200m, 23)],
            2 => [Item(1m, 100m, 23), Item(1m, 50m, 8)],
            3 => [Item(3m, 30m, 5)],
            4 => [Item(1.5m, 20m, 8)],
            _ => throw new ArgumentOutOfRangeException(nameof(example))
        };
        var fa = Fa(InvoiceXmlBuilder.Build(Invoice(items)));
        var oldGross = items.Sum(i =>
        {
            var net = Math.Round(i.Quantity * i.UnitPrice, 2, MidpointRounding.ToEven);
            return net + Math.Round(net * i.VatRate / 100m, 2, MidpointRounding.ToEven);
        });
        Assert.Equal(decimal.Parse(expectedGross, CultureInfo.InvariantCulture), oldGross);
        Assert.Equal(oldGross, Amount(fa, "P_15"));
        foreach (var group in items.GroupBy(i => i.VatRate))
        {
            var oldNet = group.Sum(i => Math.Round(i.Quantity * i.UnitPrice, 2, MidpointRounding.ToEven));
            Assert.Equal(oldNet, Amount(fa, $"P_13_{Suffix(group.Key)}"));
            Assert.Equal(Math.Round(oldNet * group.Key / 100m, 2, MidpointRounding.ToEven), Amount(fa, $"P_14_{Suffix(group.Key)}"));
        }
    }
}
