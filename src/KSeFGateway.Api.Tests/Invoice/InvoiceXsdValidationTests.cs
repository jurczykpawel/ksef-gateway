using System.Xml;
using System.Xml.Schema;
using KSeFGateway.Api.Invoice;

namespace KSeFGateway.Api.Tests.Invoice;

/// <summary>
/// Validates that InvoiceXmlBuilder output conforms to the official FA(3) XSD schema.
/// These tests catch breaking changes when MF updates the FA(3) schema.
/// </summary>
public class InvoiceXsdValidationTests
{
    private static readonly string SchemasDir = FindSchemasDir();
    private static readonly XmlSchemaSet SchemaSet = LoadSchemaSet();

    private static string FindSchemasDir()
    {
        // Walk up from bin/ to find Schemas/ directory
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "Schemas");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find Schemas/ directory");
    }

    private static XmlSchemaSet LoadSchemaSet()
    {
        var schemaSet = new XmlSchemaSet();
        var resolver = new LocalXmlResolver(SchemasDir);
        schemaSet.XmlResolver = resolver;

        // Only load the main schema - resolver handles imports/includes
        var mainSchema = Path.Combine(SchemasDir, "schemat_FA(3)_v1-0E.xsd");
        schemaSet.Add(null, mainSchema);
        schemaSet.Compile();
        return schemaSet;
    }

    private static List<string> ValidateXml(string xml)
    {
        var errors = new List<string>();

        var settings = new XmlReaderSettings
        {
            Schemas = SchemaSet,
            ValidationType = ValidationType.Schema,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings
        };
        settings.ValidationEventHandler += (_, e) => errors.Add($"[{e.Severity}] {e.Message}");

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read()) { }

        return errors;
    }

    [Fact]
    public void SchemaSet_LoadsSuccessfully()
    {
        Assert.True(SchemaSet.Count > 0, "XSD schema set should contain at least one schema");
        Assert.True(SchemaSet.IsCompiled, "XSD schema set should be compiled");
    }

    [Fact]
    public void Build_MinimalInvoice_ValidatesAgainstXsd()
    {
        var req = new InvoiceRequest
        {
            InvoiceNumber = "FV/XSD/001/2026",
            IssueDate = "2026-03-24",
            SaleDate = "2026-03-24",
            IssuePlace = "Warszawa",
            Seller = new SellerData
            {
                Nip = "1234567890",
                Name = "XSD Test Seller sp. z o.o.",
                Address = new AddressData { Street = "ul. Testowa 1", City = "00-001 Warszawa" }
            },
            Buyer = new BuyerData
            {
                Nip = "5265877635",  // Valid NIP (passes XSD pattern check)
                Name = "XSD Test Buyer sp. z o.o.",
                Address = new AddressData { Street = "ul. Kupiecka 2", City = "00-002 Warszawa" }
            },
            Items = [
                new InvoiceItem { Name = "Service", Quantity = 1, UnitPrice = 100.00m, VatRate = 23 }
            ],
            Payment = new PaymentData { Paid = true, Date = "2026-03-24", Method = "transfer" }
        };

        var xml = InvoiceXmlBuilder.Build(req);
        var errors = ValidateXml(xml);

        Assert.True(errors.Count == 0, $"XSD validation errors:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void Build_MultiItemMixedVat_ValidatesAgainstXsd()
    {
        var req = new InvoiceRequest
        {
            InvoiceNumber = "FV/XSD/002/2026",
            IssueDate = "2026-03-24",
            SaleDate = "2026-03-24",
            IssuePlace = "Krakow",
            Seller = new SellerData
            {
                Nip = "1234567890",
                Name = "Multi VAT Seller",
                Address = new AddressData { Street = "ul. Testowa 1", City = "00-001 Warszawa" },
                Email = "seller@example.com",
                Phone = "123456789"
            },
            Buyer = new BuyerData
            {
                Nip = "5265877635",
                Name = "Multi VAT Buyer",
                Address = new AddressData { Street = "ul. Kupiecka 2", City = "00-002 Krakow" }
            },
            Items = [
                new InvoiceItem { Name = "Consulting", Quantity = 10, UnitPrice = 200.00m, VatRate = 23 },
                new InvoiceItem { Name = "Food delivery", Quantity = 5, UnitPrice = 50.00m, VatRate = 8 },
                new InvoiceItem { Name = "Books", Quantity = 3, UnitPrice = 30.00m, VatRate = 5 }
            ],
            Payment = new PaymentData { Paid = false, Method = "transfer" }
        };

        var xml = InvoiceXmlBuilder.Build(req);
        var errors = ValidateXml(xml);

        Assert.True(errors.Count == 0, $"XSD validation errors:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// Resolves XSD imports from URLs to local files in the Schemas directory.
    /// </summary>
    private class LocalXmlResolver : XmlUrlResolver
    {
        private readonly string _schemasDir;

        public LocalXmlResolver(string schemasDir)
        {
            _schemasDir = schemasDir;
        }

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            // Map remote URLs to local files
            var filename = Path.GetFileName(absoluteUri.LocalPath);
            var localPath = Path.Combine(_schemasDir, filename);

            if (File.Exists(localPath))
                return File.OpenRead(localPath);

            // Fall back to URL resolution
            return base.GetEntity(absoluteUri, role, ofObjectToReturn);
        }
    }
}
