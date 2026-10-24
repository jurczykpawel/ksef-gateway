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
var ksefEnv = KsefEnvironment.Parse(builder.Configuration["KSEF_ENV"]);
builder.Services.AddSingleton(ksefEnv);

// Validate the public SDK surface before licensing/authentication can contact services.
var endpoints = SdkReflector.DiscoverEndpoints();
var sdkSelection = builder.Configuration["KSEF_SDK_ENDPOINTS"];
var exposedEndpoints = SdkEndpointPolicy.Select(endpoints, sdkSelection);

// Register CIRFMF KSeF SDK
builder.Services.AddKSeFClient(options =>
{
    options.BaseUrl = ksefEnv.BaseUrl;
}, http => http.AddKsefOperationRateLimiting());
builder.Services.AddCryptographyClient();

// Licensing: multi-NIP requires a valid GATEWAY_LICENSE (see LicenseService) - must be
// refreshed at startup; ContextProvider checks the current permission at each lookup.
builder.Services.AddSingleton(sp => new JwksClient(
    new HttpClient(),
    LicenseService.DefaultJwksUrl,
    sp.GetRequiredService<ILogger<JwksClient>>()));
builder.Services.AddSingleton(sp => new RevocationClient(
    new HttpClient(),
    LicenseService.DefaultRevocationUrl,
    sp.GetRequiredService<ILogger<RevocationClient>>()));
builder.Services.AddSingleton<LicenseService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LicenseService>());

// Auth: ContextProvider + TokenPool (multi-NIP)
builder.Services.AddSingleton<ContextProvider>();
builder.Services.AddSingleton<TokenPool>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TokenPool>());

// HTTP client for PDF service
builder.Services.AddHttpClient();

// OpenAPI: preserve the existing document contract across runtime upgrades.
builder.Services.AddGatewayOpenApi();

// Configure request size for large invoices (Kestrel only, Lambda has its own limits)
if (!isLambda)
{
    builder.WebHost.ConfigureKestrel(opts =>
    {
        opts.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
    });
}

var app = builder.Build();

// Resolve the initial license state before serving requests.
await app.Services.GetRequiredService<LicenseService>().RefreshAsync();

// Middleware - caller-facing auth runs first: fail fast on unauthenticated traffic
// before spending any rate-limit budget or reaching an endpoint.
// TrustedProxyMiddleware is opt-in (no-op unless TRUSTED_PROXY_SECRET is set) and runs before the
// API-key check so direct-to-origin traffic that bypasses the configured proxy is rejected first.
app.UseMiddleware<TrustedProxyMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

// OpenAPI JSON + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "KSeF Gateway API";
    options.Theme = ScalarTheme.BluePlanet;
});

// Discover and map SDK endpoints
app.MapDiscoveredEndpoints(exposedEndpoints);
app.MapHealthEndpoints(endpoints.Count, exposedEndpoints.Count);
app.MapWorkflowEndpoints();
app.MapInvoiceDownloadEndpoints();

if (sdkSelection?.Trim() == "all")
    app.Logger.LogWarning("all SDK endpoints exposed incl. token/permission/certificate administration");

app.Logger.LogInformation(
    "Exposed {ExposedCount} of {Count} discovered SDK endpoints across {Groups} discovered groups",
    exposedEndpoints.Count,
    endpoints.Count,
    endpoints.Select(e => e.GroupName).Distinct().Count());

foreach (var group in exposedEndpoints.GroupBy(e => e.GroupName))
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
