using Microsoft.AspNetCore.Mvc;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Models;

namespace KSeFGateway.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app, int discoveredCount)
    {
        app.MapGet("/health", ([FromServices] TokenManager tokenManager, [FromServices] IConfiguration config) =>
        {
            var state = tokenManager.GetState();
            return Results.Json(new HealthResponse(
                Status: "ok",
                DiscoveredEndpoints: discoveredCount,
                KsefEnvironment: config["KSEF_ENV"] ?? "TEST",
                Authenticated: state.IsAuthenticated,
                TokenExpiresAt: state.AccessTokenExpiresAt
            ));
        })
        .WithTags("System")
        .WithName("health")
        .ExcludeFromDescription();

        app.MapGet("/ksef/status", async (
            [FromServices] TokenManager tokenManager,
            [FromServices] IConfiguration config) =>
        {
            var authState = tokenManager.GetState();

            return Results.Json(ApiResponse.Ok(new
            {
                gateway = new
                {
                    authenticated = authState.IsAuthenticated,
                    tokenExpiresAt = authState.AccessTokenExpiresAt,
                    canRefresh = authState.CanRefresh,
                    environment = config["KSEF_ENV"] ?? "TEST"
                }
            }));
        })
        .WithTags("System")
        .WithName("ksef_status")
        .WithOpenApi();
    }
}
