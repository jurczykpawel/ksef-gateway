using KSeF.Client.Core.Models.Invoices;

namespace KSeFGateway.Api.Invoice;

public static class IssuedInvoiceMapper
{
    public static IssuedInvoiceSummary ToSummary(InvoiceSummary invoice) => new(
        invoice.KsefNumber,
        invoice.InvoiceNumber,
        invoice.IssueDate,
        invoice.PermanentStorageDate,
        invoice.Buyer.Identifier.Type.ToString(),
        invoice.Buyer.Identifier.Value,
        invoice.Buyer.Name,
        invoice.NetAmount,
        invoice.GrossAmount,
        invoice.VatAmount,
        invoice.Currency,
        invoice.HasAttachment
    );
}
