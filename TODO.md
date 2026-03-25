# Implementation Plan - v0.2.0

## Feature 1: CI/CD (GitHub Actions)
**Branch:** `feat/ci`

Steps:
1. Create `.github/workflows/ci.yml`
2. Jobs: dotnet restore → build → test (ksef-api)
3. Docker compose build (both containers)
4. GITHUB_PAT as repository secret for CIRFMF packages
5. Trigger: push + PR to dev and main
6. Badge in README

## Feature 2: Unit Tests
**Branch:** `feat/tests`

Tests to write:
- `SdkReflectorTests` - discovers all interfaces, filters infra params, deduplicates overloads, kebab-case naming
- `EndpointMapperTests` - registers correct routes, handles DI resolution, JSON body parsing
- `TokenManagerTests` - mock SDK auth flow, refresh logic, error recovery, resilient startup
- `RateLimitTests` - sliding window accuracy, per-second/minute/hour limits, concurrent access, cleanup
- `KsefRateLimitsTests` - correct limit selection per path pattern

Framework: xUnit + Moq + FluentAssertions (already in .csproj)

## Feature 3: POST /ksef/invoice (friendly JSON)
**Branch:** `feat/friendly-json`

### Endpoint
`POST /ksef/invoice` - accepts intuitive JSON, builds FA(3) XML internally.

### JSON Schema (input)
```json
{
  "invoiceNumber": "FV/001/2026",
  "issueDate": "2026-03-24",
  "issuePlace": "Warszawa",
  "saleDate": "2026-03-24",
  "currency": "PLN",
  "type": "VAT",
  "seller": {
    "nip": "1234567890",
    "name": "Firma sp. z o.o.",
    "address": { "street": "ul. Testowa 1", "city": "00-001 Warszawa", "country": "PL" },
    "email": "firma@example.com",
    "phone": "123456789"
  },
  "buyer": {
    "nip": "0987654321",
    "name": "Klient sp. z o.o.",
    "address": { "street": "ul. Kupiecka 2", "city": "00-002 Warszawa", "country": "PL" }
  },
  "items": [
    {
      "name": "Usługa konsultingowa",
      "unit": "szt.",
      "quantity": 1,
      "unitPrice": 100.00,
      "vatRate": 23
    }
  ],
  "payment": {
    "paid": true,
    "date": "2026-03-24",
    "method": "transfer"
  }
}
```

### Implementation
1. Define `InvoiceRequest` C# model with the above structure
2. `InvoiceXmlBuilder` class: `InvoiceRequest` → FA(3) XML string
   - Auto-calculates: P_11 (net per line), P_13_1 (total net), P_14_1 (total VAT), P_15 (total gross)
   - Auto-fills: Naglowek (KodFormularza, WariantFormularza, DataWytworzeniaFa, SystemInfo)
   - Auto-fills: Adnotacje (standard defaults for simple invoices)
   - Maps payment.method: "transfer"→6, "cash"→1, "card"→2, etc.
3. Endpoint in WorkflowEndpoints: deserialize → build XML → forward to existing /ksef/send logic
4. Tests: `InvoiceXmlBuilderTests`
   - Correct XML structure for minimal invoice
   - Multi-item invoice with correct totals
   - VAT calculation accuracy
   - All payment methods mapped correctly
   - Missing required fields → clear error message
   - Round-trip: friendly JSON → XML → xml2js → verify values match

### Test strategy for transformation
- Unit tests: `InvoiceXmlBuilder` input → output XML string → parse with xml2js → assert values
- Snapshot tests: known input → expected XML (catch regressions)
- Validation tests: incomplete/invalid input → descriptive errors
- E2E test (optional): send friendly JSON to KSeF TEST → accepted

## Feature 4: JSON Schema auto-generation (nice-to-have)
**Branch:** `feat/json-schema`

- Script to convert FA(3) XSD → JSON Schema using xsd2jsonschema
- Run in CI, output to `schemas/fa3.schema.json`
- Used for validation in `/ksef/send/json` endpoint
- Exposed at `GET /schemas/fa3.json` for clients

## Priority Order
1. CI/CD (unblocks everything else)
2. Tests (validates what we have)
3. Friendly JSON (biggest user value)
4. JSON Schema (nice-to-have)
