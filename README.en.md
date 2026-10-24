# KSeF Gateway

> REST API for Poland's e-Invoice System (KSeF). Send a JSON, get a KSeF number. Receive invoices without knowing their number. One HTTP call, both directions.

Run examples from the repository root. Export `GATEWAY_API_KEY` matching the service configuration (`.env` does not export it into your shell). The shared fixture is [`examples/invoice-simple.json`](examples/invoice-simple.json): NIP `3202004132` is the TEST demo company. For your installation replace both NIPs and party details, dates and invoice number; the seller must match a configured KSeF context. Use a new invoice number for each send. In download commands, `KSEF_NUMBER` is the value returned in `data.ksefNumber`; `KSEF_NIP` is your context NIP. When proxy enforcement is enabled, use the proxy URL.

<details>
<summary>🇵🇱 Po polsku</summary>

**KSeF Gateway** to bramka REST API do Krajowego Systemu e-Faktur. Wysyłasz prosty JSON z danymi faktury, dostajesz numer KSeF. Odbierasz faktury wystawione na Ciebie bez znajomości ich numeru - jednym zapytaniem po dacie. Jedno wywołanie HTTP zamiast budowania XML, szyfrowania AES-256, zarządzania sesjami i tokenami.

```bash
curl -H "Content-Type: application/json" -H "X-Api-Key: $GATEWAY_API_KEY" -X POST https://twoj-gateway/ksef/invoice \
  -d '{
  "invoiceNumber": "FV/EXAMPLE/001/2026",
  "issueDate": "2026-03-24",
  "issuePlace": "Warszawa",
  "saleDate": "2026-03-24",
  "currency": "PLN",
  "type": "VAT",
  "seller": {"nip": "3202004132", "name": "Przykładowa Firma sp. z o.o.", "address": {"street": "ul. Sprzedawcy 10", "city": "00-001 Warszawa", "country": "PL"}, "email": "firma@example.com", "phone": "123456789"},
  "buyer": {"nip": "3202004132", "name": "Klient Testowy sp. z o.o.", "address": {"street": "ul. Kupiecka 5", "city": "30-001 Kraków", "country": "PL"}},
  "items": [{"name": "Usługa konsultingowa IT", "unit": "godz.", "quantity": 10, "unitPrice": 80.0, "vatRate": 23}, {"name": "Konfiguracja serwera", "unit": "szt.", "quantity": 1, "unitPrice": 200.0, "vatRate": 23}],
  "payment": {"paid": true, "date": "2026-03-24", "method": "transfer"}
}'
# → {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}
```

**Szybki start:** `docker compose up` i gotowe. Nie potrzebujesz .NET lokalnie.

**Cechy:** wysyłanie i odbieranie faktur, oficjalne SDK Ministerstwa Finansów (CIRFMF), PDF z QR, 60+ endpointów, multi-NIP, deploy jednym kliknięciem (Render/Lambda/Azure). Gotowe na obowiązkowy KSeF (produkcja, nie tylko test).

**Instrukcja generowania tokenu produkcyjnego:** [Production Token](#production-token-step-by-step)

**Pełna wersja po polsku:** [README.md](README.md)

</details>

![License](https://img.shields.io/badge/License-AGPL%20v3-blue)
![Status](https://img.shields.io/badge/Status-Beta-yellow)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
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
curl -H "X-Api-Key: $GATEWAY_API_KEY" -X POST https://your-gateway/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
  "invoiceNumber": "FV/EXAMPLE/001/2026",
  "issueDate": "2026-03-24",
  "issuePlace": "Warszawa",
  "saleDate": "2026-03-24",
  "currency": "PLN",
  "type": "VAT",
  "seller": {"nip": "3202004132", "name": "Przykładowa Firma sp. z o.o.", "address": {"street": "ul. Sprzedawcy 10", "city": "00-001 Warszawa", "country": "PL"}, "email": "firma@example.com", "phone": "123456789"},
  "buyer": {"nip": "3202004132", "name": "Klient Testowy sp. z o.o.", "address": {"street": "ul. Kupiecka 5", "city": "30-001 Kraków", "country": "PL"}},
  "items": [{"name": "Usługa konsultingowa IT", "unit": "godz.", "quantity": 10, "unitPrice": 80.0, "vatRate": 23}, {"name": "Konfiguracja serwera", "unit": "szt.", "quantity": 1, "unitPrice": 200.0, "vatRate": 23}],
  "payment": {"paid": true, "date": "2026-03-24", "method": "transfer"}
}'

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

## Security

API responses (including errors), except `/health` and `/scalar` and `/openapi` documentation, as well as PDF service responses from `/pdf/invoice` and `/json-to-xml`, include `Cache-Control: no-store` and `X-Content-Type-Options: nosniff` to prevent caching tax data and content type sniffing.

The gateway authenticates *itself* to KSeF (token or certificate - see below), but on its own has **no way to authenticate whoever is calling it**. Deploy it on a reachable URL without protecting it, and anyone who finds that URL can send or read real invoices for whichever NIP it's configured with.

**`GATEWAY_API_KEY` closes that gap.** Every request except `GET /health` (used by health checks/monitoring) must include it as an `X-Api-Key` header, or the gateway rejects it - including `/scalar/v1` and the OpenAPI JSON, so the API surface itself isn't exposed either:

| Situation | Response |
|---|---|
| `GATEWAY_API_KEY` not set on the gateway | `503` - fails **closed**: refuses everything rather than silently staying open |
| Missing or wrong `X-Api-Key` header | `401` |
| Correct `X-Api-Key` header | Request proceeds normally |

Generate a strong key once, keep it secret - same handling as `KSEF_TOKEN`:

```bash
openssl rand -hex 32
```

> This is separate from `KSEF_TOKEN`/certificate auth, which protects the gateway's own connection *to* KSeF. You need both: one so the gateway can talk to KSeF, one so only you can talk to the gateway.

**Defense in depth for production:** the API key is the one thing standing between a public URL and the entire internet, so for a real deployment also restrict network access where the platform allows it - an IP allowlist (Render, Azure), a VPN/private network, or a reverse proxy in front of the gateway. Don't rely on the key as the only layer.

---

## Try the Live Demo

No setup required. A public demo instance runs around the clock (no cold starts), authenticated to a disposable KSeF **TEST** company (not a real business) via certificate. Self-invoicing is enabled, so you can send an invoice and immediately look yourself up as the buyer:

```bash
DEMO_URL="https://ksef-demo.techskills.academy"
GATEWAY_API_KEY="0679d36400bfdedcaf1f7d1f5774d0d94ffae5a9f6bc7596cbf24de94d42a8ee"
DEMO_NIP="3202004132"

curl -X POST "$DEMO_URL/ksef/invoice" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $GATEWAY_API_KEY" \
  -d '{
  "invoiceNumber": "FV/EXAMPLE/001/2026",
  "issueDate": "2026-03-24",
  "issuePlace": "Warszawa",
  "saleDate": "2026-03-24",
  "currency": "PLN",
  "type": "VAT",
  "seller": {"nip": "3202004132", "name": "Przykładowa Firma sp. z o.o.", "address": {"street": "ul. Sprzedawcy 10", "city": "00-001 Warszawa", "country": "PL"}, "email": "firma@example.com", "phone": "123456789"},
  "buyer": {"nip": "3202004132", "name": "Klient Testowy sp. z o.o.", "address": {"street": "ul. Kupiecka 5", "city": "30-001 Kraków", "country": "PL"}},
  "items": [{"name": "Usługa konsultingowa IT", "unit": "godz.", "quantity": 10, "unitPrice": 80.0, "vatRate": 23}, {"name": "Konfiguracja serwera", "unit": "szt.", "quantity": 1, "unitPrice": 200.0, "vatRate": 23}],
  "payment": {"paid": true, "date": "2026-03-24", "method": "transfer"}
}'
# → {"success":true,"data":{"ksefNumber":"..."}}

curl "$DEMO_URL/ksef/invoices/received?from=2026-01-01&to=2026-12-31" \
  -H "X-Api-Key: $GATEWAY_API_KEY"
# → {"success":true,"data":{"invoices":[{"ksefNumber":"...","invoiceNumber":"DEMO/...",...}],"hasMore":false}}
```

Or import the [Bruno collection](#testing-with-bruno) and switch to the `demo` environment - every request works immediately, no credentials to find.

> **This demo is public and shared** - don't send anything sensitive. It's a disposable TEST-environment NIP, not connected to any real business, and the API key may be rotated without notice if abused.

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
# Edit .env: set GITHUB_PAT and GATEWAY_API_KEY (e.g. `openssl rand -hex 32`)
```

> **Why does `.env` need a `GATEWAY_API_KEY`?** The gateway has no other caller-facing auth - see [Security](#security) below. Every request except `GET /health` needs it back as the `X-Api-Key` header, or the gateway rejects it.

### 2. Generate a KSeF test token

```bash
docker compose --profile tools run --rm token-generator
```

Copy the output (`KSEF_TOKEN`, `KSEF_NIP`, `KSEF_ENV`) into your `.env` file.

> **What does this do?** See [How Token Generator Works](#how-token-generator-works) below. Prefer testing the [certificate-based auth path](#certificate-based-auth-alternative-to-tokens) instead? `docker compose --profile tools run --rm cert-generator` does the same for certificates - see [How Cert Generator Works](#how-cert-generator-works).

### 3. Run the gateway

```bash
docker compose up --build
```

API: `http://localhost:8080` | Docs: `http://localhost:8080/scalar/v1`

### 4. Send your first invoice

```bash
curl -X POST http://localhost:8080/ksef/invoice \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $GATEWAY_API_KEY" \
  -d '{
  "invoiceNumber": "FV/EXAMPLE/001/2026",
  "issueDate": "2026-03-24",
  "issuePlace": "Warszawa",
  "saleDate": "2026-03-24",
  "currency": "PLN",
  "type": "VAT",
  "seller": {"nip": "3202004132", "name": "Przykładowa Firma sp. z o.o.", "address": {"street": "ul. Sprzedawcy 10", "city": "00-001 Warszawa", "country": "PL"}, "email": "firma@example.com", "phone": "123456789"},
  "buyer": {"nip": "3202004132", "name": "Klient Testowy sp. z o.o.", "address": {"street": "ul. Kupiecka 5", "city": "30-001 Kraków", "country": "PL"}},
  "items": [{"name": "Usługa konsultingowa IT", "unit": "godz.", "quantity": 10, "unitPrice": 80.0, "vatRate": 23}, {"name": "Konfiguracja serwera", "unit": "szt.", "quantity": 1, "unitPrice": 200.0, "vatRate": 23}],
  "payment": {"paid": true, "date": "2026-03-24", "method": "transfer"}
}'

# Response: {"success":true,"data":{"ksefNumber":"1234567890-20260326-..."}}

# Download PDF with QR code
curl -H "X-Api-Key: $GATEWAY_API_KEY" -o faktura.pdf "http://localhost:8080/ksef/invoice/$KSEF_NUMBER/pdf"
```

> **Also supports raw XML** (`POST /ksef/send`) and xml-js JSON format (`POST /ksef/send/json`) - see [Sending Invoices](#sending-invoices) below.

### 5. Find an invoice sent to you

On TEST/DEMO you can try this immediately without a second company: set `buyer.nip` to the **same** NIP you used as `seller.nip` above (KSeF supports self-invoicing), then look yourself up as the buyer:

```bash
curl "http://localhost:8080/ksef/invoices/received?from=2026-03-01&to=2026-04-01" \
  -H "X-Api-Key: $GATEWAY_API_KEY" \
  -H "X-KSeF-NIP: $KSEF_NIP"

# Response: {"success":true,"data":{"invoices":[{"ksefNumber":"...","invoiceNumber":"FV/2026/001",...}],"hasMore":false}}
```

No need to already know the KSeF number - see [Receiving Invoices](#receiving-invoices) below for the full picture, including polling for new invoices.

---

## Testing with Bruno

A [Bruno](https://www.usebruno.com/) collection is included in the `bruno/` directory - all endpoints with assertions.

**Setup:**
1. Install Bruno (desktop app or CLI: `npm install -g @usebruno/cli`)
2. Open collection in Bruno desktop: **Open Collection** → select `bruno/`
3. Select environment `local`, `render` (your own Render deploy), or `demo` (the [live demo](#try-the-live-demo) - no credentials to set, already filled in)
4. For `local`/`render`: set `apiKey` to your `GATEWAY_API_KEY` and `sellerNip` to your NIP - the collection-level header (`collection.bru`) sends `apiKey` as `X-Api-Key` on every request automatically

**Run with CLI:**
```bash
# Health check (the only endpoint that doesn't need an API key)
cd bruno && bru run health.bru --env local

# Full collection (requires GATEWAY_API_KEY, KSEF_TOKEN + KSEF_NIP in .env)
bru run --env local
```

`send-xml.bru` and `send-invoice.bru` automatically save the returned `ksefNumber` as a variable - after sending, `get-invoice-xml` and `get-invoice-pdf` work immediately.

**What's in the collection:**

| Request | Endpoint |
|---|---|
| `health.bru` | `GET /health` |
| `status.bru` | `GET /ksef/status` |
| `contexts.bru` | `GET /ksef/contexts` |
| `send-invoice.bru` | `POST /ksef/invoice` (friendly JSON) |
| `send-xml.bru` | `POST /ksef/send` (raw FA(3) XML) |
| `send-xml-explicit-nip.bru` | `POST /ksef/send` with `X-KSeF-NIP` header (multi-NIP) |
| `send-json.bru` | `POST /ksef/send/json` (xml-js format) |
| `get-invoice-xml.bru` | `GET /ksef/invoice/{ksefNumber}` |
| `get-invoice-pdf.bru` | `GET /ksef/invoice/{ksefNumber}/pdf` |
| `list-received-invoices.bru` | `GET /ksef/invoices/received` |
| `list-new-received-invoices.bru` | `GET /ksef/invoices/received/new` |
| `list-issued-invoices.bru` | `GET /ksef/invoices/issued` |

---

## API Endpoints

> **Every endpoint below except `GET /health` requires an `X-Api-Key` header** (see [Security](#security)). Examples further down omit it for brevity - assume it's there.

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
| `GET` | `/ksef/invoices/issued` | `?from=&to=&page=&pageSize=` | List of invoices you issued (seller role) |
| `GET` | `/ksef/status` | - | Gateway + KSeF status |
| `GET` | `/health` | - | Health check |

### Auto-discovered SDK Endpoints (low-level)

Reflection discovers 64 methods in 14 groups in SDK 2.9.0. By default, 20 invoice sending/receiving methods are exposed as `POST /ksef/{group}/{method}`:

| Group | Endpoints | Default | Description |
|-------|-----------|---------|-------------|
| `online-session` | 3 | Yes | Interactive invoice sending |
| `batch-session` | 4 | Yes | Batch sending (ZIP packages) |
| `invoice-download` | 4 | Yes | Download, query, export invoices |
| `session-status` | 9 | Yes | Session/invoice status, UPO |
| `active-sessions` | 3 | No | List and revoke authentication sessions |
| `authorization` | 6 | No | Authentication, issue and refresh access tokens |
| `ksef-token` | 4 | No | Generate, query, revoke tokens |
| `certificate` | 7 | No | KSeF certificate management |
| `grant-permission` | 7 | No | Grant permissions |
| `revoke-permission` | 2 | No | Revoke permissions |
| `search-permission` | 8 | No | Search permissions |
| `permission-operation` | 2 | No | Permission operation status |
| `collective-identifiers` | 4 | No | Collective invoice identifiers |
| `peppol` | 1 | No | Query Peppol providers |

`KSEF_SDK_ENDPOINTS`: unset, empty or `safe` selects the defaults above. `all` enables every method and logs a startup warning. A comma-separated list selects exactly the groups or individual methods, e.g. `online-session,invoice-download/get-invoice`. Names are case-sensitive; surrounding whitespace is ignored. Unknown names or invalid syntax stop startup with a list of available names. `safe` and `all` are standalone modes, not list entries.

**BREAKING (v0.3.0):** if you used `ksef-token`, `permissions` (now four separate groups) or `certificate`, explicitly set `KSEF_SDK_ENDPOINTS=all` or select only the required groups/methods. These operations can create persistent KSeF access outside the gateway. `lighthouse` is absent from SDK 2.9.0 reflection; if a newer SDK adds it, it belongs to `safe`.

Hidden routes return 404 after passing authentication middleware and appear in neither `/openapi/v1.json` nor `/scalar/v1`. Gateway-owned endpoints (`/ksef/send`, `/ksef/send/json`, `/ksef/invoice`, `/ksef/invoice/{ksefNumber}` and `/pdf`, `/ksef/invoices/*`, `/ksef/status`, `/ksef/contexts`) and internal authentication always work independently of this setting. `/health` returns only `{"status":"ok"}`. API-key-protected `/ksef/status` reports environment, authentication state, `exposedEndpoints` and `discoveredEndpoints`; startup logs report both counts too.

Interactive documentation of exposed routes is available at `/scalar/v1`.

---

### Updating the SDK

The contract of all 64 discovered SDK 2.9.0 routes (including those hidden by `KSEF_SDK_ENDPOINTS`) is stored in `src/KSeFGateway.Api.Tests/Discovery/sdk-contract.approved.json`. `SdkContract_MatchesApprovedManifest` compares routes, argument names and types, argument order and defaults, and return types against reflection. When updating the pinned SDK, run the tests; a mismatch writes `sdk-contract.actual.json` to the test output directory (`bin/Release/net10.0`). Compare the files, review API compatibility, explicitly replace the approved manifest with the generated file, document the change in CHANGELOG, and rerun tests. For Docker tests, retain the container (omit `--rm`) to retrieve the file with `docker cp`.

JSON bodies also work with chunked transfer. Missing required arguments (including explicit `null`) return 400 with the argument name; malformed JSON or an invalid argument type also returns 400. Supply arguments under their SDK names; the entire body can replace an argument only for a method with exactly one composite business parameter. Optional parameters retain SDK defaults.

## Sending Invoices

### Option 1: Friendly JSON (recommended)

Simple JSON with human-readable fields. Gateway builds FA(3) XML and calculates VAT totals automatically.

Each line's net amount is rounded to 2 decimal places using commercial rounding (midpoints away from zero). For each VAT rate, these net amounts are summed (`P_13_x`), then VAT is calculated from that sum and rounded the same way (`P_14_x`). The gross total (`P_15`) sums those same net and VAT summary amounts; e.g. two lines at PLN 0.03 net with 23% VAT produce PLN 0.06 net + PLN 0.01 VAT = PLN 0.07 gross.

`vatRate: 0` means domestic 0% sales only, excluding intra-community supplies (WDT) and exports (`P_13_6_1`, no `P_14_6`; line `P_12 = 0 KR`). WDT and exports at 0% require raw XML via `POST /ksef/send`; the friendly format does not support them.

The friendly format accepts numeric `vatRate: 23/8/5/0` and these strings:

| `vatRate` | Meaning / XML summary |
|---|---|
| `"zw"` | Exempt sales, `P_13_7`; requires `exemptionBasis` (Polish statutory basis, `Zwolnienie/P_19=1`, `P_19A`) |
| `"np"` or `"np I"` | Outside-territory supplies excluding Article 100(1)(4) and OSS; `P_12=np I`, `P_13_8` |
| `"np II"` | Services under Article 100(1)(4) of the Polish VAT Act; `P_13_9` |

No `P_14` fields are emitted for `zw`/`np`; `P_15` still sums the same rounded net and VAT summaries. `np` is an alias of `np I`, not intra-community supplies (WDT). `P_13_10` covers domestic reverse charge and does not map to `np`. Other annotations/procedures, including reverse charge and foreign buyer identifiers, require raw XML.

Corrections require `type: "KOR"`, a nonempty `correctionReason`, and `correctedInvoice`: `issueDate` (YYYY-MM-DD), `invoiceNumber`, and exactly one of `ksefNumber` or `outsideKsef: true`. Items express **differences**, rather than full corrected values: `quantity × unitPrice` is the net change. KOR quantities may be negative (but not zero), and prices may be negative; ordinary invoices still require positive quantities and nonnegative prices. For example, quantity 1 and price -10 at 23% means -10 net, -2.30 VAT, -12.30 gross. A rate change can use a negative line at the old rate and a positive line at the new rate. The builder omits `StanPrzed` because it uses the difference representation explicitly permitted by the FA(3) `FaWiersz` documentation. Discounts and advances have no dedicated model.

Complete examples: [exempt](examples/invoice-exempt.json) and [correction](examples/invoice-correction.json). The examples use the TEST demo NIP (`3202004132`); replace it with your own NIP before sending:

```bash
curl -X POST http://localhost:8080/ksef/invoice \
  -H "X-Api-Key: $GATEWAY_API_KEY" -H "Content-Type: application/json" \
  -d @examples/invoice-exempt.json
curl -X POST http://localhost:8080/ksef/invoice \
  -H "X-Api-Key: $GATEWAY_API_KEY" -H "Content-Type: application/json" \
  -d @examples/invoice-correction.json
```


```bash
curl -H "X-Api-Key: $GATEWAY_API_KEY" -X POST http://localhost:8080/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
  "invoiceNumber": "FV/EXAMPLE/001/2026",
  "issueDate": "2026-03-24",
  "issuePlace": "Warszawa",
  "saleDate": "2026-03-24",
  "currency": "PLN",
  "type": "VAT",
  "seller": {"nip": "3202004132", "name": "Przykładowa Firma sp. z o.o.", "address": {"street": "ul. Sprzedawcy 10", "city": "00-001 Warszawa", "country": "PL"}, "email": "firma@example.com", "phone": "123456789"},
  "buyer": {"nip": "3202004132", "name": "Klient Testowy sp. z o.o.", "address": {"street": "ul. Kupiecka 5", "city": "30-001 Kraków", "country": "PL"}},
  "items": [{"name": "Usługa konsultingowa IT", "unit": "godz.", "quantity": 10, "unitPrice": 80.0, "vatRate": 23}, {"name": "Konfiguracja serwera", "unit": "szt.", "quantity": 1, "unitPrice": 200.0, "vatRate": 23}],
  "payment": {"paid": true, "date": "2026-03-24", "method": "transfer"}
}'
```

### Option 2: Raw FA(3) XML

For systems that already generate KSeF XML (accounting software, ERP):

```bash
curl -H "X-Api-Key: $GATEWAY_API_KEY" -X POST http://localhost:8080/ksef/send \
  -H "Content-Type: application/xml" \
  --data-binary @examples/invoice.xml
```

### Option 3: JSON mirroring XML (xml-js format)

1:1 JSON representation of FA(3) XML using [xml-js compact format](https://www.npmjs.com/package/xml-js#compact-notation). When XSD changes, adapt input data and validate the resulting XML. See [`examples/invoice.json`](examples/invoice.json) for full example. The converter accepts one `Faktura` object, element/attribute names matching `^[A-Za-z_][A-Za-z0-9._-]*$` (no namespace prefixes; default `xmlns` is allowed), and `_text` / `_attributes` metadata. DOCTYPE, instructions, declarations, comments and CDATA are rejected with 400. Input is limited to 100 JSON nesting levels and 100,000 values (including objects, arrays, attributes and text); attribute values are escaped.

```bash
curl -H "X-Api-Key: $GATEWAY_API_KEY" -X POST http://localhost:8080/ksef/send/json \
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

Sending (`/ksef/send`, `/ksef/send/json`, `/ksef/invoice`) polls status every 2 seconds for up to 60 seconds. Codes 100/150 mean processing; success requires code 200 and a KSeF number. Rejection returns 502 with the code and description in `error`. A timeout returns 502 with acceptance unknown — check status before resending to avoid a duplicate.

### Error responses

Every endpoint returns the same shape on failure: `{"success": false, "data": null, "error": "..."}`. The HTTP status code tells you whether to retry and how:

| Status | Meaning | What to do |
|--------|---------|------------|
| `400` | Bad input (missing NIP, invalid XML, malformed body) | Fix the request, don't retry as-is |
| `401` | Missing or wrong `X-Api-Key` header - see [Security](#security) | Send the correct gateway API key |
| `429` | You're about to hit (or hit) a KSeF rate limit | Wait the `Retry-After` header (seconds), then retry |
| `502` | KSeF's own API rejected or failed the request | Check `error` for the KSeF error code/message; not always safe to retry blindly |
| `503` | One of: `GATEWAY_API_KEY` isn't configured on the gateway at all, KSeF's SDK circuit breaker is open (has `Retry-After` header - wait then retry), or the gateway isn't authenticated for that NIP yet (`TokenPool` still starting up - no `Retry-After`, retry shortly) | See `error` message to tell which one |
| `500` | Unexpected error in the gateway itself | Check gateway logs; please open an issue |

`429` and circuit-breaker `503` responses include a `Retry-After` header (seconds) - respect it instead of retrying immediately, especially for the [rate-limited receiving endpoints](#receiving-invoices).

---

## PDF Generation

```bash
# Get PDF with QR code by KSeF number (one call - downloads XML from KSeF internally)
curl -H "X-Api-Key: $GATEWAY_API_KEY" -o faktura.pdf "http://localhost:8080/ksef/invoice/$KSEF_NUMBER/pdf"

# Generate PDF from XML directly
curl -H "X-Api-Key: $GATEWAY_API_KEY" -X POST http://localhost:8080/pdf/invoice \
  -H "Content-Type: application/xml" \
  --data-binary @examples/invoice.xml -o faktura.pdf

# Generate PDF with QR code
curl -H "X-Api-Key: $GATEWAY_API_KEY" -X POST "http://localhost:8080/pdf/invoice?nrKSeF=$KSEF_NUMBER" \
  -H "Content-Type: application/xml" \
  --data-binary @examples/invoice.xml -o faktura.pdf
```

PDFs are generated using the official [CIRFMF/ksef-pdf-generator 1.1.46](https://github.com/CIRFMF/ksef-pdf-generator/releases/tag/1.1.46) library (FA(1), FA(2), FA(3), Polish translations; pdfmake 0.3.12). QR codes contain the KSeF verification URL with SHA-256 hash - scannable and verified by KSeF.

---

## Receiving Invoices

KSeF has no email/webhook notifications - invoices issued to you just sit in the system. These endpoints let you find them without already knowing their KSeF number.

Both endpoints search for invoices where **your NIP is the buyer** (`Podmiot2`), matching however KSeF resolves the caller's context - see [Multi-NIP Mode](#multi-nip-mode) if you run the gateway for more than one company: pass `X-KSeF-NIP` to pick which NIP's inbox to search. Requires a token with the `InvoiceRead` permission - see the note in [Production Token](#production-token-step-by-step).

### Browse what you've received

```bash
curl -H "X-Api-Key: $GATEWAY_API_KEY" "http://localhost:8080/ksef/invoices/received?from=2026-06-01&to=2026-07-01" \
  -H "X-KSeF-NIP: $KSEF_NIP"
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
curl -H "X-Api-Key: $GATEWAY_API_KEY" "http://localhost:8080/ksef/invoices/received/new" -H "X-KSeF-NIP: $KSEF_NIP"
# → {"invoices": [...], "nextSince": "2026-07-01T07:00:00Z"}

# Persist nextSince yourself, pass it back next time - only genuinely new invoices come back
curl -H "X-Api-Key: $GATEWAY_API_KEY" "http://localhost:8080/ksef/invoices/received/new?since=2026-07-01T07:00:00Z" -H "X-KSeF-NIP: $KSEF_NIP"
```

Wire this into a cron/n8n workflow polling every 15-30 minutes to get notified (email/Slack/webhook) when something new lands - the gateway itself stays stateless, your workflow owns the checkpoint.

KSeF's `query/metadata` endpoint (which this wraps) is capped at **20 requests/hour** - the gateway's own rate limiter enforces this proactively (429 with `Retry-After` before it ever reaches KSeF). Don't poll more often than every 15 minutes per KSeF's own guidance. For very high invoice volume, use the raw `invoice-download/export-invoices` endpoint (async batch export) instead - not yet wrapped in a friendly endpoint here.

The limiter counts every actual SDK HTTP attempt: each polling page, session open/send/close, status lookup and retry. Friendly and raw endpoints share the operation budget regardless of route casing. Protected operations have separate counters per NIP and category (for one egress IP); anonymous operations share the per-IP budget. `/health`, `/ksef/status` and `/ksef/contexts` consume no KSeF budget. The gateway also honors upstream `Retry-After`.

Counters live **in memory in a single replica**, reset on restart and are not synchronized with other instances or clients using the same NIP/IP. The gateway uses MF's default production limits (including separate session-close limits introduced in API 2.8.0); customized KSeF profiles can differ. The future global per-IP limit is disabled according to MF's 2.8.0 changelog. If a workflow is denied midway, earlier operations may already have completed; check session/invoice status before sending again.

**Ready-to-import n8n example:** [`examples/n8n/receive-invoices.json`](examples/n8n/receive-invoices.json) ([polska wersja](examples/n8n/receive-invoices-PL.json)) - polls `/ksef/invoices/received/new` every 20 minutes, keeps the checkpoint in the workflow's own static data, downloads each new invoice's PDF to disk, and leaves a `Notify (TODO)` node to wire up Slack/email/Discord.

---

## Invoices You Issued

`GET /ksef/invoices/issued` browses invoices where **your NIP is the seller** (`Podmiot1`) - the mirror of [Receiving Invoices](#receiving-invoices) above, for when you already know you sent something but don't have its KSeF number handy (e.g. you lost the response, or you're building a "my invoices" screen). Same auth, same rate limits, same `X-KSeF-NIP` multi-NIP selection.

```bash
curl "http://localhost:8080/ksef/invoices/issued?from=2026-06-01&to=2026-07-01" \
  -H "X-Api-Key: $GATEWAY_API_KEY" \
  -H "X-KSeF-NIP: $KSEF_NIP"
```

```json
{
  "success": true,
  "data": {
    "invoices": [
      {
        "ksefNumber": "8094031464-20260701-3EA2E3400000-26",
        "invoiceNumber": "FV/2026/001",
        "issueDate": "2026-07-01T00:00:00+00:00",
        "permanentStorageDate": "2026-07-01T06:54:30.083815+00:00",
        "buyerIdentifierType": "Nip",
        "buyerIdentifierValue": "5265877635",
        "buyerName": "Klient Testowy sp. z o.o.",
        "netAmount": 555, "grossAmount": 682.65, "vatAmount": 127.65,
        "currency": "PLN",
        "hasAttachment": false
      }
    ],
    "hasMore": false
  }
}
```

Buyer identity is `buyerIdentifierType`/`buyerIdentifierValue` rather than a flat NIP field - KSeF allows buyers identified by something other than NIP (e.g. `VatUe` for EU VAT numbers), unlike sellers who are always NIP-identified.

Same `from`/`to`/`page`/`pageSize` query params and 3-month span cap as [Receiving Invoices](#receiving-invoices). Download the PDF with the same `GET /ksef/invoice/{ksefNumber}/pdf` endpoint used everywhere else. There's no polling/`new` variant here - unlike invoices received from someone else, you already know when you sent something, so there's nothing to be notified about.

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
curl -H "X-Api-Key: $GATEWAY_API_KEY" -X POST https://your-ksef-gateway.onrender.com/ksef/invoice \
  -H "Content-Type: application/json" \
  -d '{
  "invoiceNumber": "FV/EXAMPLE/001/2026",
  "issueDate": "2026-03-24",
  "issuePlace": "Warszawa",
  "saleDate": "2026-03-24",
  "currency": "PLN",
  "type": "VAT",
  "seller": {"nip": "3202004132", "name": "Przykładowa Firma sp. z o.o.", "address": {"street": "ul. Sprzedawcy 10", "city": "00-001 Warszawa", "country": "PL"}, "email": "firma@example.com", "phone": "123456789"},
  "buyer": {"nip": "3202004132", "name": "Klient Testowy sp. z o.o.", "address": {"street": "ul. Kupiecka 5", "city": "30-001 Kraków", "country": "PL"}},
  "items": [{"name": "Usługa konsultingowa IT", "unit": "godz.", "quantity": 10, "unitPrice": 80.0, "vatRate": 23}, {"name": "Konfiguracja serwera", "unit": "szt.", "quantity": 1, "unitPrice": 200.0, "vatRate": 23}],
  "payment": {"paid": true, "date": "2026-03-24", "method": "transfer"}
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
- **Receive & Download Invoices** (`receive-invoices.json`) - polls for invoices issued *to* you every 20 minutes, downloads PDFs, checkpointed so nothing gets skipped or re-scanned - see [Receiving Invoices](#receiving-invoices)

Sellf/WooCommerce templates require a Header Auth credential, a signing secret (prefer an environment variable, with `webhookSecret` in Configuration as fallback) and serialized executions. A Configuration secret is visible in workflow exports — do not share them. Before activation, read the [configuration and deduplication limits](examples/n8n/README.md#commercial-workflow-safety-sellf-and-woocommerce).

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

### How Cert Generator Works

`docker compose --profile tools run --rm cert-generator` does the certificate equivalent - also **TEST only**, using a disposable self-signed certificate:

```
Step 1: Generate random NIP with valid checksum
Step 2: Create self-signed X.509 certificate (accepted on TEST only)
Step 3: POST /auth/challenge → get one-time challenge from KSeF
Step 4: Build AuthTokenRequest XML, sign with XAdES
Step 5: POST /auth/xades-signature → submit signed auth request
Step 6: Poll GET /auth/{ref} until status = 200 (auth verified)
Step 7: Export certificate + private key as PEM to ./output
```

Output: `test-cert.crt`, `test-cert.key`, `test-cert.nip` in `./output` - the same files a production certificate download from the portal gives you, just self-signed instead of MF-issued. Unlike the token generator, it stops after verifying the auth succeeds - it's proving the gateway's certificate-loading code works, not minting a long-lived credential.

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
   - **Odczyt faktur** / **Przeglądanie faktur** (InvoiceRead) - downloading invoices (**required** for [Receiving Invoices](#receiving-invoices) and [Invoices You Issued](#invoices-you-issued) too - `/ksef/invoices/received`, `/ksef/invoices/issued`, and `/ksef/invoice/{ksefNumber}` all need it, not just `/ksef/send`)
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

**Try it on TEST first:**

```bash
docker compose --profile tools run --rm cert-generator
```

One command generates a disposable self-signed certificate, verifies it actually authenticates against the live KSeF TEST API, and writes `test-cert.crt` + `test-cert.key` to `./output`, plus the random NIP it registered with. Point `KSEF_CERT_PATH`/`KSEF_KEY_PATH`/`KSEF_NIP` at those files with `KSEF_ENV=TEST` to try the whole flow before touching a real certificate. Self-signed certificates only work on TEST - production needs a real one from the portal, below.

**Getting a production certificate:**

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

Containers run without root: API as `app` (UID 1654, supplementary group 1000 for Render Secret Files), PDF as `node` (UID 1000). Mounted `contexts.json`, certificates and keys must be readable by the API user; keep mounts read-only (`:ro`). On a Linux host, assign files to UID 1654 with mode `0400` (directories `0500`), or GID 1000 with mode `0440` (directories `0550`). Do not make private keys world-readable. Bind-mount permissions are controlled by the host; the image cannot change read-only mounted files.

```yaml
volumes:
  - ./certs:/app/certs:ro
```

A context needs exactly one of: `token`, `certificatePath` + `privateKeyPath`, or `certificateContent` + `privateKeyContent`. Everything else (endpoints, rate limits, multi-NIP) works identically no matter which one a context uses.

**No file mounts available (Lambda, Container Apps)?** Use `certificateContent`/`privateKeyContent` (or `KSEF_CERT_CONTENT`/`KSEF_KEY_CONTENT`) instead - the raw PEM text rather than a path. Prefer the path-based form wherever mounting a file is practical (Docker Compose bind mount, Render Secret Files) - a file stays out of plain env var dumps (`docker inspect`, process env listings) in a way a content-based secret can't. See [Cloud Deployment](#cloud-deployment) for how each platform wires this.

---

## Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `GATEWAY_API_KEY` | Yes | - | Shared secret callers must send as `X-Api-Key` - see [Security](#security). Gateway fails closed (503) if unset |
| `KSEF_TOKEN` | Yes* | - | KSeF authentication token |
| `KSEF_CERT_PATH` | Yes* | - | Path to KSeF certificate (PEM) - alternative to `KSEF_TOKEN`, see [Certificate-Based Auth](#certificate-based-auth-alternative-to-tokens) |
| `KSEF_KEY_PATH` | Yes* | - | Path to the certificate's private key (PEM) - required alongside `KSEF_CERT_PATH` |
| `KSEF_CERT_CONTENT` | Yes* | - | KSeF certificate as raw PEM content - alternative to `KSEF_CERT_PATH` for platforms without file mounts |
| `KSEF_KEY_CONTENT` | Yes* | - | Private key as raw PEM content - required alongside `KSEF_CERT_CONTENT` |
| `KSEF_KEY_PASSWORD` | No | - | Password for the private key, if it's encrypted (works with either path or content form) |
| `KSEF_NIP` | Yes | - | NIP for authentication context |
| `KSEF_ENV` | No | `TEST` | API and PDF environment: `TEST`, `DEMO`, `PRODUCTION` (aliases: `PRD`, `PROD`). Case and surrounding whitespace are ignored; an empty value means `TEST`. An unknown value stops gateway startup. |
| `KSEF_SDK_ENDPOINTS` | No | `safe` | Exposed SDK routes: `safe`, `all`, or a list of groups / `group/method`; invalid values stop startup. |
| `KSEF_API_PORT` | No | `8080` | Gateway API port |
| `KSEF_QR_URL` | No | Derived from `KSEF_ENV` | QR verification base URL: TEST → `https://qr-test.ksef.mf.gov.pl`, DEMO → `https://qr-demo.ksef.mf.gov.pl`, PRODUCTION/PRD → `https://qr.ksef.mf.gov.pl`. An explicit value in the PDF service takes precedence; missing/unknown environment → TEST (unknown: one-time warning). |
| `GITHUB_PAT` | Build | - | GitHub PAT with `read:packages` for CIRFMF SDK |
| `KSEF_CONTEXTS_FILE` | No | `/app/contexts.json` | Path to multi-NIP config file |
| `GATEWAY_LICENSE` | No | - | Multi-NIP license key - only needed for more than 1 NIP, see [Multi-NIP Licensing](#multi-nip-licensing) |

\* Provide exactly one: `KSEF_TOKEN`, `KSEF_CERT_PATH` + `KSEF_KEY_PATH`, or `KSEF_CERT_CONTENT` + `KSEF_KEY_CONTENT`.

### Multi-NIP Mode

To handle invoices for multiple companies, create a `contexts.json` file:

```json
[
  {
    "nip": "1234567890",
    "token": "ksef-token-for-company-A",
    "label": "Company A",
    "apiKeys": ["sha256:0000000000000000000000000000000000000000000000000000000000000000"]
  },
  {
    "nip": "0987654321",
    "certificatePath": "/app/certs/company-b.crt",
    "privateKeyPath": "/app/certs/company-b.key",
    "label": "Company B (certificate)"
  }
]
```

`GATEWAY_API_KEY` is the **administrator** key: it covers every active NIP and every enabled endpoint. Give clients separate random keys. Optional `apiKeys` entries assign their hashes to contexts; omitting the field preserves existing behavior. The zeros above are a placeholder; replace them with the actual hash. Never commit plaintext keys.

```bash
KEY=$(openssl rand -hex 32)
printf '%s' "$KEY" | shasum -a 256
# apiKeys entry: "sha256:<64 hexadecimal digits from the output>"
# the client sends the original KEY in X-Api-Key
```

A client key works only for NIPs containing its hash. One hash may belong to several NIPs, and one NIP may have several hashes. A single authorized NIP becomes the default; multiple NIPs require `X-KSeF-NIP` (missing: **400**). An unauthorized NIP in the header, invoice seller body, or KSeF number prefix returns **403** `{ "success": false, "error": "API key is not authorized for NIP ..." }`, whether or not that context exists. This also applies to received invoices whose KSeF numbers start with an external seller's NIP; downloading those requires the administrator key. Client permissions do not bypass multi-NIP license limits.

`/ksef/contexts` and `/ksef/status` show only the client's own contexts; client status omits administrative and license data. Client keys can use only SDK routes in the default `safe` set; other SDK routes return **404**, even with `KSEF_SDK_ENDPOINTS=all`. The administrator key retains all routes enabled by `KSEF_SDK_ENDPOINTS`.

Only `sha256:` followed by 64 hexadecimal digits (either case) is accepted. Hashes are compared in constant time. An invalid format prevents startup with an `apiKeys` error; changes require a restart. Missing `GATEWAY_API_KEY` still blocks every protected request (**503**), including client keys.

Contexts can mix tokens and certificates freely - see [Certificate-Based Auth](#certificate-based-auth-alternative-to-tokens) above. Each context authenticates independently, so this also covers an accounting-office style setup - **one certificate representing several client NIPs** (list the same `certificatePath`/`certificateContent` under multiple contexts, one per NIP). The gateway itself is NIP-agnostic about which certificate backs which context; whether a given certificate can actually authenticate into a NIP it doesn't belong to depends on KSeF's own authorization on that NIP's side (granting the certificate holder's identity representation/`ProxyEntity` rights) - a real KSeF-portal step per company, outside the gateway's control, and not something this project's own TEST tooling exercises (`CertGenerator` always mints a certificate scoped to the one NIP it registers).

Mount it in Docker Compose (already configured in `docker-compose.yml`):

```yaml
volumes:
  - ./contexts.json:/app/contexts.json:ro
```

The gateway auto-detects which NIP to use based on:
1. `X-KSeF-NIP` header (explicit)
2. Seller NIP from the invoice body
3. Default context (first in list or from `KSEF_NIP` env var)

The `X-KSeF-NIP` header must contain exactly one value: 10 ASCII digits without spaces (no checksum validation). Invalid format, an empty value, or multiple values returns HTTP **400** with `{ "success": false, "error": "..." }`, before consuming rate-limit budget. For the administrator key, a correctly formatted NIP without an active context in `ContextProvider` returns HTTP **404** with `No context configured for NIP ...`; this also applies to a seller NIP selected from the invoice body. Rejected NIPs do not allocate authentication state.

Check authenticated contexts: `GET /ksef/contexts`

> **Note:** `KSEF_TOKEN` + `KSEF_NIP` env vars still work for single-NIP mode. If both env vars and `contexts.json` are present, the env var context is added to the list.

### Multi-NIP Licensing

A single NIP is free, always - no license needed, no time limit. Running **more than one NIP** (accounting offices, holding groups, agencies serving several clients) requires a multi-NIP license - one-time payment, unlocks unlimited NIPs on that instance forever.

**[👉 Buy the multi-NIP license](https://sellf.techskills.academy/p/ksef-gateway-multi-nip)**

Set the license key as `GATEWAY_LICENSE`. ECDSA signatures are checked locally; public JWKS keys and the revocation list (CRL) are fetched over HTTPS (10 s timeout per request). Your license token never leaves your server.

All configured contexts remain in memory. The current license determines access at each use: the default NIP (or the first if none is selected) is always available. After license loss, additional NIPs return 404 “No context configured…” with a license explanation; license recovery restores them without a restart. Check `GET /ksef/status` for `license: {licensed, maxNips, activeNips, email, expiresAt}` (`email` is just for your own visibility - which purchase this license belongs to - it's not checked against anything). The gateway verifies the signature, the product name, the expiry date and the revocation list - the domain stored on the license is **not** checked. Changing `GATEWAY_LICENSE` on Render needs a new **deploy**: saving the variable (or a plain restart) does not reload it.

| Situation | Behavior |
|---|---|
| No `GATEWAY_LICENSE` set | Free tier - first configured NIP only |
| Valid license | Unlimited NIPs |
| License expired/revoked/malformed | Falls back to free tier (1 NIP) - never crashes the gateway |
| Sellf's license server temporarily unreachable | JWKS: uses keys cached in RAM for up to 7 days since fetch; falls back to free tier without usable cache. CRL: uses cache for up to 7 days; without it, an outage does not revoke a valid license (fail-open). |

The license refreshes every 6 hours: revocation or expiry restricts access after the next refresh (up to 6 h delay when CRL is reachable). JWKS/CRL caches exist only in RAM and do not survive restarts; starting without reachable JWKS means free tier until a successful refresh. Fresh cache TTL is 5 minutes. Tier changes are logged as `Multi-NIP license state changed: free -> licensed` or `licensed -> free`.

Advanced configuration: `GATEWAY_LICENSE_JWKS_URL` and `GATEWAY_LICENSE_REVOCATION_URL` are unsupported. Sellf HTTPS addresses are fixed; tests inject HTTP clients without network access. A custom JWKS would establish a custom trust root and allow self-issued licenses.

---

## Architecture

```
docker compose up
       |
  ksef-api:8080            ksef-pdf:3000
  (ASP.NET 10)              (Node.js + tsx)
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
| **InvoiceDownloadEndpoints** | High-level: `/ksef/invoices/received`, `/ksef/invoices/received/new`, `/ksef/invoices/issued` |
| **EndpointErrorHandling** | Shared `Guard()` - lets KSeF rate-limit/circuit-breaker/API errors surface as proper 429/503/502 (with `Retry-After`) instead of a flat 500 |
| **PDF Service** | XML to PDF via CIRFMF library + QR code generation |
| **JSON-to-XML** | `js2xml()` - XML-shaped JSON/XML conversion |

### Design Principles
- **Thin wrapper** - zero business logic, HTTP-to-SDK translation
- **Auto-adaptive** - reflection discovers endpoints from the pinned SDK version; changes require an explicit dependency update
- **Transparent crypto** - callers send plaintext, gateway encrypts
- **XML-shaped JSON** - mirrors XML 1:1, no mapping code
- **Resilient** - auth failures don't crash, retry automatically

---

## Tech Stack

| Technology | Role |
|------------|------|
| [ASP.NET 10 Minimal API](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) | Gateway HTTP layer |
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

Click the button, set `GITHUB_PAT`, `GATEWAY_API_KEY`, `KSEF_TOKEN` and `KSEF_NIP`, done. Both services (API + PDF) deploy automatically from `render.yaml`. Prefer a certificate? Leave `KSEF_TOKEN` blank, upload the cert/key as [Secret Files](https://render.com/docs/configure-environment-variables) in the dashboard, and set `KSEF_CERT_PATH`/`KSEF_KEY_PATH` to `/etc/secrets/<filename>` instead.

**Multi-NIP with several certificates on Render:** Secret Files aren't limited to one pair - upload as many cert/key files as you have companies (e.g. `company-a.crt`/`company-a.key`, `company-b.crt`/`company-b.key`, ...), *plus* your `contexts.json` itself as another Secret File, with each entry's `certificatePath`/`privateKeyPath` pointing at the matching `/etc/secrets/<filename>`. Then set `KSEF_CONTEXTS_FILE=/etc/secrets/contexts.json` (env var) instead of `KSEF_TOKEN`/`KSEF_CERT_PATH`. Requires a [multi-NIP license](#multi-nip-licensing) for more than one NIP.

### Mikrus / any VPS (one command)

The cheapest way to your own instance. One command on a fresh server stands up the whole gateway (API + PDF), generates the `GATEWAY_API_KEY` and the PDF-service secret itself, pulls the prebuilt images, and brings the stack up:

```bash
curl -fsSL https://stackpilot.techskills.academy/ksef-gateway | bash
```

Or from your own machine, with a domain and SSL certificate out of the box (Cloudflare, fully automatic):

```bash
git clone https://github.com/jurczykpawel/stackpilot && cd stackpilot
./local/deploy.sh ksef-gateway --ssh=YOUR_VPS --domain-type=cloudflare --domain=ksef.yourcompany.com
```

The gateway boots immediately; connect KSeF by adding `KSEF_TOKEN` + `KSEF_NIP` to `/opt/stacks/ksef-gateway/.env` and running `docker compose up -d`. No database, no config — the rest of the secrets generate themselves.

> **No VPS yet?** [Mikrus](https://mikr.us/?r=pavvel) is a Polish VPS from ~€1.20/mo, a perfect fit for this gateway (amd64, Docker, ~1 GB RAM is plenty). You'll have KSeF Gateway running in minutes — cheaper than any e-invoicing subscription.

### AWS Lambda

Deploy as a serverless Lambda function with Function URL (no API Gateway - avoids 29s timeout).

```bash
cd deploy/aws
sam build --build-arg GITHUB_PAT=<your-pat>
sam deploy --guided
```

See [`deploy/aws/README.md`](deploy/aws/README.md) for details.

### Azure Container Apps

The Azure template defaults to release `v0.2.1`; use `apiImage` and `pdfImage` to select a newer release or verified digests.

Deploy as managed containers - mirrors Docker Compose, zero code changes.

```bash
az deployment group create \
  --resource-group ksef-gateway \
  --template-file deploy/azure/main.bicep \
  --parameters gatewayApiKey="$GATEWAY_API_KEY" ksefToken="$KSEF_TOKEN" ksefNip="$KSEF_NIP"
```

See [`deploy/azure/README.md`](deploy/azure/README.md) for details.

| | Docker Compose | Render | AWS Lambda | Azure Container Apps |
|---|---|---|---|---|
| Setup | `docker compose up` | One-click button | SAM CLI | Azure CLI + Bicep |
| Cold start | None | ~30s (free tier) | ~3-5s | ~5-10s (or 0 with minReplicas=1) |
| Cost (low traffic) | Server cost | Free tier available | Near-zero | ~$10-15/month |
| PDF service | Included | Included | Separate deployment | Included (internal container) |
| Multi-NIP | `contexts.json` mount | Env vars | Env vars (single NIP) | Env vars or Azure Files |
| Certificate auth | File mount (`certificatePath`) | Secret Files (`certificatePath`) | Content (`certificateContent`, no file mounts) | Content (`certificateContent`, no file mounts) |

---

## Image verification

Images built by the new publication workflow pass CI for the same commit, carry
SPDX SBOM and `mode=min` provenance attestations, and are signed by digest using
keyless cosign. Older images may be unsigned. Install [cosign](https://docs.sigstore.dev/cosign/system_config/installation/).
For your release, set `TAG` and the API `DIGEST` from its `image-digests.txt` attachment,
then verify the signature before deploying:

```bash
# TAG=vX.Y.Z
# DIGEST=sha256:... (image-digests.txt)
cosign verify "ghcr.io/jurczykpawel/ksef-gateway-api@${DIGEST}" \
  --certificate-identity "https://github.com/jurczykpawel/ksef-gateway/.github/workflows/docker-publish.yml@refs/tags/${TAG}" \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

For PDF, use `ksef-gateway-pdf` and its digest. For branch images, change the identity
suffix to `@refs/heads/main` or `@refs/heads/dev`. Deploy the verified digest because
a tag can later point to another image. Keep identity, issuer and transparency-log
verification enabled.

Releases include `api.spdx.json`, `pdf.spdx.json` and `image-digests.txt` (the workflow
prepares a draft with attachments if the release does not exist yet). Each publication
also retains them in the `image-sboms` workflow artifact. The SBOM is attached to the
signed OCI image index as well; retrieve it using the verified digest:

```bash
docker buildx imagetools inspect "ghcr.io/jurczykpawel/ksef-gateway-api@${DIGEST}" \
  --format '{{ json .SBOM.SPDX }}' > api.spdx.json
```

Image license files and notices are in `/app/licenses/`.
See [SECURITY.md](SECURITY.md) for private vulnerability reporting.

## Roadmap

- [x] Auto-discovery of 60+ SDK endpoints via reflection
- [x] Token-based authentication with background refresh
- [x] `POST /ksef/send` - one-call invoice sending (XML)
- [x] `POST /ksef/send/json` - JSON invoice sending (XML-shaped format)
- [x] `GET /ksef/invoice/{nr}/pdf` - PDF with verified QR code
- [x] PDF generation with official CIRFMF library
- [x] Token generator for TEST environment
- [x] Scalar API documentation
- [x] Docker one-command setup
- [x] Client-side rate limiting (proactive, per official MF limits)
- [x] `POST /ksef/invoice` - friendly JSON with auto VAT calculation
- [x] Unit/integration tests — current results in the CI badge above (SdkReflector, RateLimiter, InvoiceXmlBuilder, XSD validation, KSeF E2E)
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

### Errors and request boundaries

API errors use `{"success":false,"data":null,"error":"…","correlationId":"…"}`. The identifier matches the response header `X-Correlation-Id` and the identifier in logs. The gateway assigns it per request; 500 responses only expose `Internal server error`, with the full exception logged. KSeF status codes and `Retry-After` remain intact. Invalid JSON returns 400.

`POST /ksef/invoice` validates before KSeF authentication or SDK calls: required parties, addresses, items, invoice number, valid `YYYY-MM-DD` dates, NIPs (10 ASCII digits), quantity > 0, price >= 0 and VAT 0/5/8/23. A 400 response includes `error: "Validation failed"` and `errors: [{"field":"items.0.quantity","message":"Must be greater than zero"}]`. Missing strings and objects identify the specific field. JSON syntax/type errors or missing required numeric properties detected during deserialization identify `body` as the field.

`PDF_SERVICE_TIMEOUT_SECONDS` bounds sending and reading PDF responses (default 60 s, timeout → 504). Client disconnects cancel both KSeF invoice retrieval and the PDF request. The PDF service permits `PDF_MAX_CONCURRENT_RENDERS` concurrent renders (default 2); excess requests receive 503 with `Retry-After: 1`. Slots are released after rendering settles, including errors; disconnects cannot interrupt synchronous generator work.

## Troubleshooting and maintenance

| Symptom | Check |
|---|---|
| Invoice `400` | Read field errors; use the complete DTO in `examples/invoice-simple.json`. |
| `401` | Send `X-Api-Key` matching the server's `GATEWAY_API_KEY`; a KSeF token does not replace it. |
| `403` | Check the allowlist and proxy header overwrite rule (`TRUSTED_PROXY_HEADER`); origin requires the correct proxy secret. |
| `429` | Wait at least `Retry-After`, with backoff and jitter. Limits are in one instance's RAM; replicas do not share budgets. |
| `503` | Missing `GATEWAY_API_KEY`, KSeF authentication failure or open circuit breaker: check logs, `/ksef/status` and `Retry-After` when present. `/health` does not prove KSeF/PDF readiness. |
| PDF/Render timeout or `502` | PDF can wake independently of the API. Check its `/health`, `PDF_SERVICE_URL`, matching `PDF_SERVICE_SECRET` in both services and `PDF_SERVICE_TIMEOUT_SECONDS` (default 60 s). On Render free use the public PDF HTTPS URL; private networking cannot reach that service. |
| Multi-NIP falls back to 1 NIP | Check the license and HTTPS access to Sellf JWKS/CRL. Cache is in RAM and lost on restart; startup without JWKS uses the free tier. A CRL outage alone does not revoke a valid license. |
| NuGet restore `401`/`403` | `GITHUB_PAT` needs `read:packages` and access to CIRFMF packages; use the repository's `nuget.config`. Keep PATs out of issues and logs. |
| Wrong environment or missing SDK route | Match credentials to `KSEF_ENV`: `TEST`, `DEMO`, `PRODUCTION`. Set it in PDF too (QR); optional `KSEF_QR_URL` overrides the QR base. `KSEF_SDK_ENDPOINTS=safe` hides administration; explicitly select needed methods. Invalid configuration stops startup. |

AWS `PdfServiceUrl` maps to `PDF_SERVICE_URL`: supply a reachable HTTP(S) PDF service URL. An empty value **does not disable routes** — conversion/PDF calls fail. Azure's template sets the internal PDF service URL. For a public PDF service, configure `PDF_SERVICE_SECRET` in both API and PDF (on AWS this requires adding environment configuration; the SAM parameters do not expose this secret).

### Upgrade and rollback

Before upgrading, record **both** image digests and back up configuration. In a Compose override set `image: ghcr.io/jurczykpawel/ksef-gateway-api@sha256:…` and the matching `ksef-gateway-pdf@sha256:…`, using full, actual digests of a tested release. Run `docker compose pull ksef-api ksef-pdf` and `docker compose up -d --no-build ksef-api ksef-pdf`; verify `/health`, authenticated `/ksef/status` and a sample PDF. In Azure pass the pinned references as `apiImage`/`pdfImage`; for Lambda pin the deployed ECR image. To roll back, restore previous digests and compatible configuration, redeploy and verify readiness. After a send timeout, determine whether KSeF accepted the invoice before retrying.

### Rotation and backups

The gateway accepts one value for each secret. Schedule a short maintenance window, pause automation and generate replacements outside logs. Change `GATEWAY_API_KEY` in the service and client credentials; change `PDF_SERVICE_SECRET` in both API and PDF; change `TRUSTED_PROXY_SECRET` in origin and the proxy header overwrite rule. Recreate containers or redeploy to load the environment. Verify the new key (200 on `/ksef/status`), rejection of the old key (401), origin protection (403) and PDF before resuming callers. For certificate rotation, obtain a replacement for the correct environment and permissions, replace certificate/key/password together, restart and confirm authentication in `/ksef/status`; then explicitly revoke the old certificate in KSeF. Issuing a new certificate does not revoke the old one.

Keep encrypted backups of `.env`/deployment secrets, `contexts.json`, proxy configuration, image digests, certificates and keys (with passwords stored securely). The gateway has no database to back up. For n8n, export workflows and back up persistent data/credentials together with its encryption key; JSON export alone does not preserve the polling checkpoint in static data. Verify checkpoint and deduplication before re-enabling schedules after restoration. Periodically test restoration on TEST.

## Development

Full regression: `./scripts/test-full.sh --all` (unit and KSeF TEST integration, PDF, n8n, landing build). The default pre-push `./scripts/test-full.sh` is unchanged. Requires Docker, Node 22.23.4+ and TEST credentials in `.env`. See [CONTRIBUTING.md — Testy i regresja](CONTRIBUTING.md#testy-i-regresja) for the coverage matrix, explicit gaps, KSeF quotas and CI layers. Nightly regression runs once daily; published images pass a smoke test before release tags are promoted.
