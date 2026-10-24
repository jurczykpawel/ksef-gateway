# Changelog

## [Unreleased]

### Added

- Regression suite: host contracts and route coverage matrix, quota-aware shared TEST fixture, nightly CI, published-image smoke gate and local `test-full.sh --all`.
- Failing dependency audit gates; pin patched CMS/XML signature packages to 10.0.13 in tests and tools.

### Security

- Add `Cache-Control: no-store` and `X-Content-Type-Options: nosniff` to API and PDF service responses, including errors; exempt health and API documentation.
- Validate JSON-to-XML names and compact metadata, escape attribute values, and cap JSON depth at 100 and values at 100,000 before serialization.
- Add optional SHA-256 client API keys scoped to NIPs, filtered diagnostics and safe SDK routes; preserve the administrator key and fail-closed configuration.
- Pin container base digests and Azure release tags; expand Dependabot coverage to Docker, submodules, tests and tools with authenticated NuGet access.
- Gate image publication on CI for the same ref, attach SPDX SBOMs and sign image digests with keyless cosign.
- Pin all external GitHub Actions to verified commit SHAs.
- Add private vulnerability reporting, ownership templates and third-party license notices in API/PDF images.
- Return minimal public health status and generic server errors with logged correlation IDs.
- Dependency updates resolving 5 advisories in the PDF service.
- Dependency updates resolving 39 advisories across the PDF service and landing page.
- Harden commercial n8n templates with credential/environment secrets, strict raw-body HMAC verification, bounded deduplication and conservative send retries; document static-data durability limits.
- Fix licensing trust endpoints to Sellf HTTPS URLs; bound JWKS/CRL requests to 10 seconds and dispose responses.
- **BREAKING (v0.3.0):** expose only invoice sending/receiving SDK groups by default. Use `KSEF_SDK_ENDPOINTS` for explicit groups/methods or opt into `all` (including token, permission and certificate administration); hidden routes are absent from routing and OpenAPI.
- API and PDF containers run as non-root users; the API retains read access to Render Secret Files. PDF errors return concise JSON without internal messages or stack traces.

### Changed

- Upgrade the PDF generator to CIRFMF release 1.1.46, initialize bundled Polish translations and retain pdfmake 0.3 Promise rendering; verify invoice amounts, zero totals, pagination and environment QR links from real PDF content.
- Migrate the API, tests, tools, CI, and container deployments to .NET 10 LTS while retaining the OpenAPI 3.0 contract and KSeF SDK 2.9.0.
- Pin all NuGet PackageReference versions in the API, tests, and tools to their currently resolved versions, including KSeF SDK 2.9.0.
- PDF image installs production dependencies only (`tsx` remains a runtime dependency) and sets `NODE_ENV=production`.

### Added

- Support exempt (`zw`) and outside-territory (`np I`/`np II`) friendly invoice rates, statutory exemption basis, and `KOR` invoices with signed line differences and original-invoice references.
- Approve the complete reflected SDK 2.9.0 route/signature manifest in tests; SDK contract changes require explicit review and a changelog entry.

### Fixed

- Restore commercial n8n webhook secret fallback for default n8n 2.x/Cloud; prefer environment secrets and document Configuration export exposure.
- Fix InvoiceDemo compilation against KSeF SDK 2.9.0; compile all tools during Docker builds and gate them in CI.
- Align bilingual invoice examples and landing claims with API contracts; validate documentation DTOs and FA(3) fixtures in tests.
- Document deployment parameters, troubleshooting, digest-based rollback, secret rotation and backups.
- Bind chunked JSON bodies on SDK routes, return 400 for invalid JSON or missing required arguments, and limit whole-body binding to a single composite business parameter.
- Validate friendly invoice fields before KSeF calls; dispose PDF HTTP resources, propagate cancellation, bound upstream time to 60 seconds by default and limit concurrent PDF renders.
- Preserve full order IDs, convert Sellf gross to net, and include WooCommerce shipping/fees without premature unit-price rounding; add offline n8n tests to CI.
- Serialize per-NIP token authentication and refresh, publish immutable snapshots, refresh at 80% of actual lifetime, and preserve valid tokens on transient refresh failures without swallowing cancellation.
- Count each SDK HTTP attempt against per-NIP operation budgets (anonymous calls per IP), honor upstream Retry-After, update session-close/export limits, and exclude local diagnostics regardless of route casing.
- Apply current Multi-NIP license permissions at each context lookup; revoke and restore additional NIPs without restart and keep status counts current.
- Poll sent invoices by KSeF status code with a 60-second deadline; preserve rate-limit/cancellation errors, always attempt session cleanup, and pass the SDK public key selector.
- Use commercial rounding (midpoints away from zero) for invoice line net amounts and VAT summaries.
- Derive invoice gross totals from the same per-rate net and VAT summaries, fixing discrepancies for small line amounts.
- Emit schema-valid domestic 0% VAT fields (`P_13_6_1` and line `0 KR`, without `P_14_6`); document raw XML for WDT and exports.
- Restore clean landing installs by using TypeScript 6, compatible with `@astrojs/check`; build the landing in CI, including Dependabot PRs.
- Stabilize PDF service tests with per-test ephemeral IPv4 servers and restore environment variables without leaking the string `"undefined"`.
- Derive PDF invoice QR verification hosts from the shared KSeF environment (TEST/DEMO/PRODUCTION/PRD), preserving explicit `KSEF_QR_URL` overrides.
- Validate and normalize `KSEF_ENV` at startup (including production aliases `PRD`/`PROD`); reject unknown values instead of silently using TEST, and report the canonical environment in authenticated `/ksef/status`.
- Validate `X-KSeF-NIP` before rate limiting (400 for malformed/multiple values); reject unconfigured header or selected seller NIPs with 404 before allocating token state or logging their values.
- `POST /ksef/send/json` always returned 401: it called the gateway's own `http://localhost:8080/ksef/send` without `X-Api-Key` (and only worked on port 8080). It now uses the same in-process code path as `/ksef/send`. Regression test added (red on the old code: `Unauthorized`, green on the new)

## [0.2.1] - 2026-10-23

### Security

- Docker images: the build-time GitHub PAT (`read:packages`, used to restore the CIRFMF SDK from GitHub Packages) was passed as a build argument, so BuildKit recorded its value in the public provenance attestation of the published `ksef-gateway-api` images (up to v0.2.0 and `latest` until 2026-10-23). The token has been revoked and replaced. The Dockerfiles (API, Lambda, tools) now take it as a BuildKit **secret** (`--secret id=GITHUB_PAT,env=GITHUB_PAT`), never promote it to `ENV`, and CI/publish workflows pass it as a secret instead of a build argument. `--build-arg GITHUB_PAT=...` still works as a fallback for builders without secret support (Render, SAM). If you built or pushed your own images with the old Dockerfiles, rotate the PAT you used.

### Fixed

- `pdf-service/Dockerfile`: `npm install` without the lockfile crashed (`Cannot read properties of null (reading 'edgesOut')`), so the PDF image failed to build - on Render every PDF-service deploy since 2026-09-23 ended in `build_failed` while the old build kept serving. Now copies `package-lock.json` and runs `npm ci` (reproducible)
- `examples/n8n/receive-invoices*.json`: the checkpoint was never read back - `Get Checkpoint` and `Save Checkpoint` used the `node` static-data scope, which is private to each node, so every run re-fetched the last 30 days. Now uses the `global` scope
- `examples/n8n/receive-invoices*.json`: the checkpoint is committed only after the PDFs were downloaded, saved and notified (a failure midway used to skip the batch); timeouts/retries on the HTTP nodes; PDFs downloaded one at a time (the gateway rate-limits `invoices/get`); the gateway API key is a Header Auth credential instead of plain text in the workflow

### Changed

- Dependency updates merged since 0.2.0 (npm: pdf-service, landing)

### Added

- `examples/n8n/receive-invoices*.json`: wake loop for Render's free tier (`/health` of the gateway and the PDF service, 30 s retries), optional `pdfServiceUrl`; fails on purpose after 6 polls in a row so an Error Workflow can alert you
- Docs: license verification details (domain not checked) and that a `GATEWAY_LICENSE` change on Render needs a deploy

## [0.2.0] - 2026-07-01

### Added

- Multi-NIP support - `TokenPool`/`ContextProvider` manage one auth context per NIP, `X-KSeF-NIP` header selects which one a request uses; verified live with several distinct certificates (not just tokens or one shared certificate) authenticating independently for different NIPs
- Multi-NIP license gating - running more than one NIP requires a `GATEWAY_LICENSE` (one-time purchase, [Sellf](https://sellf.techskills.academy/p/ksef-gateway-multi-nip)), verified fully offline (ECDSA P-256, JWKS + k-anonymity revocation check); a single NIP stays free forever
- `POST /ksef/invoice` - friendly JSON invoice input (`{seller, buyer, items}`) with automatic VAT calculation and XML generation, no XML knowledge required
- Certificate-based authentication (X.509 + XAdES signing) as an alternative to `KSEF_TOKEN` - as a file path or as PEM content (for platforms without file mounts, e.g. AWS Lambda/Azure Container Apps)
- `GET /ksef/invoices/received` and `/received/new` - browse or poll for invoices received as buyer, without needing the KSeF number upfront
- `GET /ksef/invoices/issued` - the seller-role mirror, browse invoices you sent
- Gateway API-key authentication (`GATEWAY_API_KEY` / `X-Api-Key` header) - fails closed, protects every endpoint except `GET /health`
- Client-side rate limiting and circuit-breaker handling with proper `429`/`503`/`502` responses (`Retry-After`) instead of a flat `500`
- Deployment templates for Render (one-click), AWS Lambda, and Azure Container Apps
- Public live demo on Render - try the gateway with zero setup (README "Try the Live Demo", landing page CTA, `bruno/environments/demo.bru`)
- `tools/CertGenerator` and `tools/TokenGenerator` - one-command TEST credential generators
- Bruno API client collection, plus CI integration tests running against a live gateway
- Landing page (Astro, Cloudflare Pages)
- Ready-to-import n8n workflow examples (Sellf, WooCommerce, receive-invoices) in English and Polish
- `pdf-service` test coverage (0 → 34 tests) and its own CI job - previously never built or tested in CI
- Dependabot with auto-merge for patch updates
- TruffleHog secret scanning (CI and pre-commit)

### Changed

- License: MIT → AGPL-3.0
- CI integration tests mint a fresh, disposable KSeF test token per run instead of reusing a static secret - avoids accumulating rate-limit history across months of runs

### Fixed

- `ContextProvider` now warns when more than one KSeF auth method (token, certificate path, certificate content) is configured for the same NIP, instead of silently picking one with no indication anything was ambiguous

## [0.1.0] - 2026-03-25

### Added

- Auto-discovery of 60+ SDK endpoints via .NET reflection
- Token-based KSeF authentication with background refresh
- Scalar API documentation at `/scalar/v1`
- Health and status endpoints (`/health`, `/ksef/status`)
- PDF service sidecar (scaffold, CIRFMF ksef-pdf-generator integration pending)
- Docker Compose one-command setup
- Error handling middleware (KsefApiException, KsefRateLimitException)
- Support for TEST, DEMO, and PRODUCTION KSeF environments
