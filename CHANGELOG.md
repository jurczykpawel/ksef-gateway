# Changelog

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
