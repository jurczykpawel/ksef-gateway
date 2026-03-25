using System.Xml.Linq;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

public class InvoiceXmlBuilderTests
{
    private static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static InvoiceRequest MinimalInvoice() => new()
    {
        InvoiceNumber = "FV/TEST/001/2026",
        IssueDate = "2026-03-24",
        SaleDate = "2026-03-24",
        IssuePlace = "Warszawa",
        Seller = new SellerData
        {
            Nip = "1234567890",
            Name = "Seller sp. z o.o.",
            Address = new AddressData { Street = "ul. Testowa 1", City = "00-001 Warszawa" }
        },
        Buyer = new BuyerData
        {
            Nip = "0987654321",
            Name = "Buyer sp. z o.o.",
            Address = new AddressData { Street = "ul. Kupiecka 2", City = "00-002 Warszawa" }
        },
        Items = [
            new InvoiceItem { Name = "Service", Quantity = 1, UnitPrice = 100.00m, VatRate = 23 }
        ],
        Payment = new PaymentData { Paid = true, Date = "2026-03-24", Method = "transfer" }
    };

    [Fact]
    public void Build_MinimalInvoice_ProducesValidXml()
    {
        var xml = InvoiceXmlBuilder.Build(MinimalInvoice());
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root);
        Assert.Equal("Faktura", doc.Root.Name.LocalName);
        Assert.Equal(Ns.NamespaceName, doc.Root.Name.NamespaceName);
    }

    [Fact]
    public void Build_SetsCorrectHeader()
    {
        var xml = InvoiceXmlBuilder.Build(MinimalInvoice());
        var doc = XDocument.Parse(xml);

        var header = doc.Root!.Element(Ns + "Naglowek")!;
        var kodFormularza = header.Element(Ns + "KodFormularza")!;
        Assert.Equal("FA", kodFormularza.Value);
        Assert.Equal("FA (3)", kodFormularza.Attribute("kodSystemowy")!.Value);
        Assert.Equal("1-0E", kodFormularza.Attribute("wersjaSchemy")!.Value);
        Assert.Equal("3", header.Element(Ns + "WariantFormularza")!.Value);
    }

    [Fact]
    public void Build_SetsSellerData()
    {
        var xml = InvoiceXmlBuilder.Build(MinimalInvoice());
        var doc = XDocument.Parse(xml);

        var seller = doc.Root!.Element(Ns + "Podmiot1")!;
        Assert.Equal("1234567890", seller.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "NIP")!.Value);
        Assert.Equal("Seller sp. z o.o.", seller.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "Nazwa")!.Value);
        Assert.Equal("PL", seller.Element(Ns + "Adres")!.Element(Ns + "KodKraju")!.Value);
    }

    [Fact]
    public void Build_SetsBuyerData()
    {
        var xml = InvoiceXmlBuilder.Build(MinimalInvoice());
        var doc = XDocument.Parse(xml);

        var buyer = doc.Root!.Element(Ns + "Podmiot2")!;
        Assert.Equal("0987654321", buyer.Element(Ns + "DaneIdentyfikacyjne")!.Element(Ns + "NIP")!.Value);
    }

    [Fact]
    public void Build_SingleItem_CorrectTotals()
    {
        var xml = InvoiceXmlBuilder.Build(MinimalInvoice());
        var doc = XDocument.Parse(xml);

        var fa = doc.Root!.Element(Ns + "Fa")!;
        Assert.Equal("100.00", fa.Element(Ns + "P_13_1")!.Value); // net 23%
        Assert.Equal("23.00", fa.Element(Ns + "P_14_1")!.Value);  // VAT 23%
        Assert.Equal("123.00", fa.Element(Ns + "P_15")!.Value);   // gross total
    }

    [Fact]
    public void Build_MultipleItems_CorrectTotals()
    {
        var req = MinimalInvoice() with
        {
            Items = [
                new InvoiceItem { Name = "Item 1", Quantity = 2, UnitPrice = 50.00m, VatRate = 23 },
                new InvoiceItem { Name = "Item 2", Quantity = 1, UnitPrice = 200.00m, VatRate = 23 }
            ]
        };

        var xml = InvoiceXmlBuilder.Build(req);
        var doc = XDocument.Parse(xml);
        var fa = doc.Root!.Element(Ns + "Fa")!;

        Assert.Equal("300.00", fa.Element(Ns + "P_13_1")!.Value); // 100 + 200 net
        Assert.Equal("69.00", fa.Element(Ns + "P_14_1")!.Value);  // 300 * 0.23
        Assert.Equal("369.00", fa.Element(Ns + "P_15")!.Value);
    }

    [Fact]
    public void Build_MixedVatRates_SeparateP13P14Fields()
    {
        var req = MinimalInvoice() with
        {
            Items = [
                new InvoiceItem { Name = "Standard", Quantity = 1, UnitPrice = 100.00m, VatRate = 23 },
                new InvoiceItem { Name = "Reduced", Quantity = 1, UnitPrice = 50.00m, VatRate = 8 }
            ]
        };

        var xml = InvoiceXmlBuilder.Build(req);
        var doc = XDocument.Parse(xml);
        var fa = doc.Root!.Element(Ns + "Fa")!;

        Assert.Equal("100.00", fa.Element(Ns + "P_13_1")!.Value); // 23% net
        Assert.Equal("23.00", fa.Element(Ns + "P_14_1")!.Value);  // 23% VAT
        Assert.Equal("50.00", fa.Element(Ns + "P_13_2")!.Value);  // 8% net
        Assert.Equal("4.00", fa.Element(Ns + "P_14_2")!.Value);   // 8% VAT
        Assert.Equal("177.00", fa.Element(Ns + "P_15")!.Value);   // gross: 123 + 54
    }

    [Fact]
    public void Build_LineItems_CorrectWiersze()
    {
        var req = MinimalInvoice() with
        {
            Items = [
                new InvoiceItem { Name = "Item A", Quantity = 3, UnitPrice = 10.00m, VatRate = 23 },
                new InvoiceItem { Name = "Item B", Quantity = 1, UnitPrice = 50.00m, VatRate = 23 }
            ]
        };

        var xml = InvoiceXmlBuilder.Build(req);
        var doc = XDocument.Parse(xml);
        var wiersze = doc.Root!.Element(Ns + "Fa")!.Elements(Ns + "FaWiersz").ToList();

        Assert.Equal(2, wiersze.Count);
        Assert.Equal("1", wiersze[0].Element(Ns + "NrWierszaFa")!.Value);
        Assert.Equal("Item A", wiersze[0].Element(Ns + "P_7")!.Value);
        Assert.Equal("30.00", wiersze[0].Element(Ns + "P_11")!.Value); // 3 * 10
        Assert.Equal("2", wiersze[1].Element(Ns + "NrWierszaFa")!.Value);
    }

    [Theory]
    [InlineData("transfer", "6")]
    [InlineData("cash", "1")]
    [InlineData("card", "2")]
    public void Build_PaymentMethod_MapsCorrectly(string method, string expectedCode)
    {
        var req = MinimalInvoice() with
        {
            Payment = new PaymentData { Paid = true, Date = "2026-03-24", Method = method }
        };

        var xml = InvoiceXmlBuilder.Build(req);
        var doc = XDocument.Parse(xml);
        var platnosc = doc.Root!.Element(Ns + "Fa")!.Element(Ns + "Platnosc")!;

        Assert.Equal(expectedCode, platnosc.Element(Ns + "FormaPlatnosci")!.Value);
    }

    [Fact]
    public void Build_InvoiceNumber_PreservedExactly()
    {
        var req = MinimalInvoice();
        var xml = InvoiceXmlBuilder.Build(req);
        var doc = XDocument.Parse(xml);

        Assert.Equal("FV/TEST/001/2026", doc.Root!.Element(Ns + "Fa")!.Element(Ns + "P_2")!.Value);
    }

    [Fact]
    public void Build_ContactData_IncludedWhenProvided()
    {
        var req = MinimalInvoice() with
        {
            Seller = MinimalInvoice().Seller with { Email = "test@example.com", Phone = "123456789" }
        };

        var xml = InvoiceXmlBuilder.Build(req);
        var doc = XDocument.Parse(xml);
        var kontakt = doc.Root!.Element(Ns + "Podmiot1")!.Element(Ns + "DaneKontaktowe")!;

        Assert.Equal("test@example.com", kontakt.Element(Ns + "Email")!.Value);
        Assert.Equal("123456789", kontakt.Element(Ns + "Telefon")!.Value);
    }

    [Fact]
    public void Build_ContactData_OmittedWhenNull()
    {
        var req = MinimalInvoice();
        var xml = InvoiceXmlBuilder.Build(req);
        var doc = XDocument.Parse(xml);

        Assert.Null(doc.Root!.Element(Ns + "Podmiot1")!.Element(Ns + "DaneKontaktowe"));
    }
}
