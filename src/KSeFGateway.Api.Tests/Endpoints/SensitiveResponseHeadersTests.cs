using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Invoices;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace KSeFGateway.Api.Tests.Endpoints;

public class SensitiveResponseHeadersTests
{
    [Theory]
    [InlineData("/ksef/status")]
    [InlineData("/ksef/contexts")]
    [InlineData("/ksef/invoice/1234567890-test")]
    [InlineData("/ksef/invoice/1234567890-test/pdf")]
    [InlineData("/ksef/invoices/received")]
    [InlineData("/ksef/invoices/issued")]
    [InlineData("/ksef/invoices/received/new")]
    public async Task DataResponses_ProtectHeadersAndPreserveContent(string path)
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivate(response);
        var body = await response.Content.ReadAsStringAsync();
        if (path.EndsWith("/pdf"))
        {
            Assert.Equal("%PDF-test", body);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Contains("faktura-1234567890-test.pdf", response.Content.Headers.ContentDisposition.ToString());
        }
        else if (path.Contains("/invoice/")) Assert.Equal("<Faktura/>", body);
        else Assert.Contains("\"success\":true", body);
    }

    [Theory]
    [InlineData("/ksef/status", false, 401)]
    [InlineData("/ksef/failure", true, 500)]
    [InlineData("/unknown", true, 404)]
    [InlineData("/health-private", true, 404)]
    [InlineData("/scalar-private", true, 404)]
    [InlineData("/openapi-private", true, 404)]
    public async Task ErrorsAndSimilarPaths_AreProtected(string path, bool authenticated, int status)
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", authenticated ? "test-key" : "wrong");
        using var response = await client.GetAsync(path);
        Assert.Equal(status, (int)response.StatusCode);
        AssertPrivate(response);
        if (status == 500) Assert.Contains("Internal server error", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/scalar/v1")]
    [InlineData("/openapi/v1.json")]
    public async Task PublicHealthAndDocumentation_AreExempt(string path)
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.CacheControl);
        Assert.False(response.Headers.Contains("X-Content-Type-Options"));
    }

    private static void AssertPrivate(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    private static WebApplication CreateApp()
    {
        var sdk = new Mock<IKSeFClient>(MockBehavior.Strict);
        sdk.Setup(x => x.GetInvoiceAsync(It.IsAny<string>(), "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("<Faktura/>");
        sdk.Setup(x => x.QueryInvoiceMetadataAsync(It.IsAny<InvoiceQueryFilters>(), "token",
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<SortOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedInvoiceResponse { Invoices = [], HasMore = false });
        var app = BoundaryTests.App(sdk, new HttpClient(new PdfHandler()), testServer: true);
        app.Configuration["GATEWAY_API_KEY"] = "test-key";
        app.UseMiddleware<SensitiveResponseHeadersMiddleware>();
        app.UseMiddleware<ErrorHandlingMiddleware>();
        app.UseMiddleware<ApiKeyMiddleware>();
        app.MapGet("/ksef/failure", (Func<IResult>)(() => throw new InvalidOperationException("private")));
        app.MapGet("/scalar/v1", () => "docs");
        app.MapGet("/openapi/v1.json", () => "{}");
        var pool = app.Services.GetRequiredService<TokenPool>();
        var states = (ConcurrentDictionary<string, AuthState>)typeof(TokenPool)
            .GetField("_states", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pool)!;
        states["1234567890"] = new AuthState { AccessToken = "token", AccessTokenExpiresAt = DateTimeOffset.MaxValue };
        return app;
    }

    private sealed class PdfHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("%PDF-test") });
    }
}
