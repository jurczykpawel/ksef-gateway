using System.Globalization;

namespace KSeFGateway.Api.Invoice;

public record FieldError(string Field, string Message);

public static class InvoiceRequestValidator
{
    public static List<FieldError> Validate(InvoiceRequest? invoice)
    {
        var errors = new List<FieldError>();
        void Required(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add(new(field, "Required"));
        }
        void Date(string? value, string field)
        {
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                errors.Add(new(field, "Expected a valid date in YYYY-MM-DD format"));
        }
        void Party(string field, string? nip, string? name, AddressData? address)
        {
            if (nip is null || nip.Length != 10 || nip.Any(c => c < '0' || c > '9'))
                errors.Add(new(field + ".nip", "Expected 10 ASCII digits"));
            Required(name, field + ".name");
            if (address is null) errors.Add(new(field + ".address", "Required"));
            else
            {
                Required(address.Street, field + ".address.street");
                Required(address.City, field + ".address.city");
                Required(address.Country, field + ".address.country");
            }
        }
        if (invoice is null) return [new("body", "Invoice is required")];
        Required(invoice.InvoiceNumber, "invoiceNumber");
        Date(invoice.IssueDate, "issueDate");
        Date(invoice.SaleDate, "saleDate");
        if (invoice.Payment?.Date is { } paymentDate) Date(paymentDate, "payment.date");
        if (invoice.Seller is null) errors.Add(new("seller", "Required"));
        else Party("seller", invoice.Seller.Nip, invoice.Seller.Name, invoice.Seller.Address);
        if (invoice.Buyer is null) errors.Add(new("buyer", "Required"));
        else Party("buyer", invoice.Buyer.Nip, invoice.Buyer.Name, invoice.Buyer.Address);
        if (invoice.Items is null || invoice.Items.Count == 0) errors.Add(new("items", "At least one item is required"));
        else for (var i = 0; i < invoice.Items.Count; i++)
        {
            var field = $"items.{i}";
            var item = invoice.Items[i];
            if (item is null) { errors.Add(new(field, "Required")); continue; }
            Required(item.Name, field + ".name");
            if (item.Quantity <= 0) errors.Add(new(field + ".quantity", "Must be greater than zero"));
            if (item.UnitPrice < 0) errors.Add(new(field + ".unitPrice", "Cannot be negative"));
            if (item.VatRate is not (0 or 5 or 8 or 23)) errors.Add(new(field + ".vatRate", "Supported rates: 0, 5, 8, 23"));
        }
        return errors;
    }
}
