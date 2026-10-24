using System.Reflection;
using System.Text;
using System.Text.Json;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Authorization;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Discovery;
using KSeFGateway.Api.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KSeFGateway.Api.Tests.Discovery;

public class EndpointMapperTests
{
    private static EndpointMetadata Find(string method) => SdkReflector.DiscoverEndpoints()
        .Single(e => e.MethodName == method);

    [Fact]
    public async Task ChunkedBody_BindsRequest()
    {
        var result = await Invoke(Find("close-online-session"), "{\"sessionReferenceNumber\":\"session-123\"}", chunked: true);
        Assert.Equal(200, result.Status);
        Assert.Equal("session-123", result.Arguments![0]);
        Assert.Equal("fake-access", result.Arguments[1]);
        Assert.IsType<CancellationToken>(result.Arguments[2]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"sessionReferenceNumber\":null}")]
    public async Task MissingBusinessParameter_Returns400(string json)
    {
        var result = await Invoke(Find("close-online-session"), json);
        Assert.Equal(400, result.Status);
        Assert.Contains("sessionReferenceNumber", result.Body);
        Assert.Null(result.Arguments);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"sessionReferenceNumber\":123}")]
    [InlineData("[]")]
    public async Task MalformedJson_Returns400(string json)
    {
        var result = await Invoke(Find("close-online-session"), json);
        Assert.Equal(400, result.Status);
        Assert.False(JsonDocument.Parse(result.Body).RootElement.GetProperty("success").GetBoolean());
        Assert.Null(result.Arguments);
    }

    [Fact]
    public async Task SingleCompositeParameter_BindsWholeBody()
    {
        var endpoint = Find("submit-ksef-token-auth-request");
        var result = await Invoke(endpoint, "{\"challenge\":\"request-challenge\"}");
        Assert.Equal(200, result.Status);
        var request = result.Arguments![endpoint.BusinessParameters[0].Position];
        Assert.Equal("request-challenge", request!.GetType().GetProperty("Challenge")!.GetValue(request));
    }

    [Theory]
    [InlineData("{\"sessionReferenceNumber\":\"session-123\"}", "requestPayload")]
    [InlineData("{\"requestPayload\":{}}", "sessionReferenceNumber")]
    public async Task MultipleParameters_DoNotBindWholeBody(string json, string missing)
    {
        var result = await Invoke(Find("send-online-session-invoice"), json);
        Assert.Equal(400, result.Status);
        Assert.Contains(missing, result.Body);
        Assert.Null(result.Arguments);
    }

    [Fact]
    public async Task NamedCompositeParameter_AndOptionalDefault_Bind()
    {
        var result = await Invoke(Find("open-online-session"), "{\"requestPayload\":{\"formCode\":{\"systemCode\":\"FA (3)\"}}}");
        Assert.Equal(200, result.Status);
        var request = JsonSerializer.SerializeToElement(result.Arguments![0]);
        Assert.Equal("FA (3)", request.GetProperty("FormCode").GetProperty("SystemCode").GetString());
        var endpoint = Find("open-online-session");
        foreach (var parameter in endpoint.BusinessParameters.Where(p => p.HasDefaultValue))
            Assert.Equal(parameter.DefaultValue, result.Arguments[parameter.Position]);
    }

    private static async Task<(int Status, string Body, object?[]? Arguments)> Invoke(
        EndpointMetadata metadata, string json, bool chunked = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KSEF_CONTEXTS_FILE"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"),
            ["KSEF_NIP"] = "1234567890", ["KSEF_TOKEN"] = "fake"
        });
        using var http = new HttpClient(new RejectNetwork());
        using var license = new LicenseService(
            new JwksClient(http, "https://unused.invalid", NullLogger<JwksClient>.Instance),
            new RevocationClient(http, "https://unused.invalid", NullLogger<RevocationClient>.Instance),
            builder.Configuration, NullLogger<LicenseService>.Instance);
        var provider = new ContextProvider(builder.Configuration, NullLogger<ContextProvider>.Instance, license);
        builder.Services.AddSingleton(provider);
        builder.Services.AddSingleton<TokenPool>(sp => new FakePool(sp, provider));
        var client = DispatchProxy.Create<IKSeFClient, RecordingClient>();
        builder.Services.AddSingleton(client);
        await using var app = builder.Build();
        app.MapDiscoveredEndpoints([metadata]);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>().Single();
        var bytes = Encoding.UTF8.GetBytes(json);
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = chunked ? null : bytes.Length;
        if (chunked) context.Request.Headers.TransferEncoding = "chunked";
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(),
            ((RecordingClient)client).Arguments);
    }

    public class RecordingClient : DispatchProxy
    {
        public object?[]? Arguments;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Arguments = args;
            if (targetMethod!.ReturnType == typeof(Task)) return Task.CompletedTask;
            var type = targetMethod.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type)
                .Invoke(null, [type.IsValueType ? Activator.CreateInstance(type) : null]);
        }
    }

    private sealed class FakePool(IServiceProvider services, ContextProvider provider)
        : TokenPool(services, provider, NullLogger<TokenPool>.Instance)
    {
        protected override Task<AuthenticationOperationStatusResponse> AuthenticateAsync(string nip, CancellationToken ct) =>
            Task.FromResult(JsonSerializer.Deserialize<AuthenticationOperationStatusResponse>(
                "{\"AccessToken\":{\"Token\":\"fake-access\",\"ValidUntil\":\"9999-01-01T00:00:00Z\"}}")!);
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Tests must not access the network");
    }
}
