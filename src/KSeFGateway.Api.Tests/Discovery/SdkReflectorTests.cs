using KSeFGateway.Api.Discovery;

namespace KSeFGateway.Api.Tests.Discovery;

public class SdkReflectorTests
{
    private readonly IReadOnlyList<EndpointMetadata> _endpoints = SdkReflector.DiscoverEndpoints();

    [Fact]
    public void DiscoverEndpoints_ReturnsNonEmpty()
    {
        Assert.NotEmpty(_endpoints);
    }

    [Fact]
    public void DiscoverEndpoints_FindsAtLeast50Endpoints()
    {
        Assert.True(_endpoints.Count >= 50, $"Expected at least 50 endpoints, got {_endpoints.Count}");
    }

    [Fact]
    public void DiscoverEndpoints_ContainsOnlineSessionGroup()
    {
        var group = _endpoints.Where(e => e.GroupName == "online-session").ToList();
        Assert.NotEmpty(group);
        Assert.Contains(group, e => e.MethodName == "open-online-session");
        Assert.Contains(group, e => e.MethodName == "send-online-session-invoice");
        Assert.Contains(group, e => e.MethodName == "close-online-session");
    }

    [Fact]
    public void DiscoverEndpoints_ContainsInvoiceDownloadGroup()
    {
        var group = _endpoints.Where(e => e.GroupName == "invoice-download").ToList();
        Assert.NotEmpty(group);
        Assert.Contains(group, e => e.MethodName == "get-invoice");
        Assert.Contains(group, e => e.MethodName == "query-invoice-metadata");
    }

    [Fact]
    public void DiscoverEndpoints_ContainsKsefTokenGroup()
    {
        var group = _endpoints.Where(e => e.GroupName == "ksef-token").ToList();
        Assert.Equal(4, group.Count);
    }

    [Fact]
    public void DiscoverEndpoints_ExcludesAccessTokenParameter()
    {
        foreach (var endpoint in _endpoints)
        {
            Assert.DoesNotContain(endpoint.BusinessParameters, p => p.Name == "accessToken");
        }
    }

    [Fact]
    public void DiscoverEndpoints_ExcludesCancellationTokenParameter()
    {
        foreach (var endpoint in _endpoints)
        {
            Assert.DoesNotContain(endpoint.BusinessParameters,
                p => p.ParameterType == typeof(CancellationToken));
        }
    }

    [Fact]
    public void DiscoverEndpoints_NoDuplicateRoutes()
    {
        var routes = _endpoints.Select(e => e.Route).ToList();
        var duplicates = routes.GroupBy(r => r).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void DiscoverEndpoints_AllRoutesStartWithKsef()
    {
        foreach (var endpoint in _endpoints)
        {
            Assert.StartsWith("/ksef/", endpoint.Route);
        }
    }

    [Fact]
    public void DiscoverEndpoints_GroupNamesAreKebabCase()
    {
        foreach (var endpoint in _endpoints)
        {
            Assert.Matches(@"^[a-z][a-z0-9-]*$", endpoint.GroupName);
        }
    }

    [Fact]
    public void DiscoverEndpoints_MethodNamesAreKebabCase()
    {
        foreach (var endpoint in _endpoints)
        {
            Assert.Matches(@"^[a-z][a-z0-9-]*$", endpoint.MethodName);
        }
    }

    [Fact]
    public void DiscoverEndpoints_InvoiceDownloadGroupHasNoDuplicateExportInvoices()
    {
        var exports = _endpoints.Where(e =>
            e.GroupName == "invoice-download" && e.MethodName == "export-invoices").ToList();
        Assert.Single(exports);
    }
}
