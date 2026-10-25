using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KSeFGateway.Api.Tests.Endpoints;

public class BoundaryTests
{
    private const string Valid = """
    {"invoiceNumber":"1","issueDate":"2026-10-01","saleDate":"2026-10-01",
     "seller":{"nip":"1234567890","name":"Seller","address":{"street":"Street","city":"City"}},
     "buyer":{"nip":"0987654321","name":"Buyer","address":{"street":"Street","city":"City"}},
     "items":[{"name":"Item","quantity":1,"unitPrice":10,"vatRate":23}]}
    """;

    internal static WebApplication App(Mock<IKSeFClient> sdk, HttpClient? pdfClient = null, bool testServer = false)
    {
        var b = WebApplication.CreateBuilder();
        if (testServer) b.WebHost.UseTestServer();
        b.Configuration.Sources.Clear();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["KSEF_CONTEXTS_FILE"] = "/missing-boundary-contexts.json", ["KSEF_NIP"] = "1234567890", ["KSEF_TOKEN"] = "test" });
        b.Services.AddSingleton(new LicenseService(
            new JwksClient(new HttpClient(), "https://unused.invalid", NullLogger<JwksClient>.Instance),
            new RevocationClient(new HttpClient(), "https://unused.invalid", NullLogger<RevocationClient>.Instance),
            b.Configuration, NullLogger<LicenseService>.Instance));
        b.Services.AddSingleton(KsefEnvironment.Parse("TEST"));
        b.Services.AddSingleton<ContextProvider>();
        b.Services.AddSingleton<TokenPool>();
        b.Services.AddSingleton(sdk.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => pdfClient!);
        b.Services.AddSingleton(factory.Object);
        b.Services.AddSingleton(Mock.Of<ICryptographyService>());
        var app = b.Build();
        app.MapWorkflowEndpoints();
        app.MapHealthEndpoints(64, 20);
        app.MapInvoiceDownloadEndpoints();
        return app;
    }

    private static async Task<DefaultHttpContext> Invoke(WebApplication app, string path, string body = "")
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText == path);
        var ctx = new DefaultHttpContext { RequestServices = app.Services };
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Response.Body = new MemoryStream();
        await new ErrorHandlingMiddleware(endpoint.RequestDelegate!, NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(ctx);
        ctx.Response.Body.Position = 0;
        return ctx;
    }

    [Theory]
    [InlineData("\"noId\":false", "PL", "buyer")]
    [InlineData("\"noId\":true,\"taxId\":\"123\"", "US", "buyer")]
    [InlineData("\"euVatNumber\":\"GB123\"", "GB", "buyer.euVatNumber")]
    [InlineData("\"taxId\":\"123\"", "ZZ", "buyer.address.country")]
    public async Task ForeignBuyer_InvalidIdentity_Returns400BeforeSdk(string identity, string country, string field)
    {
        var sdk = new Mock<IKSeFClient>(MockBehavior.Strict);
        await using var app = App(sdk);
        var json = Invoice.ForeignBuyerTests.Input(identity, country);
        var ctx = await Invoke(app, "/ksef/invoice", json.ToJsonString());
        Assert.Equal(400, ctx.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Contains(body.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == field);
        sdk.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("rate")] [InlineData("exemption")] [InlineData("correction")]
    public async Task SpecialInvoice_InvalidData_Returns400BeforeSdk(string scenario)
    {
        var sdk = new Mock<IKSeFClient>(MockBehavior.Strict);
        await using var app = App(sdk);
        var json = Invoice.InvoiceSpecialRatesTests.Input();
        if (scenario == "rate") json["items"]![0]!["vatRate"] = "invalid";
        if (scenario == "exemption") json["items"]![0]!["vatRate"] = "zw";
        if (scenario == "correction") json["type"] = "KOR";
        var ctx = await Invoke(app, "/ksef/invoice", json.ToJsonString());
        Assert.Equal(400, ctx.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body);
        var errors = body.RootElement.GetProperty("errors");
        if (scenario == "rate") Assert.Contains("VAT rate", errors[0].GetProperty("message").GetString());
        else Assert.Contains(errors.EnumerateArray(), e => e.GetProperty("field").GetString() ==
            (scenario == "exemption" ? "exemptionBasis" : "correctedInvoice"));
        sdk.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("seller", null)] [InlineData("buyer", null)]
    [InlineData("seller.address", null)] [InlineData("buyer.address", null)]
    [InlineData("items", null)] [InlineData("items", "[]")]
    [InlineData("items.0", null)]
    [InlineData("invoiceNumber", "\"\"")]
    [InlineData("items.0.quantity", "0")] [InlineData("items.0.quantity", "-1")]
    [InlineData("items.0.unitPrice", "-1")] [InlineData("items.0.vatRate", "22")]
    [InlineData("seller.nip", "\"123\"")] [InlineData("buyer.nip", "\"abcdefghij\"")]
    [InlineData("issueDate", "\"2026-02-30\"")] [InlineData("saleDate", "\"tomorrow\"")]
    public async Task InvalidInvoice_Returns400WithoutSdkCall(string field, string? value)
    {
        var json = JsonNode.Parse(Valid)!;
        var parts = field.Split('.');
        var parent = json;
        foreach (var part in parts[..^1]) parent = parent is JsonArray a ? a[int.Parse(part)]! : parent[part]!;
        if (parent is JsonArray array) array[int.Parse(parts[^1])] = value is null ? null : JsonNode.Parse(value);
        else parent[parts[^1]] = value is null ? null : JsonNode.Parse(value);
        var sdk = new Mock<IKSeFClient>(MockBehavior.Strict);
        await using var app = App(sdk);
        var ctx = await Invoke(app, "/ksef/invoice", json.ToJsonString());
        Assert.Equal(400, ctx.Response.StatusCode);
        using var response = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Equal("Validation failed", response.RootElement.GetProperty("error").GetString());
        Assert.Contains(response.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == field);
        sdk.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("seller")] [InlineData("buyer")] [InlineData("items")]
    [InlineData("invoiceNumber")] [InlineData("issueDate")]
    public async Task MissingInvoiceField_ReturnsFieldError(string field)
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json.Remove(field);
        var sdk = new Mock<IKSeFClient>(MockBehavior.Strict);
        await using var app = App(sdk);
        var ctx = await Invoke(app, "/ksef/invoice", json.ToJsonString());
        Assert.Equal(400, ctx.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Contains(body.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == field);
        sdk.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("{")] [InlineData("{}")] [InlineData("null")]
    public async Task ValidationErrors_ListFields(string json)
    {
        await using var app = App(new Mock<IKSeFClient>(MockBehavior.Strict));
        var ctx = await Invoke(app, "/ksef/invoice", json);
        Assert.Equal(400, ctx.Response.StatusCode);
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.NotEmpty(body.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task PdfRequestAborted_CancelsUpstream()
    {
        using var abort = new CancellationTokenSource();
        var handler = new AbortHandler(abort);
        using var pdf = new HttpClient(handler);
        var sdk = new Mock<IKSeFClient>();
        sdk.Setup(x => x.GetInvoiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<Faktura/>");
        await using var app = App(sdk, pdf);
        var pool = app.Services.GetRequiredService<TokenPool>();
        var states = (System.Collections.Concurrent.ConcurrentDictionary<string, AuthState>)typeof(TokenPool)
            .GetField("_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pool)!;
        states["1234567890"] = new AuthState { AccessToken = "token", AccessTokenExpiresAt = DateTimeOffset.MaxValue };
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/ksef/invoice/{ksefNumber}/pdf");
        var ctx = new DefaultHttpContext { RequestServices = app.Services, RequestAborted = abort.Token };
        ctx.Request.RouteValues["ksefNumber"] = "1234567890-123";
        ctx.Response.Body = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => endpoint.RequestDelegate!(ctx));
        Assert.True(handler.Cancelled);
        sdk.Verify(x => x.GetInvoiceAsync("1234567890-123", "token", abort.Token), Times.Once);
    }

    private sealed class AbortHandler(CancellationTokenSource abort) : HttpMessageHandler
    {
        public bool Cancelled;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            abort.Cancel();
            Cancelled = ct.IsCancellationRequested;
            return Cancelled ? Task.FromCanceled<HttpResponseMessage>(ct)
                : Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        }
    }

    [Fact]
    public void Validation_ListsAllFields_AndAcceptsZeroPrice()
    {
        var json = JsonNode.Parse(Valid)!;
        json["items"]![0]!["unitPrice"] = 0;
        var invoice = JsonSerializer.Deserialize<KSeFGateway.Api.Invoice.InvoiceRequest>(json.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Empty(KSeFGateway.Api.Invoice.InvoiceRequestValidator.Validate(invoice));
        var errors = KSeFGateway.Api.Invoice.InvoiceRequestValidator.Validate(invoice with { Seller = null!, Buyer = null!, Items = null! });
        Assert.Equal(new[] { "seller", "buyer", "items" }, errors.Select(e => e.Field));
    }

    [Fact]
    public async Task Health_WithoutKey_ReturnsOnlyStatus()
    {
        await using var app = App(new Mock<IKSeFClient>(MockBehavior.Strict));
        var ctx = await Invoke(app, "/health");
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ServerError_DoesNotLeakMessage_ButLogsAndReturnsCorrelationId()
    {
        var logger = new Mock<ILogger<ErrorHandlingMiddleware>>();
        var error = new InvalidOperationException("private detail");
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Response.Body = new MemoryStream();
        await new ErrorHandlingMiddleware(async _ => {
            var result = await EndpointErrorHandling.Guard(() => throw error);
            await result.ExecuteAsync(ctx);
        }, logger.Object).InvokeAsync(ctx);
        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Equal(500, ctx.Response.StatusCode);
        Assert.Equal("Internal server error", body.RootElement.GetProperty("error").GetString());
        var id = body.RootElement.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(id, ctx.Response.Headers["X-Correlation-Id"].ToString());
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v != null && v.ToString()!.Contains(id!)), error,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
