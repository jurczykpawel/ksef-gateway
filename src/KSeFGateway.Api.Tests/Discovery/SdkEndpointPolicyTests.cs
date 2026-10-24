using System.Net;
using System.Text.Json;
using KSeFGateway.Api.Discovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace KSeFGateway.Api.Tests.Discovery;

public class SdkEndpointPolicyTests(ITestOutputHelper output)
{
    private readonly IReadOnlyList<EndpointMetadata> _all = SdkReflector.DiscoverEndpoints();
    private static readonly string[] SafeGroups =
        ["batch-session", "invoice-download", "online-session", "session-status"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("safe")]
    [InlineData(" safe ")]
    public void Default_ExposesExactlySafeGroups(string? value)
    {
        var selected = SdkEndpointPolicy.Select(_all, value);
        Assert.Equal(SafeGroups, selected.Select(e => e.GroupName).Distinct().Order());
        Assert.Equal(_all.Where(e => SafeGroups.Contains(e.GroupName)), selected);
        Assert.DoesNotContain(selected, e => new[] { "ksef-token", "permissions", "certificate", "authorization" }.Contains(e.GroupName));
    }

    [Fact]
    public void All_ExposesEveryDiscoveredEndpoint()
    {
        foreach (var group in _all.GroupBy(e => e.GroupName).OrderBy(g => g.Key))
            output.WriteLine($"{group.Key} ({group.Count()}): {string.Join(", ", group.Select(e => e.MethodName))}");
        Assert.Equal(64, _all.Count);
        Assert.Equal(14, _all.Select(e => e.GroupName).Distinct().Count());
        Assert.Equal(_all, SdkEndpointPolicy.Select(_all, " all "));
    }

    [Theory]
    [InlineData("online-session,invoice-download")]
    [InlineData(" online-session , invoice-download/get-invoice ")]
    [InlineData("certificate,grant-permission,ksef-token")]
    [InlineData("invoice-download/get-invoice")]
    [InlineData("online-session,online-session/open-online-session,online-session")]
    public void ExplicitList_ExposesExactlySelectedRoutesWithoutDuplicates(string value)
    {
        var names = value.Split(',').Select(s => s.Trim()).ToArray();
        var expected = _all.Where(e => names.Contains(e.GroupName) || names.Contains($"{e.GroupName}/{e.MethodName}"));
        Assert.Equal(expected, SdkEndpointPolicy.Select(_all, value));
    }

    [Theory]
    [InlineData("typo")]
    [InlineData("invoice-download/typo")]
    [InlineData("typo/get-invoice")]
    [InlineData("safe,online-session")]
    [InlineData("all,certificate")]
    [InlineData("online-session,")]
    [InlineData(",")]
    [InlineData("/online-session")]
    [InlineData("online-session/")]
    [InlineData("online-session/open-online-session/extra")]
    [InlineData("ONLINE-SESSION")]
    public void InvalidSelection_FailsWithAvailableGroupsAndMethods(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => SdkEndpointPolicy.Select(_all, value));
        Assert.Contains("KSEF_SDK_ENDPOINTS", error.Message);
        foreach (var endpoint in _all)
        {
            Assert.Contains(endpoint.GroupName, error.Message);
            Assert.Contains($"{endpoint.GroupName}/{endpoint.MethodName}", error.Message);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("all", true)]
    [InlineData("ksef-token", true)]
    public async Task Mapping_HiddenRoutesReturn404AndAreAbsentFromOpenApi(string? selection, bool exposeToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi();
        await using var app = builder.Build();
        app.MapDiscoveredEndpoints(SdkEndpointPolicy.Select(_all, selection));
        app.MapOpenApi();
        // Prove routing without resolving authentication services or calling KSeF.
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.DisplayName?.StartsWith("HTTP: POST /ksef/") == true)
            {
                context.Response.StatusCode = 204;
                return;
            }
            await next(context);
        });
        await app.StartAsync();
        var client = app.GetTestClient();
        var tokenRoute = _all.First(e => e.GroupName == "ksef-token").Route;
        Assert.Equal(exposeToken ? HttpStatusCode.NoContent : HttpStatusCode.NotFound,
            (await client.PostAsync(tokenRoute, null)).StatusCode);
        var invoiceRoute = _all.First(e => e.GroupName == "invoice-download").Route;
        Assert.Equal(selection == "ksef-token" ? HttpStatusCode.NotFound : HttpStatusCode.NoContent,
            (await client.PostAsync(invoiceRoute, null)).StatusCode);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(SdkEndpointPolicy.Select(_all, selection).Select(e => e.Route).Order(), paths);
        Assert.Equal(exposeToken, paths.Contains(tokenRoute));
    }
}
