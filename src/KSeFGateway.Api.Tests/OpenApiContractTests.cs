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
        app.MapHealthEndpoints(64);
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
