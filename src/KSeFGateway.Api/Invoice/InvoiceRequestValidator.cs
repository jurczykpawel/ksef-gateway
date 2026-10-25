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

    // Exact FA(3) TKodyKrajowUE and KodyKrajow_v10-0E TKodKraju dictionaries.
    private static readonly HashSet<string> EuCodes = new("AT BE BG CY CZ DK EE FI FR DE EL HR HU IE IT LV LT LU MT NL PL PT RO SK SI ES SE XI".Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> CountryCodes = new("AF AX AL DZ AD AO AI AQ AG AN SA AR AM AW AU AT AZ BS BH BD BB BE BZ BJ BM BT BY BO BQ BA BW BR BN IO BG BF BI XC CL CN HR CW CY TD ME DK DM DO DJ EG EC ER EE ET FK FJ PH FI FR TF GA GM GH GI GR GD GL GE GU GG GY GF GP GT GN GQ GW HT ES HN HK IN ID IQ IR IE IS IL JM JP YE JE JO KY KH CM CA QA KZ KE KG KI CO KM CG CD KP XK CR CU KW LA LS LB LR LY LI LT LV LU MK MG YT MO MW MV MY ML MT MP MA MQ MR MU MX XL FM UM MD MC MN MS MZ MM NA NR NP NL DE NE NG NI NU NF NO NC NZ PS OM PK PW PA PG PY PE PN PF PL GS PT PR CF CZ KR ZA RE RU RO RW EH BL KN LC MF VC SV WS AS SM SN RS SC SL SG SK SI SO LK PM US SZ SD SS SR SJ SH SY CH SE TJ TH TW TZ TG TK TO TT TN TR TM TV UG UA UY UZ VU WF VA HU VE GB VN IT TL CI BV CX IM SX CK VI VG HM CC MH FO SB ST TC ZM CV ZW AE XI".Split(' '), StringComparer.Ordinal);

    private static bool IsEuVatNumber(string? value) => value is { Length: >= 3 and <= 14 }
        && EuCodes.Contains(value[..2]) && Regex.IsMatch(value[2..], @"\A[\dA-Z+*]{1,12}\z");

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
        void Party(string field, string? nip, string? name, AddressData? address, bool checkNip = true)
        {
            if (checkNip && (nip is null || nip.Length != 10 || nip.Any(c => c < '0' || c > '9')))
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
        else
        {
            var buyer = invoice.Buyer;
            Party("buyer", buyer.Nip, buyer.Name, buyer.Address, checkNip: buyer.Nip is not null);
            var identities = (buyer.Nip is not null ? 1 : 0) + (buyer.EuVatNumber is not null ? 1 : 0)
                + (buyer.TaxId is not null ? 1 : 0) + (buyer.NoId ? 1 : 0);
            if (identities != 1)
                errors.Add(new("buyer", "Provide exactly one of nip, euVatNumber, taxId or noId=true"));
            if (buyer.EuVatNumber is not null && !IsEuVatNumber(buyer.EuVatNumber))
                errors.Add(new("buyer.euVatNumber", "Expected an FA(3) EU prefix (including EL and XI) followed by 1–12 uppercase letters, digits, + or *"));
            if (buyer.TaxId is not null && (string.IsNullOrWhiteSpace(buyer.TaxId) || buyer.TaxId.Length > 50))
                errors.Add(new("buyer.taxId", "Expected a tax identifier of 1–50 characters"));
            if (buyer.Address is { } address && !CountryCodes.Contains(address.Country ?? ""))
                errors.Add(new("buyer.address.country", "Expected a country code from FA(3) KodyKrajow"));
            // The 0 WDT annotation describes an intra-community supply of goods.
            if (invoice.Items?.Any(item => item?.VatRate.Value == "0 WDT") == true && !IsEuVatNumber(buyer.EuVatNumber))
                errors.Add(new("buyer.euVatNumber", "0 WDT requires an EU buyer identified by euVatNumber"));
        }
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
            if (item.Discount is { } discount)
            {
                if (discount < 0) errors.Add(new(field + ".discount", "Cannot be negative"));
                if (decimal.Round(discount, 2) != discount)
                    errors.Add(new(field + ".discount", "Expected at most 2 decimal places"));
                // Zero is equivalent to omitting the discount, including on signed KOR lines.
                if (discount > 0 && discount > item.Quantity * item.UnitPrice)
                    errors.Add(new(field + ".discount", "Cannot exceed quantity times unit price"));
            }
            if (item.VatRate.SummarySuffix is null) errors.Add(new(field + ".vatRate", VatRateJsonConverter.ValidationMessage));
        }
        return errors;
    }
}
