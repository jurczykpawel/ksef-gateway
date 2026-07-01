# KSeF Gateway

> REST API for Poland's e-Invoice System (KSeF). Send a JSON, get a KSeF number. Receive invoices without knowing their number. One HTTP call, both directions.

<details>
<summary>🇵🇱 Po polsku</summary>

**KSeF Gateway** to bramka REST API do Krajowego Systemu e-Faktur. Wysyłasz prosty JSON z danymi faktury, dostajesz numer KSeF. Odbierasz faktury wystawione na Ciebie bez znajomości ich numeru - jednym zapytaniem po dacie. Jedno wywołanie HTTP zamiast budowania XML, szyfrowania AES-256, zarządzania sesjami i tokenami.

```bash
curl -X POST https://twoj-gateway/ksef/invoice \
  -d '{"seller":{"nip":"..."},"buyer":{"nip":"..."},"items":[{"name":"Usługa","unitPrice":100,"vatRate":23}]}'
# → {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}
```

**Szybki start:** `docker compose up` i gotowe. Nie potrzebujesz .NET lokalnie.

**Cechy:** wysyłanie i odbieranie faktur, oficjalne SDK Ministerstwa Finansów (CIRFMF), PDF z QR, 60+ endpointów, multi-NIP, deploy jednym kliknięciem (Render/Lambda/Azure). Gotowe na obowiązkowy KSeF (produkcja, nie tylko test).

**Instrukcja generowania tokenu produkcyjnego:** [Production Token](#production-token-step-by-step)

</details>

![License](https://img.shields.io/badge/License-AGPL%20v3-blue)
![Status](https://img.shields.io/badge/Status-Beta-yellow)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![Open Source](https://img.shields.io/badge/Open%20Source-100%25-brightgreen)
[![CI](https://github.com/jurczykpawel/ksef-gateway/actions/workflows/ci.yml/badge.svg)](https://github.com/jurczykpawel/ksef-gateway/actions/workflows/ci.yml)

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/jurczykpawel/ksef-gateway)

---

## The Problem

Integrating with KSeF directly means:
- Building FA(3) XML from scratch (complex schema, Polish field names like P_13_1, P_14_1)
- AES-256 encryption of invoices with KSeF's RSA public key
- Session management (open, send, poll status, close)
- Token authentication with XAdES signatures and auto-refresh
- Handling rate limits, retries, and error codes from KSeF API

**KSeF Gateway handles all of this.** You send a simple JSON, get back a KSeF number.

### Without KSeF Gateway

```
Your app → build XML → encrypt AES-256 → exchange RSA keys → open session
→ send encrypted invoice → poll status → parse response → close session
→ handle errors, retries, token refresh...
```

### With KSeF Gateway

```bash
curl -X POST https://your-gateway/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{"seller":{"nip":"..."},"buyer":{"nip":"..."},"items":[{"name":"Service","unitPrice":100,"vatRate":23}]}'

# → {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}
```

---

## Why KSeF Gateway?

- **One HTTP call** - send JSON, get KSeF number. Gateway handles encryption, sessions, polling
- **Simple JSON input** - `{seller, buyer, items}` with auto VAT calculation. No XML knowledge needed
- **Receive without knowing the number** - KSeF has no email/webhook notifications; [browse or poll for invoices issued to you](#receiving-invoices) by date instead
- **PDF with QR** - download verified invoice PDF by KSeF number, one call
- **Official SDK inside** - wraps [CIRFMF/ksef-client-csharp](https://github.com/CIRFMF/ksef-client-csharp), maintained by the Polish Ministry of Finance
- **60+ auto-discovered endpoints** from the SDK via .NET reflection
- **Deploy anywhere** - Docker, Render (one click), AWS Lambda, Azure
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
curl -X POST http://localhost:8080/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
    "invoiceNumber": "FV/2026/001",
    "issueDate": "2026-03-26",
    "saleDate": "2026-03-26",
    "seller": {
      "nip": "YOUR_NIP",
      "name": "Your Company sp. z o.o.",
      "address": { "street": "ul. Testowa 1", "city": "00-001 Warszawa" }
    },
    "buyer": {
      "nip": "5265877635",
      "name": "Buyer sp. z o.o.",
      "address": { "street": "ul. Kupiecka 5", "city": "30-001 Krakow" }
    },
    "items": [
      { "name": "Consulting service", "quantity": 10, "unitPrice": 150, "vatRate": 23 }
    ],
    "payment": { "paid": true, "date": "2026-03-26", "method": "transfer" }
  }'

# Response: {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}

# Download PDF with QR code
curl -o faktura.pdf http://localhost:8080/ksef/invoice/{ksefNumber}/pdf
```

> **Also supports raw XML** (`POST /ksef/send`) and xml-js JSON format (`POST /ksef/send/json`) - see [Sending Invoices](#sending-invoices) below.

### 5. Find an invoice sent to you

On TEST/DEMO you can try this immediately without a second company: set `buyer.nip` to the **same** NIP you used as `seller.nip` above (KSeF supports self-invoicing), then look yourself up as the buyer:

```bash
curl "http://localhost:8080/ksef/invoices/received?from=2026-03-01&to=2026-04-01" \
  -H "X-KSeF-NIP: YOUR_NIP"

# Response: {"success":true,"data":{"invoices":[{"ksefNumber":"...","invoiceNumber":"FV/2026/001",...}],"hasMore":false}}
```

No need to already know the KSeF number - see [Receiving Invoices](#receiving-invoices) below for the full picture, including polling for new invoices.

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
| `GET` | `/ksef/invoices/received` | `?from=&to=&page=&pageSize=` | List of invoices you received (buyer role) |
| `GET` | `/ksef/invoices/received/new` | `?since=` | New invoices since a checkpoint, for polling/sync |
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

### Option 1: Friendly JSON (recommended)

Simple JSON with human-readable fields. Gateway builds FA(3) XML, calculates VAT totals, handles all KSeF fields automatically.

```bash
curl -X POST http://localhost:8080/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
    "invoiceNumber": "FV/2026/001",
    "issueDate": "2026-03-26",
    "saleDate": "2026-03-26",
    "seller": {
      "nip": "1234567890",
      "name": "Seller sp. z o.o.",
      "address": { "street": "ul. Testowa 1", "city": "00-001 Warszawa" }
    },
    "buyer": {
      "nip": "0987654321",
      "name": "Buyer sp. z o.o.",
      "address": { "street": "ul. Kupiecka 2", "city": "00-002 Warszawa" }
    },
    "items": [
      { "name": "Consulting service", "quantity": 10, "unitPrice": 150, "vatRate": 23 },
      { "name": "Server hosting", "quantity": 1, "unitPrice": 50, "vatRate": 23 }
    ],
    "payment": { "paid": true, "date": "2026-03-26", "method": "transfer" }
  }'
```

### Option 2: Raw FA(3) XML

For systems that already generate KSeF XML (accounting software, ERP):

```bash
curl -X POST http://localhost:8080/ksef/send \
  -H "Content-Type: application/xml" \
  -d @invoice.xml
```

### Option 3: JSON mirroring XML (xml-js format)

1:1 JSON representation of FA(3) XML using [xml-js compact format](https://www.npmjs.com/package/xml-js#compact-notation). Zero-maintenance - when XSD changes, JSON structure changes automatically. See [`examples/invoice.json`](examples/invoice.json) for full example.

```bash
curl -X POST http://localhost:8080/ksef/send/json \
  -H "Content-Type: application/json" \
  -d @examples/invoice.json
```

### Response (all options)

```json
{
  "success": true,
  "data": {
    "ksefNumber": "1234567890-20260326-5EC118800000-05",
    "status": "accepted",
    "statusDescription": "Sukces"
  }
}
```

### Error responses

Every endpoint returns the same shape on failure: `{"success": false, "data": null, "error": "..."}`. The HTTP status code tells you whether to retry and how:

| Status | Meaning | What to do |
|--------|---------|------------|
| `400` | Bad input (missing NIP, invalid XML, malformed body) | Fix the request, don't retry as-is |
| `429` | You're about to hit (or hit) a KSeF rate limit | Wait the `Retry-After` header (seconds), then retry |
| `502` | KSeF's own API rejected or failed the request | Check `error` for the KSeF error code/message; not always safe to retry blindly |
| `503` | Either KSeF's SDK circuit breaker is open (has `Retry-After` header - wait then retry), or the gateway isn't authenticated for that NIP yet (`TokenPool` still starting up - no `Retry-After`, retry shortly) | See `error` message to tell which one |
| `500` | Unexpected error in the gateway itself | Check gateway logs; please open an issue |

`429` and circuit-breaker `503` responses include a `Retry-After` header (seconds) - respect it instead of retrying immediately, especially for the [rate-limited receiving endpoints](#receiving-invoices).

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

## Receiving Invoices

KSeF has no email/webhook notifications - invoices issued to you just sit in the system. These endpoints let you find them without already knowing their KSeF number.

Both endpoints search for invoices where **your NIP is the buyer** (`Podmiot2`), matching however KSeF resolves the caller's context - see [Multi-NIP Mode](#multi-nip-mode) if you run the gateway for more than one company: pass `X-KSeF-NIP` to pick which NIP's inbox to search. Requires a token with the `InvoiceRead` permission - see the note in [Production Token](#production-token-step-by-step).

### Browse what you've received

```bash
curl "http://localhost:8080/ksef/invoices/received?from=2026-06-01&to=2026-07-01" \
  -H "X-KSeF-NIP: YOUR_NIP"
```

```json
{
  "success": true,
  "data": {
    "invoices": [
      {
        "ksefNumber": "8094031464-20260701-3EA2E3400000-26",
        "invoiceNumber": "FV/DOSTAWCA/2026/042",
        "issueDate": "2026-07-01T00:00:00+00:00",
        "permanentStorageDate": "2026-07-01T06:54:30.083815+00:00",
        "sellerNip": "8094031464",
        "sellerName": "Dostawca Testowy sp. z o.o.",
        "netAmount": 555, "grossAmount": 682.65, "vatAmount": 127.65,
        "currency": "PLN",
        "hasAttachment": false
      }
    ],
    "hasMore": false
  }
}
```

Download the PDF the same way you would for an invoice you sent - `GET /ksef/invoice/{ksefNumber}/pdf` works for both roles, no extra endpoint needed.

| Query param | Default | Notes |
|---|---|---|
| `from` | 30 days ago | ISO 8601 date/date-time |
| `to` | now | ISO 8601 date/date-time. **KSeF caps the `from`-`to` span at 3 months per call** - page through older history with several calls |
| `page` | `0` | Zero-based page offset |
| `pageSize` | `50` | Max invoices per page |

### Poll for new invoices (sync / notifications)

```bash
# First call - no checkpoint yet
curl "http://localhost:8080/ksef/invoices/received/new" -H "X-KSeF-NIP: YOUR_NIP"
# → {"invoices": [...], "nextSince": "2026-07-01T07:00:00Z"}

# Persist nextSince yourself, pass it back next time - only genuinely new invoices come back
curl "http://localhost:8080/ksef/invoices/received/new?since=2026-07-01T07:00:00Z" -H "X-KSeF-NIP: YOUR_NIP"
```

Wire this into a cron/n8n workflow polling every 15-30 minutes to get notified (email/Slack/webhook) when something new lands - the gateway itself stays stateless, your workflow owns the checkpoint.

KSeF's `query/metadata` endpoint (which this wraps) is capped at **20 requests/hour** - the gateway's own rate limiter enforces this proactively (429 with `Retry-After` before it ever reaches KSeF). Don't poll more often than every 15 minutes per KSeF's own guidance. For very high invoice volume, use the raw `invoice-download/export-invoices` endpoint (async batch export) instead - not yet wrapped in a friendly endpoint here.

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

Ready-to-import workflows in [`examples/n8n/`](examples/n8n/):
- **Sellf → KSeF** (`sellf-ksef.json`) - digital products, NIP check, seller data from n8n variables
- **WooCommerce → KSeF** (`woocommerce-ksef.json`) - WooCommerce orders, VAT rate auto-detection, consumer skipping

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

### KSeF is already mandatory

- **February 1, 2026** - large taxpayers (>200M PLN 2024 revenue) must issue invoices via KSeF. **Everyone**, regardless of size, must be able to **receive** purchase invoices via KSeF from this date.
- **April 1, 2026** - the rest of the B2B market (JDG, SME, sp. z o.o., etc.) must issue via KSeF too.
- **2027** - micro-businesses (≤10k PLN gross invoiced per month) join, closing the last exemption.

If you're reading this after those dates, production setup below isn't optional anymore for most businesses - it's the thing standing between you and a compliant invoice.

### Test vs Production

| Step | TEST | PRODUCTION |
|------|------|------------|
| Identity proof | Self-signed certificate (automatic) | Profil Zaufany (free), qualified e-signature, or qualified electronic seal |
| NIP | Random, any value | Real, registered with tax office |
| Token generation | Same flow | Same flow |
| Who does it | Script (one command) | Business owner or authorized representative (one time) |

### Production Token (step by step)

Getting a production token takes about 10 minutes and, for most businesses, **costs nothing**. **Profil Zaufany is free** (you likely already have it via your bank's login or mObywatel) and is enough to generate a token - you do **not** need to buy a qualified e-signature.

> **Sole proprietors (JDG):** Log in directly, no prior registration needed.
> **Companies (sp. z o.o., SA, fundacje):** Before first login, submit the **ZAW-FA** form to your tax office - a one-time formality - unless the company already has a qualified electronic seal bound to its NIP, which replaces it.

**Via Aplikacja Podatnika KSeF 2.0:**

1. Go to [ap.ksef.mf.gov.pl](https://ap.ksef.mf.gov.pl/)
2. Click **Zaloguj** and authenticate with one of:
   - **Profil Zaufany** (ePUAP / mObywatel / your bank's login) - free, no prior setup, the easiest path for JDG
   - Podpis kwalifikowany (SimplySign, Certum, Szafir)
   - Pieczęć kwalifikowana (companies only - also replaces the ZAW-FA requirement above)
   - e-Dowod (electronic ID card with NFC)
3. Enter your company **NIP** and click **Uwierzytelnij**
4. Review and sign the authentication request
5. Navigate to the **Tokeny** tab
6. Click **Generuj token**
7. Enter a descriptive **name** for the token (e.g. "ksef-gateway API")
8. Select permissions:
   - **Wystawianie faktur** (InvoiceWrite) - sending invoices
   - **Odczyt faktur** / **Przeglądanie faktur** (InvoiceRead) - downloading invoices (**required** for [Receiving Invoices](#receiving-invoices) too - `/ksef/invoices/received` and `/ksef/invoice/{ksefNumber}` both need it, not just `/ksef/send`)
9. Confirm with your authentication method
10. **Copy the token immediately** - it is displayed only once
11. Set in your `.env`:
    ```
    KSEF_TOKEN=<the token you just copied>
    KSEF_NIP=<your company NIP>
    KSEF_ENV=PRODUCTION
    ```
12. Restart the gateway. No code changes, no rebuild - just the env vars above.

You only need to do this once. If you lose the token, revoke it in the portal and generate a new one.

> **Who can generate a token?** Only a person authorized to represent the company (owner, board member listed in KRS, or someone with a KSeF authorization granted by them).

### Certificate-Based Auth (Alternative to Tokens)

Instead of a token, the gateway can authenticate with a **KSeF certificate** - a certificate + private key pair issued by the KSeF portal. Every (re-)login gets signed with the certificate (XAdES) instead of presenting a static secret. This is the officially supported, ongoing authentication path - not a token workaround.

**Getting a certificate:**

1. Log in to [ap.ksef.mf.gov.pl](https://ap.ksef.mf.gov.pl/) the same way as for a token (Profil Zaufany, podpis kwalifikowany, etc.)
2. Go to **Certyfikaty** → **Wnioskuj o certyfikat**
3. Name the certificate and set a password protecting the private key (the portal enforces its own rules for both - follow whatever the form currently asks)
4. Download the two files it generates: a certificate (`.crt`) and a private key (`.key`), both in PEM format

**Using it in the gateway:**

```
KSEF_CERT_PATH=/app/certs/company.crt
KSEF_KEY_PATH=/app/certs/company.key
KSEF_KEY_PASSWORD=<only if the private key is password-protected>
KSEF_NIP=<your company NIP>
KSEF_ENV=PRODUCTION
```

Or per-context in `contexts.json`:

```json
{
  "nip": "1234567890",
  "certificatePath": "/app/certs/company.crt",
  "privateKeyPath": "/app/certs/company.key",
  "privateKeyPassword": "only-if-encrypted",
  "label": "Company A (certificate)"
}
```

Mount the cert/key files read-only, same idea as `contexts.json`:

```yaml
volumes:
  - ./certs:/app/certs:ro
```

A context needs either `token`, or `certificatePath` + `privateKeyPath` - not both. Everything else (endpoints, rate limits, multi-NIP) works identically regardless of which one a context uses.

---

## Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KSEF_TOKEN` | Yes* | - | KSeF authentication token |
| `KSEF_CERT_PATH` | Yes* | - | Path to KSeF certificate (PEM) - alternative to `KSEF_TOKEN`, see [Certificate-Based Auth](#certificate-based-auth-alternative-to-tokens) |
| `KSEF_KEY_PATH` | Yes* | - | Path to the certificate's private key (PEM) - required alongside `KSEF_CERT_PATH` |
| `KSEF_KEY_PASSWORD` | No | - | Password for the private key, if it's encrypted |
| `KSEF_NIP` | Yes | - | NIP for authentication context |
| `KSEF_ENV` | No | `TEST` | Environment: `TEST`, `DEMO`, `PRODUCTION` |
| `KSEF_API_PORT` | No | `8080` | Gateway API port |
| `KSEF_QR_URL` | No | `https://qr-test.ksef.mf.gov.pl` | QR verification base URL |
| `GITHUB_PAT` | Build | - | GitHub PAT with `read:packages` for CIRFMF SDK |
| `KSEF_CONTEXTS_FILE` | No | `/app/contexts.json` | Path to multi-NIP config file |

\* Provide either `KSEF_TOKEN`, or `KSEF_CERT_PATH` + `KSEF_KEY_PATH` - not both.

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
    "certificatePath": "/app/certs/company-b.crt",
    "privateKeyPath": "/app/certs/company-b.key",
    "label": "Company B (certificate)"
  }
]
```

Contexts can mix tokens and certificates freely - see [Certificate-Based Auth](#certificate-based-auth-alternative-to-tokens) above.

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
| **TokenPool** | Background service: per-NIP KSeF auth (token or certificate/XAdES) + auto-refresh |
| **WorkflowEndpoints** | High-level: `/ksef/send`, `/ksef/send/json`, `/ksef/invoice/{nr}/pdf` |
| **InvoiceDownloadEndpoints** | High-level: `/ksef/invoices/received`, `/ksef/invoices/received/new` |
| **EndpointErrorHandling** | Shared `Guard()` - lets KSeF rate-limit/circuit-breaker/API errors surface as proper 429/503/502 (with `Retry-After`) instead of a flat 500 |
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

AGPL-3.0 - see [LICENSE](LICENSE)

If you modify KSeF Gateway and offer it as a service, AGPL-3.0 requires you to make the modified source available to your users.

---

## Acknowledgments

- **[CIRFMF](https://github.com/CIRFMF)** (Centrum Informatyki Resortu Finansów) - official KSeF SDK, PDF generator, and documentation
- **[KSeF Documentation](https://github.com/CIRFMF/ksef-docs)** - integration guide for KSeF 2.0
- **[Scalar](https://scalar.com/)** - API documentation UI
