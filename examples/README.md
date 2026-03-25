# Examples

Three ways to send the same invoice to KSeF. All produce the same result.

## Setup

```bash
# 1. Start gateway
docker compose up

# 2. Replace SELLER_NIP in example files with your NIP from .env
# Or just run the test script which does it automatically:
KSEF_NIP=3270165758 ./examples/test.sh
```

## Option 1: XML (`POST /ksef/send`)

Full control. You provide complete FA(3) XML.

```bash
curl -X POST http://localhost:8080/ksef/send \
  -H "Content-Type: application/xml" \
  -d @examples/invoice.xml
```

**File:** [invoice.xml](invoice.xml)

**When to use:** Your system already generates FA(3) XML, or you need features not covered by the other formats (corrections, attachments, special annotations).

## Option 2: JSON - xml-js format (`POST /ksef/send/json`)

JSON structure mirrors XML 1:1. Zero-maintenance format - when FA(3) XSD changes, JSON structure changes automatically. No mapping code to maintain.

```bash
curl -X POST http://localhost:8080/ksef/send/json \
  -H "Content-Type: application/json" \
  -d @examples/invoice.json
```

**File:** [invoice.json](invoice.json)

**When to use:** You want JSON but need access to ALL FA(3) fields. Structure follows [xml-js compact format](https://www.npmjs.com/package/xml-js#compact-notation) - every XML element becomes `{ "_text": "value" }`, attributes go in `{ "_attributes": { ... } }`.

## Option 3: Friendly JSON (`POST /ksef/invoice`)

Intuitive structure with auto-calculated totals. Gateway builds the XML for you.

```bash
curl -X POST http://localhost:8080/ksef/invoice \
  -H "Content-Type: application/json" \
  -d @examples/invoice-simple.json
```

**File:** [invoice-simple.json](invoice-simple.json)

**When to use:** Standard VAT invoices. Gateway auto-calculates net/VAT/gross totals per VAT rate, fills headers and annotations. Covers ~90% of use cases.

**Auto-calculated fields:**
- `P_11` (net value per line) = quantity x unitPrice
- `P_13_1` / `P_14_1` (net/VAT totals for 23%) - grouped by VAT rate
- `P_15` (gross total)
- `Naglowek` (form code, variant, timestamp)
- `Adnotacje` (standard defaults)

**Payment methods:** `transfer` (6), `cash` (1), `card` (2), `check` (4), `credit` (5), `mobile` (7)

## Response

All three endpoints return the same response:

```json
{
  "success": true,
  "data": {
    "ksefNumber": "1234567890-20260324-ABC123000000-05",
    "status": "accepted",
    "statusDescription": "Sukces",
    "sessionReferenceNumber": "20260324-SO-...",
    "invoiceReferenceNumber": "20260324-EE-..."
  }
}
```

## After sending

```bash
# Download invoice XML from KSeF
curl http://localhost:8080/ksef/invoice/{ksefNumber}

# Download PDF with QR code (one call)
curl -o faktura.pdf http://localhost:8080/ksef/invoice/{ksefNumber}/pdf
```

## Multi-NIP

If you manage multiple firms, the gateway auto-selects the context by seller NIP from the invoice. Or set it explicitly:

```bash
curl -X POST http://localhost:8080/ksef/send \
  -H "Content-Type: application/xml" \
  -H "X-KSeF-NIP: 1234567890" \
  -d @invoice.xml
```

List configured contexts:
```bash
curl http://localhost:8080/ksef/contexts
```

## Test script

Runs all examples end-to-end:

```bash
KSEF_NIP=3270165758 ./examples/test.sh
```

Expected output:

```
=== KSeF Gateway Test ===

1. Health check
{ "status": "ok", "discoveredEndpoints": 60, "authenticated": true, ... }

2. Gateway status
{ "success": true, "data": { "mode": "single-nip", "contexts": [...] } }

3. Configured contexts
{ "success": true, "data": [{ "nip": "3270165758", "authenticated": true }] }

4. Send invoice (XML)
   POST http://localhost:8080/ksef/send
{ "success": true, "data": { "ksefNumber": "3270165758-...", "status": "accepted" } }

5. Send invoice (JSON - xml-js format)
   POST http://localhost:8080/ksef/send/json
{ "success": true, "data": { "ksefNumber": "3270165758-...", "status": "accepted" } }

6. Send invoice (friendly JSON)
   POST http://localhost:8080/ksef/invoice
{ "success": true, "data": { "ksefNumber": "3270165758-...", "status": "accepted" } }

7. Download invoice XML
   GET http://localhost:8080/ksef/invoice/3270165758-...
<?xml version="1.0" encoding="utf-8"?>
<Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
   ...

8. Download invoice PDF
   GET http://localhost:8080/ksef/invoice/3270165758-.../pdf
   Saved to /tmp/ksef-test-invoice.pdf (32100 bytes)

=== Done ===
```

Each invoice send (steps 4-6) takes ~30-60 seconds because the gateway opens a session, encrypts, sends, closes, and polls for the KSeF number.

The PDF (step 8) contains the official KSeF layout with QR verification code.
