using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using KSeFGateway.Api.Invoice;
using KSeFGateway.Api.Tests.Invoice;

namespace KSeFGateway.Api.Tests;

public class DocsTests
{
    private static string Root
    {
        get
        {
            if (Directory.Exists("/docs/examples")) return "/docs";
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "README.en.md"))) return dir.FullName;
            throw new DirectoryNotFoundException("Documentation must be available to the test runner");
        }
    }

    private static List<string> Invoices(string readme) => Regex.Matches(
        File.ReadAllText(Path.Combine(Root, readme)), @"-d\s+'(\{[\s\S]*?\})'")
        .Select(m => m.Groups[1].Value).Where(json => json.Contains("\"seller\"")).ToList();

    [Theory]
    [InlineData("README.md")]
    [InlineData("README.en.md")]
    public void ReadmeInvoices_AreValidDtosAndMatchFixture(string readme)
    {
        var invoices = Invoices(readme);
        Assert.NotEmpty(invoices);
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "examples/invoice-simple.json")));
        foreach (var json in invoices)
        {
            ValidateInvoice(json);
            Assert.True(JsonNode.DeepEquals(fixture, JsonNode.Parse(json)), $"{readme}: invoice differs from shared fixture");
        }
    }

    [Fact]
    public void Translations_ContainSameInvoiceExamples()
    {
        var pl = Invoices("README.md");
        var en = Invoices("README.en.md");
        Assert.NotEmpty(pl);
        Assert.Equal(pl.Count, en.Count);
        for (var i = 0; i < pl.Count; i++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(pl[i]), JsonNode.Parse(en[i])), $"Invoice {i} differs between translations");
    }

    [Fact]
    public void ExampleFiles_ConformToTheirInvoiceContracts()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "examples"), "invoice*.json");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var node = JsonNode.Parse(json)!;
            // /send/json uses compact xml-js, whereas /invoice accepts InvoiceRequest.
            if (node["Faktura"] is JsonObject raw)
            {
                var xml = new XElement("Faktura", raw.SelectMany(p => Elements(p.Key, p.Value!)));
                XNamespace ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";
                foreach (var element in xml.DescendantsAndSelf()) element.Name = ns + element.Name.LocalName;
                Assert.Empty(InvoiceXsdValidationTests.ValidateXml(xml.ToString()));
            }
            else ValidateInvoice(json);
        }
    }

    private static IEnumerable<XElement> Elements(string name, JsonNode node)
    {
        if (node is JsonArray array) return array.SelectMany(item => Elements(name, item!));
        var element = new XElement(name);
        foreach (var property in node.AsObject())
            if (property.Key == "_text") element.Add(property.Value!.GetValue<string>());
            else if (property.Key == "_attributes")
                foreach (var attribute in property.Value!.AsObject()) element.Add(new XAttribute(attribute.Key, attribute.Value!.GetValue<string>()));
            else element.Add(Elements(property.Key, property.Value!));
        return [element];
    }

    private static void ValidateInvoice(string json)
    {
        var invoice = JsonSerializer.Deserialize<InvoiceRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(invoice);
        Assert.Empty(InvoiceRequestValidator.Validate(invoice));
        Assert.Empty(InvoiceXsdValidationTests.ValidateXml(InvoiceXmlBuilder.Build(invoice)));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("README.en.md")]
    [InlineData("examples/README.md")]
    [InlineData("examples/test.sh")]
    public void GatewayCurlExamples_IncludeAuthenticationAndBodyMediaType(string file)
    {
        var content = File.ReadAllText(Path.Combine(Root, file));
        var commands = Regex.Matches(content, @"curl(?:\\\r?\n|[^\r\n\\]|\\[^\r\n])*");
        Assert.NotEmpty(commands);
        foreach (Match match in commands)
        {
            var command = match.Value;
            if (!command.Contains("/ksef/") && !command.Contains("/pdf/")) continue;
            Assert.Contains("X-Api-Key: $GATEWAY_API_KEY", command);
            if (command.Contains("-d ") || command.Contains("--data")) Assert.Contains("Content-Type:", command);
        }
    }
}
