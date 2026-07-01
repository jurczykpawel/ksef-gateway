# Changelog

## [Unreleased]

### Added

- Multi-NIP support - `TokenPool`/`ContextProvider` manage one auth context per NIP, `X-KSeF-NIP` header selects which one a request uses
- `POST /ksef/invoice` - friendly JSON invoice input (`{seller, buyer, items}`) with automatic VAT calculation and XML generation, no XML knowledge required
- Certificate-based authentication (X.509 + XAdES signing) as an alternative to `KSEF_TOKEN` - as a file path or as PEM content (for platforms without file mounts, e.g. AWS Lambda/Azure Container Apps)
- `GET /ksef/invoices/received` and `/received/new` - browse or poll for invoices received as buyer, without needing the KSeF number upfront
- `GET /ksef/invoices/issued` - the seller-role mirror, browse invoices you sent
- Gateway API-key authentication (`GATEWAY_API_KEY` / `X-Api-Key` header) - fails closed, protects every endpoint except `GET /health`
- Client-side rate limiting and circuit-breaker handling with proper `429`/`503`/`502` responses (`Retry-After`) instead of a flat `500`
- Deployment templates for Render (one-click), AWS Lambda, and Azure Container Apps
- `tools/CertGenerator` and `tools/TokenGenerator` - one-command TEST credential generators
- Bruno API client collection, plus CI integration tests running against a live gateway
- Landing page (Astro, Cloudflare Pages)
- Ready-to-import n8n workflow examples (Sellf, WooCommerce, receive-invoices) in English and Polish
- Dependabot with auto-merge for patch updates
- TruffleHog secret scanning (CI and pre-commit)

### Changed

- License: MIT → AGPL-3.0

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
