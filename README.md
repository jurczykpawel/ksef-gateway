# KSeF Gateway

> Universal REST API gateway for Poland's National e-Invoice System (KSeF), wrapping the official Ministry of Finance C# SDK.

![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Beta-yellow)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Open Source](https://img.shields.io/badge/Open%20Source-100%25-brightgreen)

---

## Why KSeF Gateway?

- **One command** to run a full KSeF integration - `docker-compose up`
- **Auto-discovery** - 60+ endpoints generated automatically from the official SDK via reflection. SDK update = rebuild = new endpoints
- **Universal** - any system (Node.js, Python, PHP, Go) can integrate with KSeF through simple HTTP calls
- **Official SDK inside** - wraps [CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp), maintained by the Polish Ministry of Finance
- **Zero crypto complexity** - gateway handles AES-256 encryption, RSA key exchange, and token management transparently
- **No .NET required locally** - everything builds and runs inside Docker

---

## Features

### Auto-discovered SDK Endpoints
All 60+ methods from the official `IKSeFClient` interface are automatically exposed as REST endpoints, grouped by domain:

| Group | Endpoints | Description |
|-------|-----------|-------------|
| `online-session` | 3 | Interactive invoice sending (open, send, close) |
| `batch-session` | 4 | Batch invoice sending (ZIP packages) |
| `invoice-download` | 4 | Download invoices, query metadata, export |
| `session-status` | 9 | Session and invoice status, UPO retrieval |
| `authorization` | 5 | Auth challenge, token auth, access token |
| `ksef-token` | 4 | Generate, query, revoke KSeF tokens |
| `certificate` | 7 | KSeF certificate management |
| `permissions` | 17 | Grant, revoke, search permissions |
| `lighthouse` | 2 | KSeF system status |

### PDF Generation
Built-in PDF generation from invoice XML using the official [CIRFMF/ksef-pdf-generator](https://github.com/CIRFMF/ksef-pdf-generator).

### Token Management
Automatic KSeF token authentication with background refresh. Configure once, gateway handles the rest.

### API Documentation
Interactive API docs via [Scalar](https://scalar.com/) at `/scalar/v1`.

---

## Quick Start

### Prerequisites
- Docker & Docker Compose
- KSeF token ([how to generate](https://github.com/CIRFMF/ksef-docs/blob/main/tokeny-ksef.md))
- GitHub PAT with `read:packages` scope (for CIRFMF SDK access)

### Run

```bash
git clone https://github.com/jurczykpawel/ksef-gateway.git
cd ksef-gateway
cp .env.example .env
# Edit .env: set KSEF_TOKEN, KSEF_NIP, GITHUB_PAT
docker-compose up --build
```

API available at `http://localhost:8080`
Docs at `http://localhost:8080/scalar/v1`

### Verify

```bash
# Health check
curl http://localhost:8080/health

# Gateway + KSeF system status
curl http://localhost:8080/ksef/status

# List all available endpoints
curl http://localhost:8080/openapi/v1.json | jq '.paths | keys'
```

---

## Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KSEF_TOKEN` | Yes | - | KSeF authentication token |
| `KSEF_NIP` | Yes | - | NIP for authentication context |
| `KSEF_ENV` | No | `TEST` | Environment: `TEST`, `DEMO`, `PRODUCTION` |
| `KSEF_API_PORT` | No | `8080` | Gateway API port |
| `GITHUB_PAT` | Build | - | GitHub PAT with `read:packages` for CIRFMF SDK |

---

## Architecture

```
docker-compose up
       |
  ksef-api:8080          ksef-pdf:3000
  (ASP.NET 8)            (Node.js)
       |                      |
  CIRFMF C# SDK         CIRFMF ksef-pdf-generator
       |
  KSeF API (MF)
  TEST / DEMO / PROD
```

Two containers, no database, no Redis. Auth state lives in memory (restart = re-auth in seconds).

### Key Components

| Component | Role |
|-----------|------|
| **SdkReflector** | Discovers SDK interfaces via .NET reflection at startup |
| **EndpointMapper** | Registers each discovered method as `POST /ksef/{group}/{method}` |
| **TokenManager** | Background service: KSeF token auth + auto-refresh |
| **ErrorHandling** | Maps `KsefApiException` / `KsefRateLimitException` to proper HTTP responses |

### Design Principles
- **Thin wrapper** - zero business logic, just HTTP-to-SDK translation
- **Auto-adaptive** - SDK changes propagate automatically on rebuild
- **Transparent crypto** - callers send plaintext, gateway encrypts
- **Resilient** - auth failures don't crash the gateway, retry automatically

---

## API Usage

All SDK endpoints follow the pattern:

```
POST /ksef/{group}/{method}
Content-Type: application/json

{ ...request body matching SDK method parameters... }
```

### Example: Check KSeF System Status

```bash
curl http://localhost:8080/ksef/status
```

### Example: Send an Invoice (via raw SDK endpoint)

```bash
# 1. Open session
curl -X POST http://localhost:8080/ksef/online-session/open-online-session \
  -H "Content-Type: application/json" \
  -d '{ "requestPayload": { ... } }'

# 2. Send invoice
curl -X POST http://localhost:8080/ksef/online-session/send-online-session-invoice \
  -H "Content-Type: application/json" \
  -d '{ "requestPayload": { ... }, "sessionReferenceNumber": "..." }'

# 3. Close session
curl -X POST http://localhost:8080/ksef/online-session/close-online-session \
  -H "Content-Type: application/json" \
  -d '{ "sessionReferenceNumber": "..." }'
```

---

## Tech Stack

| Technology | Role |
|------------|------|
| [ASP.NET 8 Minimal API](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) | Gateway HTTP layer |
| [CIRFMF KSeF.Client](https://github.com/CIRFMF/ksef-client-csharp) | Official KSeF SDK (Ministry of Finance) |
| [CIRFMF ksef-pdf-generator](https://github.com/CIRFMF/ksef-pdf-generator) | Official PDF generation from FA(3) XML |
| [Scalar](https://scalar.com/) | API documentation UI |
| [Docker Compose](https://docs.docker.com/compose/) | Orchestration |
| Node.js | PDF service runtime |

---

## Roadmap

- [x] Auto-discovery of SDK endpoints via reflection
- [x] Token-based authentication (KSeF token)
- [x] Background token refresh
- [x] Scalar API documentation
- [x] Docker one-command setup
- [x] PDF generation service (scaffolded)
- [ ] Convenience endpoints (`POST /ksef/send` - one-call invoice sending)
- [ ] Transparent invoice encryption in EndpointMapper
- [ ] Client-side rate limiting (respect KSeF limits proactively)
- [ ] PDF generator integration with CIRFMF library
- [ ] AWS Lambda deployment support
- [ ] Multi-NIP / multi-tenant mode
- [ ] XAdES / certificate authentication

---

## Contributing

Contributions are welcome! Whether it's bug reports, feature requests, or pull requests.

1. Fork the repository
2. Create your branch: `git checkout -b feat/your-feature`
3. Commit changes: `git commit -m "Add your feature"`
4. Push: `git push origin feat/your-feature`
5. Open a Pull Request

---

## License

MIT - see [LICENSE](LICENSE)

---

## Acknowledgments

- **[CIRFMF](https://github.com/CIRFMF)** (Centrum Informatyki Resortu Finansów) - official KSeF SDK and documentation
- **[KSeF Documentation](https://github.com/CIRFMF/ksef-docs)** - integration guide for KSeF 2.0
- **[Scalar](https://scalar.com/)** - API documentation UI
