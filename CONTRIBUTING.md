# Contributing to KSeF Gateway

Thanks for your interest in contributing!

## Development Setup

### Prerequisites

- Docker & Docker Compose
- GitHub PAT with `read:packages` (for CIRFMF SDK)
- Optionally: .NET 9 SDK (for local development without Docker)

### Running locally

```bash
cp .env.example .env
# Set GITHUB_PAT in .env
docker-compose up --build
```

API at `http://localhost:8080`, docs at `http://localhost:8080/scalar/v1`.

### Running tests

```bash
# .NET unit tests (no local SDK required)
docker compose run --rm ksef-api-tests

# PDF service
cd pdf-service && npm test
```

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
