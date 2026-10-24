# Contributing to KSeF Gateway

Thanks for your interest in contributing!

## Development Setup

### Prerequisites

- Docker & Docker Compose
- GitHub PAT with `read:packages` (for CIRFMF SDK)
- Optionally: .NET 10 SDK (for local development without Docker)

### Running locally

```bash
git submodule update --init --recursive
cp .env.example .env
# Set GITHUB_PAT and mandatory GATEWAY_API_KEY in .env (generate: openssl rand -hex 32).
# Configure a KSeF TEST token + NIP OR a TEST certificate + private key + NIP.
# Generate disposable TEST credentials if needed:
docker compose --profile tools run --rm token-generator
# Copy the generated token/NIP into .env; cert-generator is the certificate alternative.
docker compose up --build
```

API at `http://localhost:8080`, docs at `http://localhost:8080/scalar/v1`. All routes except `/health` require `X-Api-Key` matching `GATEWAY_API_KEY`. Do not commit `.env`, certificates or keys.

### Running tests

```bash
# .NET unit tests (no local SDK required)
docker compose --env-file .env --profile tools run --rm --build ksef-api-tests

# PDF service
(cd pdf-service && npm ci && npx vitest run && npx tsc --noEmit)

# Landing
(cd landing && npm ci && npm run build)

# Full integration suite: starts/reuses localhost:8080 and sends invoices to KSeF TEST
./scripts/test-full.sh
```

The unfiltered PDF `tsc --noEmit` currently reports diagnostics from the upstream `lib/` submodule and `TS6059` (files outside `rootDir`). CI and the typecheck hook filter those diagnostics; a full typecheck failure must not be reported as a clean result.

Install hooks with `pre-commit install && pre-commit install --hook-type pre-push`. The pre-commit hook runs .NET unit tests in Docker for C# changes and PDF checks for PDF changes; Docker and `.env` must be available. The pre-push hook runs `scripts/test-full.sh` against KSeF TEST. Unit tests exclude `Category=Integration`; CI also runs a separate integration job with fresh TEST credentials.

## Testy i regresja

One command for every layer: `./scripts/test-full.sh --all` (Docker, Node 22.23.4+, TEST credentials in `.env`). The pre-push default remains `./scripts/test-full.sh`. `--all` fails if integration prerequisites are missing; the default retains its existing skip behavior. Never point integration tests at production.

| Layer | Command / automation | Scope |
| --- | --- | --- |
| Fast .NET | `docker compose --env-file .env --profile tools run --rm --build ksef-api-tests`; pre-commit | No KSeF calls. TestServer runs the actual `Program` pipeline with local license HTTP and fake SDK/auth. Includes `DocsTests` validating both README examples and shared fixtures. |
| Live TEST | `./scripts/test-full.sh`; pre-push; CI `integration` | SDK authentication, XML/JSON sends, downloads and metadata. CI generates a fresh disposable NIP, so it does not share a developer's quota. |
| Published images | `docker-publish.yml` → `smoke-image` | Runs immutable API/PDF digests, checks health, authenticated TEST status, sends **one** invoice and checks PDF MIME/signature. No metadata queries. Release/branch/latest aliases and release SBOM attachment wait for smoke success. Requires repository secrets `KSEF_TOKEN`, `KSEF_NIP`, `CIRFMF_PAT`. |
| n8n | `node --test "examples/n8n/test/*.test.mjs"`; CI `test-n8n` | Offline harness: code nodes, HMAC, deduplication and retry semantics. No deployed n8n instance is called. |
| PDF | `(cd pdf-service && npm ci && npx vitest run)`; CI `pdf-service` | PDF/QR, XML↔JSON and secret gate. The `--all` typecheck uses the same upstream-diagnostic filtering as CI; unfiltered `npx tsc --noEmit` still reports known submodule/TS6059 errors. |
| Landing | `(cd landing && npm ci && npm run build)`; CI `build-landing` | Build only; interactive/browser behavior remains a gap. |

`regression-nightly.yml` reuses the same CI workflow once daily (02:17 UTC) and supports `workflow_dispatch`; results are visible in Actions, without extra notifications. Its concurrency group prevents overlapping nightly/manual runs. Other push/PR runs still use independent generated TEST NIPs. API/tests/tools have a failing NuGet vulnerability gate (including transitive packages); npm gates reject high/critical findings (PDF production dependencies, all landing dependencies).

The class fixture shares one self-invoice between XML/PDF and seller/buyer list tests, independent of test order. It waits once for indexing before issuing one successful metadata query per role; it does not poll an empty successful list. With `/received/new`, a normal run spends **3 metadata calls** (previously 3–13 with polling). This assumes the small TEST fixture fits one metadata page; the polling endpoint may paginate real data. A short `Retry-After` can add rejected attempts, with at most 15 seconds total waiting and 3 attempts; exhausted quota fails with `limit KSeF TEST wyczerpany, spróbuj po HH:MM UTC`. A missing header is explicitly reported. Indexing taking longer than 20 seconds causes a visible assertion failure instead of consuming more query quota.

### Macierz endpointów

Names below are real test method names in `src/KSeFGateway.Api.Tests` (found from the source). **LUKA** means there is no dedicated test for that route/scenario; it is not a claim that behavior is broken. `—` means not applicable (no upstream operation or no business input). Coverage of a shared helper is labelled separately and does not imply that each SDK operation has been exercised end to end.

Abbreviations used in cells:

- **KEY** = `HostRegressionTests.ProtectedRoutes_MissingAndWrongKey_Return401` (both missing and wrong key, explicit routes).
- **CTX** = `HostRegressionTests.InvalidContext_HasErrorEnvelopeWithoutSdk` (400/404 on XML download).
- **ERR** = `HostRegressionTests.SdkFailure_ThroughHandler_HasHttpContract` (429 + Retry-After, 503 + Retry-After, 502 and generic 500, full JSON envelope, XML download).
- **SYS** = `HostRegressionTests.SystemContracts_ExposeTwoContextsWithoutSecrets` (strict field allowlist, two NIPs, license and authentication fields).
- **SEND** = shared send helper tests: `SendInvoiceTests.Send_Status429_PreservesRetryAfter`, `Send_StatusCircuitBreaker_PreservesRetryAfter`, `Send_PollException_ClosesSessionWithoutMaskingPrimaryError`; not separate host tests of all three send routes.
- **CANCEL-SEND** = shared helper `SendInvoiceTests.Send_Cancellation_PropagatesAndClosesSession`.
- **COMMON** = middleware/helper tests only: `ApiKeyMiddlewareTests.KeyConfigured_NoHeaderProvided_RejectsWith401`, `KeyConfigured_WrongHeaderProvided_RejectsWith401`; `NipValidationTests.Resolver_UnknownHeader_Returns404`; `ErrorHandlingMiddlewareTests.RateLimitException_Returns429WithRetryAfter`, `CircuitBreakerOpenException_Returns503WithRetryAfter`, `KsefApiException_Returns502`; `EndpointErrorHandlingTests.Guard_Cancellation_Propagates`.

| Route | Verb | Success / JSON contract | Bad input | Missing/wrong key | Missing context | Upstream 429 / 503 / 502 | Cancellation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `/health` | GET | `Health_HasOnlyPublicLivenessField` | — | `Health_HasOnlyPublicLivenessField` (public) | — | — | — |
| `/ksef/status` | GET | SYS; `HealthEndpointsTests.Environment_ReturnsCanonicalName` | — | KEY | `Environment_ReturnsCanonicalName` (empty configuration) | — | — |
| `/ksef/contexts` | GET | SYS | — | KEY | LUKA (empty configuration) | — | — |
| `/ksef/send` | POST | `GatewayIntegrationTests.SendRawXml_ReturnsKsefNumber` | `SendRawXml_EmptyBody_Returns400`, `SendRawXml_InvalidXml_Returns400` | KEY | LUKA; COMMON | `OperationRateLimitTests.FriendlyAndRawSend_ShareSessionOpenBudget` (429); SEND; LUKA (503/502 host) | CANCEL-SEND; LUKA (host) |
| `/ksef/send/json` | POST | `GatewayIntegrationTests.SendXmlJsJson_ReturnsKsefNumber` | LUKA | KEY | LUKA; COMMON | SEND; LUKA (host) | CANCEL-SEND; LUKA (host) |
| `/ksef/invoice` | POST | `GatewayIntegrationTests.SendFriendlyJson_ReturnsKsefNumber` | `BoundaryTests.InvalidInvoice_Returns400WithoutSdkCall`, `ValidationErrors_ListFields` | KEY | LUKA; COMMON | SEND; LUKA (host) | CANCEL-SEND; LUKA (host) |
| `/ksef/invoice/{ksefNumber}` | GET | `TwoContexts_HeaderAndDefault_SelectCorrectToken`; `GetInvoiceXml_AfterSend_ReturnsXml` | CTX | KEY | CTX | ERR | `DownloadCancellation_ReachesSdkWithout500` |
| `/ksef/invoice/{ksefNumber}/pdf` | GET | `GetInvoicePdf_AfterSend_ReturnsPdf`; `PublishedImage_HealthStatusSendAndPdf` | LUKA | KEY | LUKA; COMMON | LUKA; COMMON; `PdfBoundaryTests.PdfTransportTimeout_Returns504WithCorrelationId` (504) | `BoundaryTests.PdfRequestAborted_CancelsUpstream` |
| `/ksef/invoices/received` | GET | `ListReceivedInvoices_AfterSelfInvoice_FindsItAsBuyer` | LUKA | KEY | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/invoices/received/new` | GET | `ListNewReceivedInvoices_SinceNow_ReturnsEmptyWithCheckpoint`; `OperationRateLimitTests.Poll_FivePages_ConsumesFiveQueryPermits` | LUKA | KEY | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/invoices/issued` | GET | `ListIssuedInvoices_AfterSend_FindsItAsSeller` | LUKA | KEY | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/openapi/{documentName}.json` | GET | `OpenApiContractTests.Document_PreservesNet9RoutesAndIntegerQuerySchemas` | LUKA (unknown document) | KEY | — | — | — |
| `/scalar/{documentName?}` | GET | LUKA (UI render); `EveryMappedRoute_HasCoverageEntry` (registration) | LUKA | KEY | — | — | — |
| `/scalar/favicon.svg` | GET | LUKA (framework asset) | — | LUKA; COMMON | — | — | — |
| `/scalar/scalar.aspnetcore.js` | GET | LUKA (framework asset) | — | LUKA; COMMON | — | — | — |
| `/scalar/scalar.js` | GET | LUKA (framework asset) | — | LUKA; COMMON | — | — | — |

The following **20** SDK routes are exposed by default (`safe`), all POST. Registration, parameter metadata and response types are guarded by `SdkContractTests.SdkContract_MatchesApprovedManifest`, `SdkEndpointPolicyTests.Default_ExposesExactlySafeGroups` and `HostRegressionTests.EveryMappedRoute_HasCoverageEntry`. The remaining 44 discovered routes require an explicit policy or `all`; `All_ExposesEveryDiscoveredEndpoint` and `Mapping_HiddenRoutesReturn404AndAreAbsentFromOpenApi` cover that policy. The approved manifest is the complete discovered surface. The table deliberately distinguishes manifest coverage from execution coverage.

| Route | Success | Bad input | Missing/wrong key | Missing context | Upstream 429 / 503 / 502 | Cancellation |
| --- | --- | --- | --- | --- | --- | --- |
| `/ksef/batch-session/close-batch-session` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/batch-session/open-batch-session` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/batch-session/send-batch-parts` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/batch-session/send-batch-parts-with-stream` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/invoice-download/export-invoices` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/invoice-download/get-invoice` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/invoice-download/get-invoice-export-status` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/invoice-download/query-invoice-metadata` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/online-session/close-online-session` | `EndpointMapperTests.ChunkedBody_BindsRequest` | `EndpointMapperTests.MissingBusinessParameter_Returns400`, `MalformedJson_Returns400` | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/online-session/open-online-session` | `EndpointMapperTests.NamedCompositeParameter_AndOptionalDefault_Bind` | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/online-session/send-online-session-invoice` | LUKA (manifest only) | `EndpointMapperTests.MultipleParameters_DoNotBindWholeBody` | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-session-failed-invoices` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-session-invoice` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-session-invoice-upo-by-ksef-number` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-session-invoice-upo-by-reference-number` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-session-invoices` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-session-status` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-session-upo` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-sessions` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |
| `/ksef/session-status/get-upo` | LUKA (manifest only) | LUKA | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON | LUKA; COMMON |

`EveryMappedRoute_HasCoverageEntry` uses the real host's `EndpointDataSource`, not source grep, to reject new explicit routes missing from its expected list; it also requires every mapped route to have a row here. Keep both lists current. `FreeLicense_HostShowsOneContextAndRejectsSecondNip` additionally checks the free license at HTTP level (one visible context, second NIP rejected with 404). Separate guardrails include `DocsTests.ReadmeInvoices_AreValidDtosAndMatchFixture`, `Translations_ContainSameInvoiceExamples`, `ExampleFiles_ConformToTheirInvoiceContracts`, `GatewayCurlExamples_IncludeAuthenticationAndBodyMediaType`, `TokenPoolTests` and `ContextProviderTests` (license recovery/revocation). These complement the endpoint matrix; they do not close its LUKA cells.

## Making Changes

1. Fork the repository
2. Create a feature branch: `git checkout -b feat/your-feature`
3. Make your changes
4. Verify the build: `docker-compose build`
5. Commit with a descriptive message
6. Push and open a Pull Request

## Code Standards

- C#: follow existing patterns, nullable enabled, implicit usings
- TypeScript: strict mode, ESM
- No hardcoded secrets or credentials
- No debug code in commits

## Reporting Issues

Open an issue with:
- Steps to reproduce
- Expected vs actual behavior
- Environment details (OS, Docker version)
