namespace KSeFGateway.Api.Invoice;

public record IssuedInvoiceSummary(
    string KsefNumber,
    string InvoiceNumber,
    DateTimeOffset IssueDate,
    DateTimeOffset PermanentStorageDate,
    string BuyerIdentifierType,
    string BuyerIdentifierValue,
    string BuyerName,
    decimal NetAmount,
    decimal GrossAmount,
    decimal VatAmount,
    string Currency,
    bool HasAttachment
);
