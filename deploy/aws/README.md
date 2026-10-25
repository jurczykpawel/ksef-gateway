# AWS Lambda Deployment

Deploy KSeF Gateway as an AWS Lambda function with Function URL.

## Prerequisites

- AWS CLI configured
- AWS SAM CLI (`brew install aws-sam-cli`)
- Docker (for building the Lambda container image)

## Deploy

Generate and securely store `GATEWAY_API_KEY` before deployment (`openssl rand -hex 32`), then export it in the deployment shell. Reuse it for client `X-Api-Key` headers.

```bash
# Build
sam build

# Deploy (guided, first time)
sam deploy --guided

# Deploy (subsequent)
sam deploy --parameter-overrides \
  GatewayApiKey="$GATEWAY_API_KEY" \
  KSeFToken=<your-token> \
  KSeFNip=<your-nip> \
  KSeFEnv=TEST \
  PdfServiceUrl="$PDF_SERVICE_URL"
```

The output includes the Function URL - use it as your API base URL. **Keep the `GatewayApiKey` value** - `FunctionUrlConfig.AuthType` is `NONE` (no AWS IAM auth on the URL itself), so this application-level key is the only thing protecting the endpoint. See README "Security".

### Certificate-based auth instead of a token

Lambda has no persistent file storage, so certificate auth here takes the cert/key as **PEM content**, not a file path. All of these use `NoEcho: true` (hidden from the CloudFormation console/CLI output), same as `KSeFToken`:

```bash
sam deploy --parameter-overrides \
  GatewayApiKey="$GATEWAY_API_KEY" \
  KSeFCertContent="$(cat company.crt)" \
  KSeFKeyContent="$(cat company.key)" \
  KSeFKeyPassword=<only-if-encrypted> \
  KSeFNip=<your-nip> \
  KSeFEnv=PRODUCTION \
  PdfServiceUrl="$PDF_SERVICE_URL"
```

Leave `KSeFToken` unset in that case (both work, but pick one - see README "Certificate-Based Auth"). `$(cat ...)` preserves the real newlines the PEM format needs - don't flatten it to a single line.

## Architecture

```
Client → Lambda Function URL → KSeF Gateway (.NET 10) → KSeF API
                                     ↓ (optional)
                               PDF Service URL
```

Uses Lambda Function URL instead of API Gateway to avoid the 29-second integration timeout (invoice send polls KSeF for up to 60s).

## PDF Service

The PDF service (`ksef-pdf`) is a separate Node.js container. Options:

1. **Without it** - PDF/conversion routes remain registered but fail; an empty `PdfServiceUrl` does not disable endpoints
2. **AWS App Runner** - deploy `pdf-service/` as a container (~$5/month)
3. **Separate Lambda** - deploy as a Node.js Lambda behind a Function URL

Set `PdfServiceUrl` to the reachable HTTP(S) URL of the deployed PDF service. A missing `PDF_SERVICE_URL` in API configuration defaults to `http://ksef-pdf:3000`; the SAM template explicitly passes an empty string by default, which cannot be used as a service address. For public PDF hosting set matching `PDF_SERVICE_SECRET` in both services (requires adding API environment configuration to the template).

## Notes

- **Cold start**: ~3-5s for the .NET 10 container Lambda
- **TokenPool**: re-authenticates with KSeF on cold start (~2s), then caches in memory for the instance lifetime
- **Rate limiting**: per-instance only; coordinate aggregate traffic across Lambda instances in callers
- **Multi-NIP**: use `KSEF_TOKEN` + `KSEF_NIP` env vars (single NIP). For multi-NIP, bundle `contexts.json` in the image or mount from S3
- **Timeout**: 900s (15 min) Lambda timeout, more than enough for the 60s polling loop
