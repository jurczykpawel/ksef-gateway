using Amazon.Lambda.AspNetCoreServer.Hosting;
using KSeF.Client.DI;
using KSeF.Client.Core.Interfaces.Clients;
using Scalar.AspNetCore;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Discovery;
using KSeFGateway.Api.Endpoints;
using KSeFGateway.Api.Licensing;
using KSeFGateway.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Auto-detect AWS Lambda environment
var isLambda = Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME") is not null;
if (isLambda)
    builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

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

// Licensing: multi-NIP requires a valid GATEWAY_LICENSE (see LicenseService) - must be
// registered and refreshed before ContextProvider is resolved, since it decides MaxNips.
builder.Services.AddSingleton(sp => new JwksClient(
    new HttpClient(),
    builder.Configuration["GATEWAY_LICENSE_JWKS_URL"] ?? LicenseService.DefaultJwksUrl,
    sp.GetRequiredService<ILogger<JwksClient>>()));
builder.Services.AddSingleton(sp => new RevocationClient(
    new HttpClient(),
    builder.Configuration["GATEWAY_LICENSE_REVOCATION_URL"] ?? LicenseService.DefaultRevocationUrl,
    sp.GetRequiredService<ILogger<RevocationClient>>()));
builder.Services.AddSingleton<LicenseService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LicenseService>());

// Auth: ContextProvider + TokenPool (multi-NIP)
builder.Services.AddSingleton<ContextProvider>();
builder.Services.AddSingleton<TokenPool>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TokenPool>());

// HTTP client for PDF service
builder.Services.AddHttpClient();

// OpenAPI (.NET 9 built-in)
builder.Services.AddOpenApi();

// Configure request size for large invoices (Kestrel only, Lambda has its own limits)
if (!isLambda)
{
    builder.WebHost.ConfigureKestrel(opts =>
    {
        opts.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
    });
}

var app = builder.Build();

// Verify the multi-NIP license before anything resolves ContextProvider (which reads
// LicenseService.MaxNips synchronously in its constructor) - must happen first, blocking.
await app.Services.GetRequiredService<LicenseService>().RefreshAsync();

// Middleware
app.UseMiddleware<RateLimitMiddleware>();
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

var contextProvider = app.Services.GetRequiredService<ContextProvider>();
app.Logger.LogInformation("Contexts: {Count} ({Mode} mode)",
    contextProvider.GetAll().Count,
    contextProvider.IsMultiNip ? "multi-NIP" : "single-NIP");

app.Run();
