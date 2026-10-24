using System.Net;
using System.Text.Json;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KSeFGateway.Api.Tests.Endpoints;

public class PdfBoundaryTests
{
    [Theory]
    [InlineData(200)] [InlineData(500)]
    public async Task PdfResponse_DisposesRequestAndResponse(int status)
    {
        var content = new TrackedContent();
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content }));
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var config = new ConfigurationBuilder().Build();
        var response = await PdfService.RequestAsync(factory.Object, config, "/pdf/invoice", "<Faktura/>", "application/xml", default);
        Assert.Equal(status, (int)response.Status);
        Assert.Equal(new byte[] { 1, 2 }, response.Body);
        Assert.True(content.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task PdfTransportTimeout_Returns504WithCorrelationId()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new TaskCanceledException("private timeout detail")));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await new ErrorHandlingMiddleware(async _ => {
            await PdfService.RequestAsync(factory.Object, new ConfigurationBuilder().Build(), "/pdf/invoice", "xml", "application/xml", default);
        }, NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(ctx);
        Assert.Equal(504, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Equal("PDF service timeout", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(ctx.Response.Headers["X-Correlation-Id"].ToString(), body.RootElement.GetProperty("correlationId").GetString());
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return send(request, ct);
        }
    }

    private sealed class TrackedContent() : ByteArrayContent([1, 2])
    {
        public bool Disposed;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
