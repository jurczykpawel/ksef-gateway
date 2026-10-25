using System.Text.Json;
using KSeFGateway.Api.Discovery;
using KSeFGateway.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KSeFGateway.Api.Tests;

public class OpenApiContractTests
{
    [Fact]
    public async Task Document_PreservesNet9RoutesAndIntegerQuerySchemas()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(SdkReflector).Assembly.GetName().Name
        });
        WebApplication? app = null;
        builder.Services.AddSingleton<EndpointDataSource>(_ =>
            new CompositeEndpointDataSource(((IEndpointRouteBuilder)app!).DataSources));
        builder.Services.AddGatewayOpenApi();
        await using var application = builder.Build();
        app = application;
        app.MapDiscoveredEndpoints(SdkReflector.DiscoverEndpoints());
        app.MapHealthEndpoints(64, 64);
        app.MapWorkflowEndpoints();
        app.MapInvoiceDownloadEndpoints();
        app.MapOpenApi();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText == "/openapi/{documentName}.json");
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.RouteValues["documentName"] = "v1";
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        Assert.Equal(200, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("3.0.4", document.RootElement.GetProperty("openapi").GetString());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var buyer = schemas.GetProperty("BuyerData");
        Assert.DoesNotContain(buyer.GetProperty("required").EnumerateArray(), p => p.GetString() == "nip");
        foreach (var name in new[] { "nip", "euVatNumber", "taxId", "noId" })
            Assert.True(buyer.GetProperty("properties").TryGetProperty(name, out _));
        var rateSchema = schemas.GetProperty("VatRate");
        var strings = rateSchema.GetProperty("oneOf").EnumerateArray().Single(p => p.GetProperty("type").GetString() == "string");
        Assert.Contains(strings.GetProperty("enum").EnumerateArray(), p => p.GetString() == "0 WDT");
        Assert.Contains(strings.GetProperty("enum").EnumerateArray(), p => p.GetString() == "0 EX");
        var item = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("InvoiceItem");
        var discount = item.GetProperty("properties").GetProperty("discount");
        var numeric = discount.TryGetProperty("anyOf", out var variants)
            ? variants.EnumerateArray().Single(s => s.TryGetProperty("type", out var type) && type.GetString() == "number")
            : discount;
        Assert.Equal("number", numeric.GetProperty("type").GetString());
        Assert.True((discount.TryGetProperty("nullable", out var nullable) ? nullable : numeric.GetProperty("nullable")).GetBoolean(), discount.GetRawText());
        Assert.DoesNotContain(item.GetProperty("required").EnumerateArray(), p => p.GetString() == "discount");
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(74, paths.EnumerateObject().Count());
        foreach (var route in new[] { "/ksef/invoices/received", "/ksef/invoices/issued" })
        {
            var parameters = paths.GetProperty(route).GetProperty("get").GetProperty("parameters");
            foreach (var name in new[] { "page", "pageSize" })
            {
                var parameter = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);
                var schema = parameter.GetProperty("schema");
                Assert.Equal("integer", schema.GetProperty("type").GetString());
                Assert.Equal("int32", schema.GetProperty("format").GetString());
                Assert.Equal(name == "page" ? 0 : 50, schema.GetProperty("default").GetInt32());
                Assert.False(schema.TryGetProperty("anyOf", out _));
                Assert.False(schema.TryGetProperty("pattern", out _));
            }
        }
    }
}
