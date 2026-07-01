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
- [x] GET /ksef/invoices/issued - browse invoices you issued (seller role), same shape as /received, no polling variant
- [x] Shared EndpointErrorHandling.Guard() - KsefRateLimitException/KsefCircuitBreakerOpenException/KsefApiException now surface as proper 429/503/502 (with Retry-After) instead of a flat 500, across every handler

## Certificate-based auth - DONE

- [x] KsefContext supports certificatePath+privateKeyPath(+privateKeyPassword) as an alternative to token
- [x] TokenPool authenticates via IAuthCoordinator.AuthAsync() + XAdES signing (SignatureService.Sign) when a context uses a certificate
- [x] Supports both plain and password-encrypted private key PEMs (X509Certificate2.CreateFromPemFile / CreateFromEncryptedPemFile)
- [x] Verified end-to-end against live TEST KSeF API with a self-signed cert (both key variants) - auth + a read-only endpoint
- [x] tools/CertGenerator - one-command TEST cert generator (self-signed, verifies auth, exports PEM), mirrors TokenGenerator
- [x] CI job "Certificate auth integration test" - generates a cert, starts a gateway with it, asserts /ksef/status authenticated=true
- [x] certificateContent+privateKeyContent (PEM as content, not a path) for platforms without file mounts - verified end-to-end on TEST (plain + encrypted key)
- [x] render.yaml / deploy/azure/main.bicep / deploy/aws/template.yaml all expose the certificate option, not just KSEF_TOKEN (path+Secret Files for Render, content-as-secret for Azure/AWS)

## Gateway API key auth - DONE

- [x] ApiKeyMiddleware - fail-closed X-Api-Key check on every request except /health (gateway previously had zero caller-facing auth - anyone reaching a deployed instance could send/read real invoices)
- [x] GATEWAY_API_KEY wired into docker-compose.yml, .env.example, Bruno (collection.bru shared header), CI (both integration jobs), and all three deploy templates (render.yaml, azure/main.bicep, aws/template.yaml)
- [x] Unit tests + live integration tests (401 without key, 401 wrong key, 200 correct key, /health exempt)

## Multi-NIP licensing - DONE

- [x] GATEWAY_LICENSE - offline ECDSA verification against Sellf's JWKS (product `ksef-gateway-multi-nip`), k-anonymity revocation check
- [x] Free tier = 1 NIP forever, no license needed; licensed = unlimited NIPs
- [x] ContextProvider caps configured NIPs at LicenseService.MaxNips at startup - keeps the default NIP, never crashes on an expired/missing license
- [x] License info surfaced in GET /ksef/status

## Future

- [ ] JSON Schema auto-generated from XSD (validation + Scalar docs)
- [ ] AWS Lambda deployment
- [ ] Invoice corrections (KOR type in friendly JSON)
- [ ] Batch sending via friendly JSON (multiple invoices in one call)
- [ ] High-volume invoice sync via invoice-download/export-invoices (async batch export + AES decrypt + unzip) - the friendly received-invoices endpoints use the simpler query/metadata call, fine for low volume but capped at 20 req/h by KSeF
