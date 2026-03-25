# Implementation Plan

## v0.2.0 - DONE

- [x] CI/CD (GitHub Actions) - build, test, Docker
- [x] Unit tests (57 total) - SdkReflector, RateLimiter, InvoiceXmlBuilder, XSD validation, ContextResolver
- [x] POST /ksef/invoice (friendly JSON) with auto VAT calculation
- [x] XSD validation tests (catches schema changes)
- [x] Client-side rate limiting (proactive, per MF docs)
- [x] Multi-NIP support (TokenPool, ContextProvider, ContextResolver)

## Future

- [ ] JSON Schema auto-generated from XSD (validation + Scalar docs)
- [ ] AWS Lambda deployment
- [ ] Invoice corrections (KOR type in friendly JSON)
- [ ] Batch sending via friendly JSON (multiple invoices in one call)
