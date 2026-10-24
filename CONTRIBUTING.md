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
