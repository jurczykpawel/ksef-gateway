# Implementation Plan

## v0.2.0 - DONE

- [x] CI/CD (GitHub Actions) - build, test, Docker
- [x] Unit tests (57 total) - SdkReflector, RateLimiter, InvoiceXmlBuilder, XSD validation, ContextResolver
- [x] POST /ksef/invoice (friendly JSON) with auto VAT calculation
- [x] XSD validation tests (catches schema changes)
- [x] Client-side rate limiting (proactive, per MF docs)
- [x] Multi-NIP support (TokenPool, ContextProvider, ContextResolver)

## Receiving invoices - DONE

- [x] GET /ksef/invoices/received - browse invoices you received (buyer role), no KSeF number needed upfront
- [x] GET /ksef/invoices/received/new - stateless polling cursor (PermanentStorage HWM) for sync/notification workflows
- [x] Shared EndpointErrorHandling.Guard() - KsefRateLimitException/KsefCircuitBreakerOpenException/KsefApiException now surface as proper 429/503/502 (with Retry-After) instead of a flat 500, across every handler

## Future

- [ ] JSON Schema auto-generated from XSD (validation + Scalar docs)
- [ ] AWS Lambda deployment
- [ ] Invoice corrections (KOR type in friendly JSON)
- [ ] Batch sending via friendly JSON (multiple invoices in one call)
- [ ] High-volume invoice sync via invoice-download/export-invoices (async batch export + AES decrypt + unzip) - the friendly received-invoices endpoints use the simpler query/metadata call, fine for low volume but capped at 20 req/h by KSeF
