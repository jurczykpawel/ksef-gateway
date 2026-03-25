namespace KSeFGateway.Api.Invoice;

public record InvoiceRequest
{
    public required string InvoiceNumber { get; init; }
    public required string IssueDate { get; init; }          // YYYY-MM-DD
    public string? IssuePlace { get; init; }
    public required string SaleDate { get; init; }            // YYYY-MM-DD
    public string Currency { get; init; } = "PLN";
    public string Type { get; init; } = "VAT";               // VAT, KOR, ZAL, etc.
    public required SellerData Seller { get; init; }
    public required BuyerData Buyer { get; init; }
    public required List<InvoiceItem> Items { get; init; }
    public PaymentData? Payment { get; init; }
}

public record SellerData
{
    public required string Nip { get; init; }
    public required string Name { get; init; }
    public required AddressData Address { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
}

public record BuyerData
{
    public required string Nip { get; init; }
    public required string Name { get; init; }
    public required AddressData Address { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
}

public record AddressData
{
    public required string Street { get; init; }
    public required string City { get; init; }
    public string Country { get; init; } = "PL";
}

public record InvoiceItem
{
    public required string Name { get; init; }
    public string Unit { get; init; } = "szt.";
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required int VatRate { get; init; }               // 23, 8, 5, 0
}

public record PaymentData
{
    public bool Paid { get; init; } = true;
    public string? Date { get; init; }                        // YYYY-MM-DD
    public string Method { get; init; } = "transfer";         // transfer, cash, card, other
}
