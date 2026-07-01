using KSeF.Client.Core.Models.Invoices;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

public class IssuedInvoiceMapperTests
{
    private static InvoiceSummary MakeInvoice(string ksefNumber, DateTimeOffset permanentStorageDate) => new()
    {
        KsefNumber = ksefNumber,
        InvoiceNumber = $"FV/{ksefNumber}",
        IssueDate = permanentStorageDate,
        InvoicingDate = permanentStorageDate,
        AcquisitionDate = permanentStorageDate,
        PermanentStorageDate = permanentStorageDate,
        Seller = new Seller { Nip = "1234567890", Name = "Seller sp. z o.o." },
        Buyer = new Buyer
        {
            Identifier = new BuyerIdentifier { Type = BuyerIdentifierType.Nip, Value = "0987654321" },
            Name = "Buyer sp. z o.o.",
        },
        NetAmount = 100m,
        GrossAmount = 123m,
        VatAmount = 23m,
        Currency = "PLN",
        InvoicingMode = InvoicingMode.Online,
        InvoiceType = KSeF.Client.Core.Models.Invoices.Common.InvoiceType.Vat,
        FormCode = new KSeF.Client.Core.Models.Sessions.FormCode
        {
            SystemCode = "FA (3)",
            SchemaVersion = "1-0E",
            Value = "FA",
        },
        IsSelfInvoicing = false,
        HasAttachment = false,
    };

    [Fact]
    public void ToSummary_MapsAllFields()
    {
        var storedAt = DateTimeOffset.Parse("2026-07-01T06:31:10Z");
        var invoice = MakeInvoice("1234567890-20260701-3BE6FF400000-63", storedAt);

        var summary = IssuedInvoiceMapper.ToSummary(invoice);

        Assert.Equal(invoice.KsefNumber, summary.KsefNumber);
        Assert.Equal(invoice.InvoiceNumber, summary.InvoiceNumber);
        Assert.Equal(storedAt, summary.PermanentStorageDate);
        Assert.Equal("Nip", summary.BuyerIdentifierType);
        Assert.Equal("0987654321", summary.BuyerIdentifierValue);
        Assert.Equal("Buyer sp. z o.o.", summary.BuyerName);
        Assert.Equal(100m, summary.NetAmount);
        Assert.Equal(123m, summary.GrossAmount);
        Assert.Equal(23m, summary.VatAmount);
        Assert.Equal("PLN", summary.Currency);
        Assert.False(summary.HasAttachment);
    }
}
