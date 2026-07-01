using KSeF.Client.Core.Exceptions;
using KSeFGateway.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace KSeFGateway.Api.Tests.Middleware;

public class EndpointErrorHandlingTests
{
    [Fact]
    public async Task Guard_ActionSucceeds_ReturnsItsResult()
    {
        var result = await EndpointErrorHandling.Guard(() => Task.FromResult(Results.Ok("done")));

        var value = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.Equal("done", value.Value);
    }

    [Fact]
    public async Task Guard_KsefRateLimitException_PropagatesForMiddlewareToHandle()
    {
        var thrown = new KsefRateLimitException("rate limited", 30, null, null!);

        var caught = await Assert.ThrowsAsync<KsefRateLimitException>(() =>
            EndpointErrorHandling.Guard(() => throw thrown));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task Guard_KsefApiException_Propagates()
    {
        var thrown = new KsefApiException("upstream error", System.Net.HttpStatusCode.BadRequest, null!, null!);

        var caught = await Assert.ThrowsAsync<KsefApiException>(() =>
            EndpointErrorHandling.Guard(() => throw thrown));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task Guard_KsefCircuitBreakerOpenException_Propagates()
    {
        var thrown = new KsefCircuitBreakerOpenException("circuit open", TimeSpan.FromSeconds(30));

        var caught = await Assert.ThrowsAsync<KsefCircuitBreakerOpenException>(() =>
            EndpointErrorHandling.Guard(() => throw thrown));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task Guard_KsefRateLimitExceptionWrappedInTargetInvocationException_UnwrapsAndPropagates()
    {
        // Mirrors how EndpointMapper invokes SDK methods via reflection - exceptions come
        // back wrapped in TargetInvocationException.
        var inner = new KsefRateLimitException("rate limited", 5, null, null!);
        var wrapped = new System.Reflection.TargetInvocationException(inner);

        var caught = await Assert.ThrowsAsync<KsefRateLimitException>(() =>
            EndpointErrorHandling.Guard(() => throw wrapped));

        Assert.Same(inner, caught);
    }

    [Fact]
    public async Task Guard_GenericException_ReturnsGeneric500WithMessage()
    {
        var result = await EndpointErrorHandling.Guard(() => throw new InvalidOperationException("boom"));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var body = Assert.IsType<KSeFGateway.Api.Models.ApiResponse>(value.Value);
        Assert.False(body.Success);
        Assert.Equal("boom", body.Error);
    }

    [Fact]
    public async Task Guard_GenericExceptionWrappedInTargetInvocationException_UnwrapsMessage()
    {
        var wrapped = new System.Reflection.TargetInvocationException(new InvalidOperationException("inner boom"));

        var result = await EndpointErrorHandling.Guard(() => throw wrapped);

        var value = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var body = Assert.IsType<KSeFGateway.Api.Models.ApiResponse>(value.Value);
        Assert.Equal("inner boom", body.Error);
    }
}
