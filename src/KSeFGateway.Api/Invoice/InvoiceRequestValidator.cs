using System.Globalization;
using System.Text.RegularExpressions;

namespace KSeFGateway.Api.Invoice;

public record FieldError(string Field, string Message);

public static class InvoiceRequestValidator
{
    // TNumerKSeF pattern from the official FA(3) XSD, anchored for whole-string validation.
    private const string KsefNumberPattern = @"\A([1-9]((\d[1-9])|([1-9]\d))\d{7}|M\d{9}|[A-Z]{3}\d{7})-"
        + @"(20[2-9][0-9]|2[1-9][0-9]{2}|[3-9][0-9]{3})(0[1-9]|1[0-2])(0[1-9]|[1-2][0-9]|3[0-1])-"
        + @"([0-9A-F]{6})-?([0-9A-F]{6})-([0-9A-F]{2})\z";

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
        if (invoice.Type == "KOR")
        {
            Required(invoice.CorrectionReason, "correctionReason");
            if (invoice.CorrectedInvoice is not { } corrected) errors.Add(new("correctedInvoice", "Required for KOR"));
            else
            {
                Date(corrected.IssueDate, "correctedInvoice.issueDate");
                Required(corrected.InvoiceNumber, "correctedInvoice.invoiceNumber");
                if (corrected.OutsideKsef ? corrected.KsefNumber is not null :
                    corrected.KsefNumber is null || !Regex.IsMatch(corrected.KsefNumber, KsefNumberPattern))
                    errors.Add(new("correctedInvoice.ksefNumber", "Provide a valid KSeF number OR outsideKsef=true, exclusively"));
            }
        }
        else if (invoice.CorrectedInvoice is not null || invoice.CorrectionReason is not null)
            errors.Add(new("type", "Correction data requires type KOR"));
        if (invoice.Items?.Any(item => item?.VatRate.Value == "zw") == true)
            Required(invoice.ExemptionBasis, "exemptionBasis");
        if (invoice.Items is null || invoice.Items.Count == 0) errors.Add(new("items", "At least one item is required"));
        else for (var i = 0; i < invoice.Items.Count; i++)
        {
            var field = $"items.{i}";
            var item = invoice.Items[i];
            if (item is null) { errors.Add(new(field, "Required")); continue; }
            Required(item.Name, field + ".name");
            if (item.Quantity == 0 || (invoice.Type != "KOR" && item.Quantity < 0)) errors.Add(new(field + ".quantity", "Must be nonzero (and positive unless type is KOR)"));
            if (invoice.Type != "KOR" && item.UnitPrice < 0) errors.Add(new(field + ".unitPrice", "Cannot be negative"));
            if (item.VatRate.SummarySuffix is null) errors.Add(new(field + ".vatRate", VatRateJsonConverter.ValidationMessage));
        }
        return errors;
    }
}
