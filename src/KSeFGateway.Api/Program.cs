using KSeF.Client.DI;
using KSeF.Client.Core.Interfaces.Clients;
using Scalar.AspNetCore;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Discovery;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Map env vars to configuration
builder.Configuration.AddEnvironmentVariables();

// Determine KSeF environment
var ksefEnv = builder.Configuration["KSEF_ENV"] ?? "TEST";
var baseUrl = ksefEnv.ToUpperInvariant() switch
{
    "PRODUCTION" or "PRD" => "https://api.ksef.mf.gov.pl",
    "DEMO" => "https://api-demo.ksef.mf.gov.pl",
    _ => "https://api-test.ksef.mf.gov.pl"
};

// Register CIRFMF KSeF SDK
builder.Services.AddKSeFClient(options =>
{
    options.BaseUrl = baseUrl;
});
builder.Services.AddCryptographyClient();

// Auth
builder.Services.AddSingleton<TokenManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TokenManager>());

// OpenAPI (.NET 9 built-in)
builder.Services.AddOpenApi();

// Configure request size for large invoices (up to 3MB with attachments)
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
});

var app = builder.Build();

// Middleware
app.UseMiddleware<ErrorHandlingMiddleware>();

// OpenAPI JSON + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "KSeF Gateway API";
    options.Theme = ScalarTheme.BluePlanet;
});

// Discover and map SDK endpoints
var endpoints = SdkReflector.DiscoverEndpoints();
app.MapDiscoveredEndpoints(endpoints);
app.MapHealthEndpoints(endpoints.Count);
app.MapWorkflowEndpoints();

app.Logger.LogInformation(
    "Discovered {Count} SDK endpoints across {Groups} groups",
    endpoints.Count,
    endpoints.Select(e => e.GroupName).Distinct().Count());

foreach (var group in endpoints.GroupBy(e => e.GroupName))
{
    app.Logger.LogInformation(
        "  {Group}: {Methods}",
        group.Key,
        string.Join(", ", group.Select(e => e.MethodName)));
}

app.Run();
