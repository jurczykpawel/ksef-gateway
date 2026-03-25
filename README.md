# KSeF Gateway

> Universal REST API gateway for Poland's National e-Invoice System (KSeF). Send invoices, get PDFs with QR codes - one HTTP call.

![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Beta-yellow)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![Open Source](https://img.shields.io/badge/Open%20Source-100%25-brightgreen)

---

## Why KSeF Gateway?

- **One HTTP call** to send an invoice to KSeF - gateway handles encryption, sessions, polling
- **JSON or XML** - send invoices as JSON (auto-converted) or raw FA(3) XML
- **PDF with QR** - get a verified PDF by KSeF number, one call
- **60+ auto-discovered endpoints** from the official SDK via .NET reflection
- **Official SDK inside** - wraps [CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp), maintained by the Polish Ministry of Finance
- **Zero crypto complexity** - AES-256 encryption, RSA key exchange, token management handled transparently
- **No .NET required locally** - everything builds and runs inside Docker

---

## Quick Start

### Prerequisites
- Docker & Docker Compose
- GitHub PAT with `read:packages` scope ([create here](https://github.com/settings/tokens/new?scopes=read:packages) - needed for CIRFMF SDK from GitHub Packages)

### 1. Clone and configure

```bash
git clone --recurse-submodules https://github.com/jurczykpawel/ksef-gateway.git
cd ksef-gateway
cp .env.example .env
# Edit .env: set GITHUB_PAT
```

### 2. Generate a KSeF test token

```bash
docker compose --profile tools run --rm token-generator
```

Copy the output (`KSEF_TOKEN`, `KSEF_NIP`, `KSEF_ENV`) into your `.env` file.

> **What does this do?** See [How Token Generator Works](#how-token-generator-works) below.

### 3. Run the gateway

```bash
docker compose up --build
```

API: `http://localhost:8080` | Docs: `http://localhost:8080/scalar/v1`

### 4. Send your first invoice

```bash
# Send FA(3) XML invoice
curl -X POST http://localhost:8080/ksef/send \
  -H "Content-Type: application/xml" \
  -d @invoice.xml

# Get PDF with QR code
curl -o faktura.pdf http://localhost:8080/ksef/invoice/{ksefNumber}/pdf
```

---

## API Endpoints

### Workflow Endpoints (high-level)

| Method | Endpoint | Input | Output |
|--------|----------|-------|--------|
| `POST` | `/ksef/send` | FA(3) XML body | KSeF number |
| `POST` | `/ksef/send/json` | JSON (xml-js format) | KSeF number |
| `GET` | `/ksef/invoice/{ksefNumber}` | - | Invoice XML |
| `GET` | `/ksef/invoice/{ksefNumber}/pdf` | - | PDF with QR code |
| `GET` | `/ksef/status` | - | Gateway + KSeF status |
| `GET` | `/health` | - | Health check |

### Auto-discovered SDK Endpoints (low-level)

All 60+ methods from the official `IKSeFClient` are exposed as `POST /ksef/{group}/{method}`:

| Group | Endpoints | Description |
|-------|-----------|-------------|
| `online-session` | 3 | Interactive invoice sending |
| `batch-session` | 4 | Batch sending (ZIP packages) |
| `invoice-download` | 4 | Download, query, export invoices |
| `session-status` | 9 | Session/invoice status, UPO |
| `authorization` | 5 | Auth challenge, token auth |
| `ksef-token` | 4 | Generate, query, revoke tokens |
| `certificate` | 7 | KSeF certificate management |
| `permissions` | 17 | Grant, revoke, search permissions |
| `lighthouse` | 2 | KSeF system status |

Full interactive docs at `/scalar/v1`.

---

## Sending Invoices

### Option 1: Raw XML

```bash
curl -X POST http://localhost:8080/ksef/send \
  -H "Content-Type: application/xml" \
  -d '<?xml version="1.0" encoding="utf-8"?>
<Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
  <Naglowek>
    <KodFormularza kodSystemowy="FA (3)" wersjaSchemy="1-0E">FA</KodFormularza>
    <WariantFormularza>3</WariantFormularza>
    <DataWytworzeniaFa>2026-03-25T12:00:00Z</DataWytworzeniaFa>
    <SystemInfo>my-system</SystemInfo>
  </Naglowek>
  <!-- ... full FA(3) XML ... -->
</Faktura>'
```

### Option 2: JSON (zero-maintenance format)

JSON structure mirrors XML 1:1 using [xml-js compact format](https://www.npmjs.com/package/xml-js#compact-notation). No manual mapping - when FA(3) XSD changes, JSON structure changes automatically.

```bash
curl -X POST http://localhost:8080/ksef/send/json \
  -H "Content-Type: application/json" \
  -d '{
  "Faktura": {
    "Naglowek": {
      "KodFormularza": {
        "_attributes": {"kodSystemowy": "FA (3)", "wersjaSchemy": "1-0E"},
        "_text": "FA"
      },
      "WariantFormularza": {"_text": "3"},
      "DataWytworzeniaFa": {"_text": "2026-03-25T12:00:00Z"},
      "SystemInfo": {"_text": "my-system"}
    },
    "Podmiot1": {
      "DaneIdentyfikacyjne": {
        "NIP": {"_text": "1234567890"},
        "Nazwa": {"_text": "Seller sp. z o.o."}
      },
      "Adres": {
        "KodKraju": {"_text": "PL"},
        "AdresL1": {"_text": "ul. Testowa 1"},
        "AdresL2": {"_text": "00-001 Warszawa"}
      }
    },
    "Podmiot2": {
      "DaneIdentyfikacyjne": {
        "NIP": {"_text": "0987654321"},
        "Nazwa": {"_text": "Buyer sp. z o.o."}
      },
      "Adres": {
        "KodKraju": {"_text": "PL"},
        "AdresL1": {"_text": "ul. Kupiecka 2"},
        "AdresL2": {"_text": "00-002 Warszawa"}
      },
      "JST": {"_text": "2"},
      "GV": {"_text": "2"}
    },
    "Fa": {
      "KodWaluty": {"_text": "PLN"},
      "P_1": {"_text": "2026-03-24"},
      "P_2": {"_text": "FV/001/03/2026"},
      "P_6": {"_text": "2026-03-24"},
      "P_13_1": {"_text": "100.00"},
      "P_14_1": {"_text": "23.00"},
      "P_15": {"_text": "123.00"},
      "Adnotacje": {
        "P_16": {"_text": "2"}, "P_17": {"_text": "2"},
        "P_18": {"_text": "2"}, "P_18A": {"_text": "2"},
        "Zwolnienie": {"P_19N": {"_text": "1"}},
        "NoweSrodkiTransportu": {"P_22N": {"_text": "1"}},
        "P_23": {"_text": "2"},
        "PMarzy": {"P_PMarzyN": {"_text": "1"}}
      },
      "RodzajFaktury": {"_text": "VAT"},
      "FaWiersz": {
        "NrWierszaFa": {"_text": "1"},
        "P_7": {"_text": "Service description"},
        "P_8A": {"_text": "szt."},
        "P_8B": {"_text": "1"},
        "P_9A": {"_text": "100.00"},
        "P_11": {"_text": "100.00"},
        "P_12": {"_text": "23"}
      },
      "Platnosc": {
        "Zaplacono": {"_text": "1"},
        "DataZaplaty": {"_text": "2026-03-24"},
        "FormaPlatnosci": {"_text": "6"}
      }
    }
  }
}'
```

### Response

```json
{
  "success": true,
  "data": {
    "ksefNumber": "1234567890-20260325-5EC118800000-05",
    "status": "accepted",
    "statusDescription": "Sukces",
    "sessionReferenceNumber": "20260325-SO-...",
    "invoiceReferenceNumber": "20260325-EE-..."
  }
}
```

---

## PDF Generation

```bash
# Get PDF with QR code by KSeF number (one call - downloads XML from KSeF internally)
curl -o faktura.pdf http://localhost:8080/ksef/invoice/{ksefNumber}/pdf

# Generate PDF from XML directly
curl -X POST http://localhost:8080/pdf/invoice \
  -H "Content-Type: application/xml" \
  -d @invoice.xml -o faktura.pdf

# Generate PDF with QR code
curl -X POST "http://localhost:8080/pdf/invoice?nrKSeF={ksefNumber}" \
  -H "Content-Type: application/xml" \
  -d @invoice.xml -o faktura.pdf
```

PDFs are generated using the official [CIRFMF/ksef-pdf-generator](https://github.com/CIRFMF/ksef-pdf-generator) library. QR codes contain the KSeF verification URL with SHA-256 hash - scannable and verified by KSeF.

---

## How Token Generator Works

The token generator automates what normally requires a qualified e-signature. It works **only on the TEST environment** using a self-signed certificate.

```
Step 1: Generate random NIP with valid checksum
Step 2: POST /auth/challenge → get one-time challenge from KSeF
Step 3: Create self-signed X.509 certificate (accepted on TEST only)
Step 4: Build AuthTokenRequest XML, sign with XAdES
Step 5: POST /auth/xades-signature → submit signed auth request
Step 6: Poll GET /auth/{ref} until status = 200 (auth complete)
Step 7: POST /auth/token/redeem → get accessToken + refreshToken
Step 8: POST /tokens → generate KSeF token with InvoiceRead + InvoiceWrite
Step 9: Poll until token status = Active
```

Output: `KSEF_TOKEN`, `KSEF_NIP`, `KSEF_ENV` - paste into `.env`.

The token lives until revoked. The gateway uses it daily: encrypts it with KSeF's public key, gets a JWT, auto-refreshes before expiry.

### Test vs Production

| Step | TEST | PRODUCTION |
|------|------|------------|
| Certificate | Self-signed (automatic) | Qualified e-signature (SimplySign, Certum, etc.) |
| NIP | Random, any value | Real, registered with tax office |
| Token generation | Same flow | Same flow |
| Who does it | Script (one command) | Business owner (one time) |

---

## Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KSEF_TOKEN` | Yes | - | KSeF authentication token |
| `KSEF_NIP` | Yes | - | NIP for authentication context |
| `KSEF_ENV` | No | `TEST` | Environment: `TEST`, `DEMO`, `PRODUCTION` |
| `KSEF_API_PORT` | No | `8080` | Gateway API port |
| `KSEF_QR_URL` | No | `https://qr-test.ksef.mf.gov.pl` | QR verification base URL |
| `GITHUB_PAT` | Build | - | GitHub PAT with `read:packages` for CIRFMF SDK |

---

## Architecture

```
docker compose up
       |
  ksef-api:8080            ksef-pdf:3000
  (ASP.NET 9)              (Node.js + tsx)
       |                        |
  CIRFMF C# SDK           CIRFMF ksef-pdf-generator
  (NuGet, GitHub Pkgs)     (git submodule)
       |
  KSeF API (MF)
  TEST / DEMO / PROD
```

Two containers, no database, no Redis. Auth state in memory (restart = re-auth in seconds).

### Key Components

| Component | Role |
|-----------|------|
| **SdkReflector** | Discovers SDK interfaces via .NET reflection at startup |
| **EndpointMapper** | Registers each method as `POST /ksef/{group}/{method}` |
| **TokenManager** | Background service: KSeF token auth + auto-refresh |
| **WorkflowEndpoints** | High-level: `/ksef/send`, `/ksef/send/json`, `/ksef/invoice/{nr}/pdf` |
| **PDF Service** | XML to PDF via CIRFMF library + QR code generation |
| **JSON-to-XML** | `js2xml()` - zero-maintenance JSON/XML conversion |

### Design Principles
- **Thin wrapper** - zero business logic, HTTP-to-SDK translation
- **Auto-adaptive** - SDK changes propagate on rebuild (reflection)
- **Transparent crypto** - callers send plaintext, gateway encrypts
- **Zero-maintenance JSON** - mirrors XML 1:1, no mapping code
- **Resilient** - auth failures don't crash, retry automatically

---

## Tech Stack

| Technology | Role |
|------------|------|
| [ASP.NET 9 Minimal API](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) | Gateway HTTP layer |
| [CIRFMF KSeF.Client](https://github.com/CIRFMF/ksef-client-csharp) | Official KSeF SDK (Ministry of Finance) |
| [CIRFMF ksef-pdf-generator](https://github.com/CIRFMF/ksef-pdf-generator) | Official PDF generation from FA(3) XML |
| [xml-js](https://www.npmjs.com/package/xml-js) | JSON/XML bidirectional conversion |
| [pdfmake](http://pdfmake.org/) | PDF rendering with native QR support |
| [Scalar](https://scalar.com/) | API documentation UI |
| [Docker Compose](https://docs.docker.com/compose/) | Orchestration |

---

## Roadmap

- [x] Auto-discovery of 60+ SDK endpoints via reflection
- [x] Token-based authentication with background refresh
- [x] `POST /ksef/send` - one-call invoice sending (XML)
- [x] `POST /ksef/send/json` - JSON invoice sending (zero-maintenance format)
- [x] `GET /ksef/invoice/{nr}/pdf` - PDF with verified QR code
- [x] PDF generation with official CIRFMF library
- [x] Token generator for TEST environment
- [x] Scalar API documentation
- [x] Docker one-command setup
- [ ] Client-side rate limiting (respect KSeF limits proactively)
- [ ] JSON Schema auto-generated from XSD (validation + docs)
- [ ] AWS Lambda deployment support
- [ ] Multi-NIP / multi-tenant mode
- [ ] Friendly JSON format (`{seller, buyer, items}`) with auto-mapping

---

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

---

## License

MIT - see [LICENSE](LICENSE)

---

## Acknowledgments

- **[CIRFMF](https://github.com/CIRFMF)** (Centrum Informatyki Resortu Finansów) - official KSeF SDK, PDF generator, and documentation
- **[KSeF Documentation](https://github.com/CIRFMF/ksef-docs)** - integration guide for KSeF 2.0
- **[Scalar](https://scalar.com/)** - API documentation UI
