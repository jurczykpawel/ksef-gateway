# KSeF Gateway

> Universal REST API gateway for Poland's National e-Invoice System (KSeF). Send invoices, get PDFs with QR codes - one HTTP call.

![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Beta-yellow)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![Open Source](https://img.shields.io/badge/Open%20Source-100%25-brightgreen)

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/jurczykpawel/ksef-gateway)

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
- GitHub PAT with `read:packages` scope ([create here](https://github.com/settings/tokens/new?scopes=read:packages))

> **Why is a GitHub PAT needed?** The official KSeF SDK ([CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp)) is published as NuGet packages on GitHub Packages, not on nuget.org. GitHub Packages requires authentication even for packages from public repositories - this is a [known GitHub limitation](https://github.com/orgs/community/discussions/26634). A PAT with `read:packages` scope is the only way to download them during build. It takes 30 seconds to generate one.

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

## Testing with Bruno

A [Bruno](https://www.usebruno.com/) collection is included in the `bruno/` directory - all endpoints with assertions.

**Setup:**
1. Install Bruno (desktop app or CLI: `npm install -g @usebruno/cli`)
2. Open collection in Bruno desktop: **Open Collection** → select `bruno/`
3. Select environment `local`
4. Set `sellerNip` to your NIP

**Run with CLI:**
```bash
# Health/status (no token required)
cd bruno && bru run health.bru status.bru contexts.bru --env local

# Full collection (requires KSEF_TOKEN + KSEF_NIP in .env)
bru run --env local
```

`send-xml.bru` and `send-invoice.bru` automatically save the returned `ksefNumber` as a variable - after sending, `get-invoice-xml` and `get-invoice-pdf` work immediately.

---

## API Endpoints

### Workflow Endpoints (high-level)

| Method | Endpoint | Input | Output |
|--------|----------|-------|--------|
| `POST` | `/ksef/invoice` | Friendly JSON `{seller, buyer, items}` | KSeF number |
| `POST` | `/ksef/send` | FA(3) XML body | KSeF number |
| `POST` | `/ksef/send/json` | JSON (xml-js format, 1:1 with XML) | KSeF number |
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

## Integration with E-Commerce (Sellf, WooCommerce, etc.)

KSeF Gateway is a standalone service - deploy it separately and call its API from your e-commerce platform. No plugins, no SDK, just HTTP.

### Flow

```
Customer pays → Platform webhook → (optional: n8n transform) → POST /ksef/invoice → KSeF number
```

### Option 1: Direct integration

Add a `POST /ksef/invoice` call in your payment success handler:

```bash
curl -X POST https://your-ksef-gateway.onrender.com/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
    "invoiceNumber": "FV/2026/001",
    "issueDate": "2026-03-26",
    "saleDate": "2026-03-26",
    "seller": { "nip": "YOUR_NIP", "name": "Your Company", "address": { "street": "...", "city": "..." } },
    "buyer": { "nip": "BUYER_NIP", "name": "Customer", "address": { "street": "...", "city": "..." } },
    "items": [{ "name": "Product name", "quantity": 1, "unitPrice": 100, "vatRate": 23 }],
    "payment": { "paid": true, "date": "2026-03-26", "method": "transfer" }
  }'
```

### Option 2: n8n as middleware (no-code, recommended)

Most platforms send webhooks in their own format, not KSeF format. Use [n8n](https://n8n.io/) to translate:

1. **Webhook node** - receives payment webhook from your platform (Sellf, WooCommerce, Stripe, etc.)
2. **Transform node** - maps platform's JSON to KSeF invoice format (seller, buyer, items)
3. **HTTP Request node** - sends `POST /ksef/invoice` to your gateway

Zero code changes in your e-commerce platform. Configure the webhook URL in your platform's settings and n8n handles the rest.

Ready-to-import workflow: [`examples/n8n/woocommerce-ksef.json`](examples/n8n/) - WooCommerce → KSeF with NIP detection, VAT rate mapping, and consumer order skipping.

### Deploy the gateway

You need a running KSeF Gateway instance. Pick one:

- **[Deploy to Render](https://render.com/deploy?repo=https://github.com/jurczykpawel/ksef-gateway)** (one click, free tier)
- **StackPilot**: `./local/deploy.sh ksef-gateway --ssh=vps` ([github.com/jurczykpawel/stackpilot](https://github.com/jurczykpawel/stackpilot))
- **Docker Compose**: `docker compose up` (self-hosted)

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

### Production Token (step by step)

For production you need a **qualified e-signature** (podpis kwalifikowany) - the same one used for JPK, e-PIT, etc. Common providers: SimplySign (Asseco), Certum, Szafir (KIR), e-dowod.

> **Companies (sp. z o.o., SA, fundacje):** Before first login, submit the **ZAW-FA** form to your tax office. Sole proprietors (JDG) do not need this.

**Via Aplikacja Podatnika KSeF 2.0:**

1. Go to [ap.ksef.mf.gov.pl](https://ap.ksef.mf.gov.pl/)
2. Click **Zaloguj** and authenticate with one of:
   - Podpis kwalifikowany (SimplySign, Certum, Szafir)
   - Profil Zaufany (ePUAP / mObywatel / e-banking)
   - e-Dowod (electronic ID card with NFC)
3. Enter your company **NIP** and click **Uwierzytelnij**
4. Review and sign the authentication request
5. Navigate to the **Tokeny** tab
6. Click **Generuj token**
7. Enter a descriptive **name** for the token (e.g. "ksef-gateway API")
8. Select permissions:
   - **Wystawianie faktur** (InvoiceWrite) - sending invoices
   - **Odczyt faktur** (InvoiceRead) - downloading invoices
9. Confirm with your e-signature
10. **Copy the token immediately** - it is displayed only once
11. Set in your `.env`:
    ```
    KSEF_TOKEN=<the token you just copied>
    KSEF_NIP=<your company NIP>
    KSEF_ENV=PRODUCTION
    ```

You only need to do this once. If you lose the token, revoke it in the portal and generate a new one.

> **Who can generate a token?** Only a person authorized to represent the company (owner, board member, or someone with a KSeF authorization granted by them).

> **Token expiration:** All KSeF tokens expire **December 31, 2026**. From January 1, 2027, only KSeF certificates will be accepted. Tokens are a transitional authentication method.

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
| `KSEF_CONTEXTS_FILE` | No | `/app/contexts.json` | Path to multi-NIP config file |

### Multi-NIP Mode

To handle invoices for multiple companies, create a `contexts.json` file:

```json
[
  {
    "nip": "1234567890",
    "token": "ksef-token-for-company-A",
    "label": "Company A"
  },
  {
    "nip": "0987654321",
    "token": "ksef-token-for-company-B",
    "label": "Company B"
  }
]
```

Mount it in Docker Compose (already configured in `docker-compose.yml`):

```yaml
volumes:
  - ./contexts.json:/app/contexts.json:ro
```

The gateway auto-detects which NIP to use based on:
1. `X-KSeF-NIP` header (explicit)
2. Seller NIP from the invoice body
3. Default context (first in list or from `KSEF_NIP` env var)

Check authenticated contexts: `GET /ksef/contexts`

> **Note:** `KSEF_TOKEN` + `KSEF_NIP` env vars still work for single-NIP mode. If both env vars and `contexts.json` are present, the env var context is added to the list.

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

## Cloud Deployment

### Render (one-click)

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/jurczykpawel/ksef-gateway)

Click the button, set three env vars (`GITHUB_PAT`, `KSEF_TOKEN`, `KSEF_NIP`), done. Both services (API + PDF) deploy automatically from `render.yaml`.

### AWS Lambda

Deploy as a serverless Lambda function with Function URL (no API Gateway - avoids 29s timeout).

```bash
cd deploy/aws
sam build --build-arg GITHUB_PAT=<your-pat>
sam deploy --guided
```

See [`deploy/aws/README.md`](deploy/aws/README.md) for details.

### Azure Container Apps

Deploy as managed containers - mirrors Docker Compose, zero code changes.

```bash
az deployment group create \
  --resource-group ksef-gateway \
  --template-file deploy/azure/main.bicep \
  --parameters ksefToken=<token> ksefNip=<nip>
```

See [`deploy/azure/README.md`](deploy/azure/README.md) for details.

| | Docker Compose | Render | AWS Lambda | Azure Container Apps |
|---|---|---|---|---|
| Setup | `docker compose up` | One-click button | SAM CLI | Azure CLI + Bicep |
| Cold start | None | ~30s (free tier) | ~3-5s | ~5-10s (or 0 with minReplicas=1) |
| Cost (low traffic) | Server cost | Free tier available | Near-zero | ~$10-15/month |
| PDF service | Included | Included | Separate deployment | Included (internal container) |
| Multi-NIP | `contexts.json` mount | Env vars | Env vars (single NIP) | Env vars or Azure Files |

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
- [x] Client-side rate limiting (proactive, per official MF limits)
- [x] `POST /ksef/invoice` - friendly JSON with auto VAT calculation
- [x] 81 unit/integration tests (SdkReflector, RateLimiter, InvoiceXmlBuilder, XSD validation, KSeF E2E)
- [x] GitHub Actions CI + TruffleHog secret scanning
- [x] Multi-NIP / multi-tenant mode
- [x] Bruno collection for manual and automated testing
- [x] AWS Lambda deployment support
- [x] Azure Container Apps deployment support

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
