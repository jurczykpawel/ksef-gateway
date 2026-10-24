using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace KSeFGateway.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app, int discoveredCount, int exposedCount)
    {
        app.MapGet("/health", () => Results.Json(new { status = "ok" }))
        .WithTags("System")
        .WithName("health")
        .ExcludeFromDescription();

        app.MapGet("/ksef/status", (
            [FromServices] TokenPool pool,
            [FromServices] ContextProvider ctxProvider,
            [FromServices] LicenseService licenseService,
            [FromServices] KsefEnvironment environment,
            HttpContext httpContext) =>
        {
            var allStates = pool.GetAllStates();
            var activeContexts = ApiKeyAccess.VisibleContexts(httpContext, ctxProvider);
            var contexts = activeContexts.Select(c =>
            {
                var state = allStates.GetValueOrDefault(c.Nip) ?? new AuthState();
                return new
                {
                    c.Nip,
                    c.Label,
                    authenticated = state.IsAuthenticated,
                    tokenExpiresAt = state.AccessTokenExpiresAt,
                    isDefault = c.Nip == ApiKeyAccess.DefaultNip(httpContext, ctxProvider)
                };
            });

            if (ApiKeyAccess.AllowedNips(httpContext) != null)
                return Results.Json(ApiResponse.Ok(new { contexts }));

            return Results.Json(ApiResponse.Ok(new
            {
                discoveredEndpoints = discoveredCount,
                exposedEndpoints = exposedCount,
                mode = activeContexts.Count > 1 ? "multi-nip" : "single-nip",
                environment = environment.Name,
                contexts,
                license = new
                {
                    licensed = licenseService.IsLicensed,
                    maxNips = licenseService.IsLicensed ? (int?)null : licenseService.MaxNips,
                    activeNips = activeContexts.Count,
                    email = licenseService.Claims?.Email,
                    expiresAt = licenseService.Claims?.Exp is long exp
                        ? DateTimeOffset.FromUnixTimeSeconds(exp)
                        : (DateTimeOffset?)null,
                }
            }));
        })
        .WithTags("System")
        .WithName("ksef_status");
    }
}
