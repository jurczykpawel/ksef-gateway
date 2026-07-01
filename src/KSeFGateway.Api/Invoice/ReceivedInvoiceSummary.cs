namespace KSeFGateway.Api.Invoice;

public record ReceivedInvoiceSummary(
    string KsefNumber,
    string InvoiceNumber,
    DateTimeOffset IssueDate,
    DateTimeOffset PermanentStorageDate,
    string SellerNip,
    string SellerName,
    decimal NetAmount,
    decimal GrossAmount,
    decimal VatAmount,
    string Currency,
    bool HasAttachment
);
