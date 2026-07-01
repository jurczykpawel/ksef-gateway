# ksef-gateway

Universal REST API gateway wrapping the official CIRFMF KSeF C# SDK.

## Stack
- **ksef-api**: ASP.NET 9 Minimal API + CIRFMF KSeF.Client SDK (NuGet, GitHub Packages)
- **ksef-pdf**: Node.js + tsx + CIRFMF ksef-pdf-generator (git submodule)
- **Orchestration**: Docker Compose (2 containers, no DB, no Redis)

## Quick start
```bash
cp .env.example .env  # set GITHUB_PAT and GATEWAY_API_KEY (openssl rand -hex 32)
docker compose --profile tools run --rm token-generator  # generates KSEF_TOKEN + KSEF_NIP
# paste output into .env
docker compose up --build
# API: http://localhost:8080 (every request except /health needs header X-Api-Key: <GATEWAY_API_KEY>)
# Docs: http://localhost:8080/scalar/v1 (needs the header too)
```

## Key files
- `src/KSeFGateway.Api/Discovery/SdkReflector.cs` - reflection engine (discovers 60+ SDK methods)
- `src/KSeFGateway.Api/Discovery/EndpointMapper.cs` - auto-registers POST /ksef/{group}/{method}
- `src/KSeFGateway.Api/Auth/TokenPool.cs` - multi-NIP KSeF auth (token or certificate/XAdES) + background refresh
- `src/KSeFGateway.Api/Auth/KsefContext.cs` - per-NIP context: token XOR certificate+key(+password)
- `src/KSeFGateway.Api/Auth/ContextProvider.cs` - loads NIP contexts, caps them at LicenseService.MaxNips
- `src/KSeFGateway.Api/Licensing/` - GATEWAY_LICENSE verification (LicenseService, LicenseVerifier, JwksClient, RevocationClient) - gates multi-NIP
- `src/KSeFGateway.Api/Endpoints/WorkflowEndpoints.cs` - high-level: /ksef/send, /ksef/send/json, /ksef/invoice/{nr}/pdf
- `src/KSeFGateway.Api/Endpoints/InvoiceDownloadEndpoints.cs` - /ksef/invoices/received, /ksef/invoices/received/new, /ksef/invoices/issued
- `src/KSeFGateway.Api/Endpoints/HealthEndpoints.cs` - /health, /ksef/status
- `src/KSeFGateway.Api/Middleware/ApiKeyMiddleware.cs` - fail-closed X-Api-Key check on every request except /health (gateway has no other caller-facing auth)
- `src/KSeFGateway.Api/Middleware/ErrorHandlingMiddleware.cs` - KsefApiException/KsefRateLimitException/KsefCircuitBreakerOpenException → HTTP responses
- `src/KSeFGateway.Api/Middleware/EndpointErrorHandling.cs` - shared `Guard()` helper so every endpoint handler rethrows KSeF errors to the middleware instead of flattening them into 500
- `pdf-service/src/server.ts` - PDF generation, JSON→XML conversion, QR codes
- `pdf-service/lib/` - git submodule: CIRFMF/ksef-pdf-generator
- `tools/TokenGenerator/` - one-command KSeF TEST token generator
- `tools/CertGenerator/` - one-command KSeF TEST certificate generator (self-signed, verifies auth, exports PEM)
- `tools/InvoiceDemo/` - E2E invoice send demo

## SDK
CIRFMF SDK on GitHub Packages (not public nuget.org). Requires GITHUB_PAT with `read:packages`.
`nuget.config` in `src/KSeFGateway.Api/` and `tools/*/` configures the source.

## Build
Everything builds inside Docker. No local .NET SDK required.
```bash
docker compose build                                    # all services
docker compose --profile tools build token-generator    # tools
```

## Tests
```bash
# .NET unit tests inside Docker (no local SDK required)
docker compose run --rm ksef-api-tests
# PDF service (local)
cd pdf-service && npm test
```
Note: `ksef-api`'s runtime image has no SDK and a fixed `ENTRYPOINT`, so it can't run `dotnet test` directly — `ksef-api-tests` is a separate compose service built from the `test` stage in `KSeFGateway.Api/Dockerfile` (profile `tools`, mirrors CI's `build-api` job, excludes `Category=Integration`).

## Architecture
- `LicenseService` (BackgroundService, mirrors TokenPool) verifies `GATEWAY_LICENSE` offline (ECDSA P-256/SHA-256) against Sellf's JWKS (product slug `ksef-gateway-multi-nip`, seller 83789f79-bdd7-4918-af1f-e56325fa5070) + a k-anonymity revocation check; `Program.cs` awaits one explicit `RefreshAsync()` right after `builder.Build()` so `ContextProvider`'s constructor already has a resolved `MaxNips` (1 = free, unlimited = licensed) before it truncates configured NIPs - JWKS unreachable fails closed (free tier), the revocation check fails open (never revoke a valid token just because the CRL is down)
- `ApiKeyMiddleware` runs first in the pipeline: rejects with 503 if `GATEWAY_API_KEY` isn't configured, 401 if the `X-Api-Key` header is missing/wrong, passes through `/health` unconditionally - the gateway authenticates itself to KSeF but has no other mechanism to authenticate its own callers
- `SdkReflector` discovers all methods on `IKSeFClient` (13 sub-interfaces) via .NET reflection at startup
- `EndpointMapper` registers each method as `POST /ksef/{group}/{method}` with dynamic JSON→SDK parameter mapping
- `TokenPool` (BackgroundService) authenticates per NIP via `IAuthCoordinator.AuthKsefTokenAsync()` (token) or `.AuthAsync()` with an XAdES `xmlSigner` built from a loaded `X509Certificate2` (certificate) and auto-refreshes at 80% TTL
- `WorkflowEndpoints` provide high-level flows: XML/JSON → encrypt → session → send → KSeF number
- `InvoiceDownloadEndpoints` wraps `QueryInvoiceMetadataAsync` (SubjectType=Subject2/buyer) for discovering received invoices without knowing their KSeF number; `/received/new` uses `DateType=PermanentStorage` + HWM for a stateless polling cursor; `/issued` is the seller-role mirror (SubjectType=Subject1, no polling variant - you already know when you sent something)
- Every endpoint handler runs through `EndpointErrorHandling.Guard()`, which rethrows `KsefApiException`/`KsefRateLimitException`/`KsefCircuitBreakerOpenException` (unwrapping `TargetInvocationException` from reflection-invoked calls first) so `ErrorHandlingMiddleware` can turn them into 502/429+Retry-After/503+Retry-After - everything else becomes a generic 500
- `pdf-service` uses xml-js for bidirectional JSON/XML conversion and CIRFMF lib for PDF rendering
- QR codes use SHA-256 hash of KSeF-canonical XML + P_1 date + seller NIP

## Git workflow
- **main**: stable releases only, tagged (v0.1.0, ...)
- **dev**: active development, feature branches merge here
- Feature branches: `feat/feature-name`, `fix/bug-name`
- Merge: `--no-ff` with descriptive commit message
- Never commit directly to main
