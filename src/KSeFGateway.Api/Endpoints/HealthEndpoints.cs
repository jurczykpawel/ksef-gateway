using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace KSeFGateway.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app, int discoveredCount)
    {
        app.MapGet("/health", (
            [FromServices] TokenPool pool,
            [FromServices] ContextProvider ctxProvider,
            [FromServices] IConfiguration config) =>
        {
            var defaultState = pool.GetDefaultState();
            return Results.Json(new HealthResponse(
                Status: "ok",
                DiscoveredEndpoints: discoveredCount,
                KsefEnvironment: config["KSEF_ENV"] ?? "TEST",
                Authenticated: defaultState.IsAuthenticated,
                TokenExpiresAt: defaultState.AccessTokenExpiresAt
            ));
        })
        .WithTags("System")
        .WithName("health")
        .ExcludeFromDescription();

        app.MapGet("/ksef/status", (
            [FromServices] TokenPool pool,
            [FromServices] ContextProvider ctxProvider,
            [FromServices] LicenseService licenseService,
            [FromServices] IConfiguration config) =>
        {
            var allStates = pool.GetAllStates();
            var contexts = ctxProvider.GetAll().Select(c =>
            {
                var state = allStates.GetValueOrDefault(c.Nip) ?? new AuthState();
                return new
                {
                    c.Nip,
                    c.Label,
                    authenticated = state.IsAuthenticated,
                    tokenExpiresAt = state.AccessTokenExpiresAt,
                    isDefault = c.Nip == ctxProvider.GetDefault()?.Nip
                };
            });

            return Results.Json(ApiResponse.Ok(new
            {
                mode = ctxProvider.IsMultiNip ? "multi-nip" : "single-nip",
                environment = config["KSEF_ENV"] ?? "TEST",
                contexts,
                license = new
                {
                    licensed = licenseService.IsLicensed,
                    maxNips = licenseService.IsLicensed ? (int?)null : licenseService.MaxNips,
                    activeNips = ctxProvider.GetAll().Count,
                    email = licenseService.Claims?.Email,
                    expiresAt = licenseService.Claims?.Exp is long exp
                        ? DateTimeOffset.FromUnixTimeSeconds(exp)
                        : (DateTimeOffset?)null,
                }
            }));
        })
        .WithTags("System")
        .WithName("ksef_status")
        .WithOpenApi();
    }
}
