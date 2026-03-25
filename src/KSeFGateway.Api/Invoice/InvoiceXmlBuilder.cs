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
        // Calculate totals per VAT rate
        var lines = req.Items.Select((item, idx) => new
        {
            Nr = idx + 1,
            item.Name,
            item.Unit,
            item.Quantity,
            item.UnitPrice,
            item.VatRate,
            NetValue = Math.Round(item.Quantity * item.UnitPrice, 2)
        }).ToList();

        var totalNet = lines.Sum(l => l.NetValue);
        var totalVat = lines.Sum(l => Math.Round(l.NetValue * l.VatRate / 100m, 2));
        var totalGross = totalNet + totalVat;

        // Group by VAT rate for P_13/P_14 fields
        var vatGroups = lines.GroupBy(l => l.VatRate).OrderByDescending(g => g.Key).ToList();

        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
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
        WriteElement(writer, "DataWytworzeniaFa", DateTimeOffset.UtcNow.ToString("o"));
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
        WriteDaneIdentyfikacyjne(writer, req.Buyer.Nip, req.Buyer.Name);
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
            var suffix = VatRateToSuffix(group.Key);
            if (suffix != null)
            {
                var groupNet = group.Sum(l => l.NetValue);
                var groupVat = Math.Round(groupNet * group.Key / 100m, 2);
                WriteElement(writer, $"P_13_{suffix}", FormatDecimal(groupNet));
                WriteElement(writer, $"P_14_{suffix}", FormatDecimal(groupVat));
            }
        }

        WriteElement(writer, "P_15", FormatDecimal(totalGross));

        // Adnotacje (standard defaults)
        writer.WriteStartElement("Adnotacje");
        WriteElement(writer, "P_16", "2");
        WriteElement(writer, "P_17", "2");
        WriteElement(writer, "P_18", "2");
        WriteElement(writer, "P_18A", "2");
        writer.WriteStartElement("Zwolnienie");
        WriteElement(writer, "P_19N", "1");
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
            WriteElement(writer, "P_12", line.VatRate.ToString());
            writer.WriteEndElement();
        }

        // Platnosc
        if (req.Payment != null)
        {
            writer.WriteStartElement("Platnosc");
            WriteElement(writer, "Zaplacono", req.Payment.Paid ? "1" : "2");
            if (req.Payment.Date != null)
                WriteElement(writer, "DataZaplaty", req.Payment.Date);
            var paymentCode = PaymentMethodMap.GetValueOrDefault(req.Payment.Method, "6");
            WriteElement(writer, "FormaPlatnosci", paymentCode);
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // Fa
        writer.WriteEndElement(); // Faktura
        writer.WriteEndDocument();
        writer.Flush();

        return sb.ToString();
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

    /// <summary>
    /// Maps VAT rate to P_13/P_14 suffix per FA(3) schema:
    /// 23% → 1, 8% → 2, 5% → 3, 0% → 6
    /// </summary>
    private static string? VatRateToSuffix(int vatRate) => vatRate switch
    {
        22 or 23 => "1",
        7 or 8 => "2",
        5 => "3",
        0 => "6",
        _ => null
    };
}
