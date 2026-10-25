using System.Globalization;
using System.Text;
using System.Xml;

namespace KSeFGateway.Api.Invoice;

/// <summary>
/// Builds FA(3) XML from a friendly InvoiceRequest.
/// Auto-calculates totals, fills required headers and annotations.
/// </summary>
public static class InvoiceXmlBuilder
{
    private const string Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private static readonly Dictionary<string, string> PaymentMethodMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cash"] = "1",
        ["card"] = "2",
        ["bon"] = "3",
        ["check"] = "4",
        ["credit"] = "5",
        ["transfer"] = "6",
        ["mobile"] = "7",
    };

    public static string Build(InvoiceRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(req.Seller, nameof(req.Seller));
        ArgumentNullException.ThrowIfNull(req.Buyer, nameof(req.Buyer));

        if (string.IsNullOrWhiteSpace(req.Seller.Nip))
            throw new ArgumentException("Seller NIP is required.", nameof(req.Seller));
        if (string.IsNullOrWhiteSpace(req.Seller.Name))
            throw new ArgumentException("Seller name is required.", nameof(req.Seller));
        if (string.IsNullOrWhiteSpace(req.Buyer.Name))
            throw new ArgumentException("Buyer name is required.", nameof(req.Buyer));
        if (string.IsNullOrWhiteSpace(req.InvoiceNumber))
            throw new ArgumentException("Invoice number is required.", nameof(req.InvoiceNumber));
        if (string.IsNullOrWhiteSpace(req.IssueDate))
            throw new ArgumentException("Issue date is required.", nameof(req.IssueDate));
        if (string.IsNullOrWhiteSpace(req.SaleDate))
            throw new ArgumentException("Sale date is required.", nameof(req.SaleDate));
        if (req.Items == null || req.Items.Count == 0)
            throw new ArgumentException("At least one invoice item is required.", nameof(req.Items));

        var specialErrors = InvoiceRequestValidator.Validate(req).Where(e =>
            e.Field == "buyer" || e.Field.StartsWith("buyer.", StringComparison.Ordinal) ||
            e.Field is "exemptionBasis" or "correctionReason" or "type" || e.Field.StartsWith("correctedInvoice", StringComparison.Ordinal)).ToList();
        if (specialErrors.Count > 0)
            throw new ArgumentException(string.Join("; ", specialErrors.Select(e => $"{e.Field}: {e.Message}")));

        foreach (var item in req.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Item name is required.", nameof(req.Items));
            if (item.Quantity == 0 || (req.Type != "KOR" && item.Quantity < 0))
                throw new ArgumentException($"Item '{item.Name}': quantity must be nonzero and positive unless type is KOR.", nameof(req.Items));
            if (req.Type != "KOR" && item.UnitPrice < 0)
                throw new ArgumentException($"Item '{item.Name}': unit price cannot be negative.", nameof(req.Items));
            if (item.VatRate.SummarySuffix == null)
                throw new ArgumentException($"Item '{item.Name}': unsupported VAT rate {item.VatRate}. Supported: 0, 5, 8, 23, 0 WDT, 0 EX, zw, np, np I, np II.", nameof(req.Items));
        }

        // Calculate totals per VAT rate
        var lines = req.Items.Select((item, idx) => new
        {
            Nr = idx + 1,
            item.Name,
            item.Unit,
            item.Quantity,
            item.UnitPrice,
            item.VatRate,
            NetValue = Math.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero)
        }).ToList();

        // Calculate each VAT summary once; P_15 uses the same rounded amounts as P_13/P_14.
        var vatGroups = lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key.SummarySuffix, StringComparer.Ordinal)
            .Select(g => new { Rate = g.Key, Net = g.Sum(l => l.NetValue) })
            .Select(g => new
            {
                g.Rate,
                g.Net,
                Vat = Math.Round(g.Net * g.Rate.Percentage / 100m, 2, MidpointRounding.AwayFromZero)
            }).ToList();
        var totalGross = vatGroups.Sum(g => g.Net + g.Vat);

        using var ms = new MemoryStream();
        using var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false
        });

        writer.WriteStartDocument();
        writer.WriteStartElement("Faktura", Namespace);

        // Naglowek
        writer.WriteStartElement("Naglowek");
        writer.WriteStartElement("KodFormularza");
        writer.WriteAttributeString("kodSystemowy", "FA (3)");
        writer.WriteAttributeString("wersjaSchemy", "1-0E");
        writer.WriteString("FA");
        writer.WriteEndElement();
        WriteElement(writer, "WariantFormularza", "3");
        WriteElement(writer, "DataWytworzeniaFa", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        WriteElement(writer, "SystemInfo", "ksef-gateway");
        writer.WriteEndElement(); // Naglowek

        // Podmiot1 (Seller)
        writer.WriteStartElement("Podmiot1");
        WriteDaneIdentyfikacyjne(writer, req.Seller.Nip, req.Seller.Name);
        WriteAdres(writer, req.Seller.Address);
        if (req.Seller.Email != null || req.Seller.Phone != null)
            WriteDaneKontaktowe(writer, req.Seller.Email, req.Seller.Phone);
        writer.WriteEndElement();

        // Podmiot2 (Buyer)
        writer.WriteStartElement("Podmiot2");
        writer.WriteStartElement("DaneIdentyfikacyjne");
        if (req.Buyer.Nip is { } buyerNip) WriteElement(writer, "NIP", buyerNip);
        else if (req.Buyer.EuVatNumber is { } euVatNumber)
        {
            WriteElement(writer, "KodUE", euVatNumber[..2]);
            WriteElement(writer, "NrVatUE", euVatNumber[2..]);
        }
        else if (req.Buyer.TaxId is { } taxId)
        {
            WriteElement(writer, "KodKraju", req.Buyer.Address.Country);
            WriteElement(writer, "NrID", taxId);
        }
        else WriteElement(writer, "BrakID", "1");
        WriteElement(writer, "Nazwa", req.Buyer.Name);
        writer.WriteEndElement();
        WriteAdres(writer, req.Buyer.Address);
        if (req.Buyer.Email != null || req.Buyer.Phone != null)
            WriteDaneKontaktowe(writer, req.Buyer.Email, req.Buyer.Phone);
        WriteElement(writer, "JST", "2");
        WriteElement(writer, "GV", "2");
        writer.WriteEndElement();

        // Fa
        writer.WriteStartElement("Fa");
        WriteElement(writer, "KodWaluty", req.Currency);
        WriteElement(writer, "P_1", req.IssueDate);
        if (req.IssuePlace != null)
            WriteElement(writer, "P_1M", req.IssuePlace);
        WriteElement(writer, "P_2", req.InvoiceNumber);
        WriteElement(writer, "P_6", req.SaleDate);

        // VAT summary fields (P_13_1/P_14_1 for 23%, P_13_2/P_14_2 for 8%, etc.)
        foreach (var group in vatGroups)
        {
            var suffix = group.Rate.SummarySuffix;
            WriteElement(writer, $"P_13_{suffix}", FormatDecimal(group.Net));
            // Domestic 0%, exempt and outside-territory sales have no P_14 element.
            if (group.Rate.Percentage > 0)
                WriteElement(writer, $"P_14_{suffix}", FormatDecimal(group.Vat));
        }

        WriteElement(writer, "P_15", FormatDecimal(totalGross));

        // Adnotacje (standard defaults)
        writer.WriteStartElement("Adnotacje");
        WriteElement(writer, "P_16", "2");
        WriteElement(writer, "P_17", "2");
        WriteElement(writer, "P_18", "2");
        WriteElement(writer, "P_18A", "2");
        writer.WriteStartElement("Zwolnienie");
        if (req.Items.Any(i => i.VatRate.Value == "zw"))
        {
            WriteElement(writer, "P_19", "1");
            WriteElement(writer, "P_19A", req.ExemptionBasis!);
        }
        else WriteElement(writer, "P_19N", "1");
        writer.WriteEndElement();
        writer.WriteStartElement("NoweSrodkiTransportu");
        WriteElement(writer, "P_22N", "1");
        writer.WriteEndElement();
        WriteElement(writer, "P_23", "2");
        writer.WriteStartElement("PMarzy");
        WriteElement(writer, "P_PMarzyN", "1");
        writer.WriteEndElement();
        writer.WriteEndElement(); // Adnotacje

        WriteElement(writer, "RodzajFaktury", req.Type);

        if (req.Type == "KOR")
        {
            var corrected = req.CorrectedInvoice!;
            WriteElement(writer, "PrzyczynaKorekty", req.CorrectionReason!);
            writer.WriteStartElement("DaneFaKorygowanej");
            WriteElement(writer, "DataWystFaKorygowanej", corrected.IssueDate);
            WriteElement(writer, "NrFaKorygowanej", corrected.InvoiceNumber);
            if (corrected.OutsideKsef) WriteElement(writer, "NrKSeFN", "1");
            else
            {
                WriteElement(writer, "NrKSeF", "1");
                WriteElement(writer, "NrKSeFFaKorygowanej", corrected.KsefNumber!);
            }
            writer.WriteEndElement();
        }

        // FA(3) FaWiersz documentation permits signed differences for corrections;
        // StanPrzed is only used by the alternative before/after representation.
        // FaWiersz (line items)
        foreach (var line in lines)
        {
            writer.WriteStartElement("FaWiersz");
            WriteElement(writer, "NrWierszaFa", line.Nr.ToString());
            WriteElement(writer, "P_7", line.Name);
            WriteElement(writer, "P_8A", line.Unit);
            WriteElement(writer, "P_8B", FormatDecimal(line.Quantity));
            WriteElement(writer, "P_9A", FormatDecimal(line.UnitPrice));
            WriteElement(writer, "P_11", FormatDecimal(line.NetValue));
            WriteElement(writer, "P_12", line.VatRate.XmlValue);
            writer.WriteEndElement();
        }

        // Platnosc
        if (req.Payment != null)
        {
            writer.WriteStartElement("Platnosc");
            if (req.Payment.Paid)
            {
                WriteElement(writer, "Zaplacono", "1");
                if (req.Payment.Date != null)
                    WriteElement(writer, "DataZaplaty", req.Payment.Date);
            }
            var paymentCode = PaymentMethodMap.GetValueOrDefault(req.Payment.Method, "6");
            WriteElement(writer, "FormaPlatnosci", paymentCode);
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // Fa
        writer.WriteEndElement(); // Faktura
        writer.WriteEndDocument();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteElement(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement(name);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static void WriteDaneIdentyfikacyjne(XmlWriter writer, string nip, string name)
    {
        writer.WriteStartElement("DaneIdentyfikacyjne");
        WriteElement(writer, "NIP", nip);
        WriteElement(writer, "Nazwa", name);
        writer.WriteEndElement();
    }

    private static void WriteAdres(XmlWriter writer, AddressData addr)
    {
        writer.WriteStartElement("Adres");
        WriteElement(writer, "KodKraju", addr.Country);
        WriteElement(writer, "AdresL1", addr.Street);
        WriteElement(writer, "AdresL2", addr.City);
        writer.WriteEndElement();
    }

    private static void WriteDaneKontaktowe(XmlWriter writer, string? email, string? phone)
    {
        writer.WriteStartElement("DaneKontaktowe");
        if (email != null) WriteElement(writer, "Email", email);
        if (phone != null) WriteElement(writer, "Telefon", phone);
        writer.WriteEndElement();
    }

    private static string FormatDecimal(decimal value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);

}
