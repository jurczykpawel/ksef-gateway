# AWS Lambda Deployment

Deploy KSeF Gateway as an AWS Lambda function with Function URL.

## Prerequisites

- AWS CLI configured
- AWS SAM CLI (`brew install aws-sam-cli`)
- Docker (for building the Lambda container image)
- `GITHUB_PAT` with `read:packages` scope (for CIRFMF SDK)

## Deploy

```bash
# Build
sam build --build-arg GITHUB_PAT=<your-pat>

# Deploy (guided, first time)
sam deploy --guided

# Deploy (subsequent)
sam deploy --parameter-overrides \
  KSeFToken=<your-token> \
  KSeFNip=<your-nip> \
  KSeFEnv=TEST
```

The output includes the Function URL - use it as your API base URL.

### Certificate-based auth instead of a token

Lambda has no persistent file storage, so certificate auth here takes the cert/key as **PEM content**, not a file path. Both parameters use `NoEcho: true` (hidden from the CloudFormation console/CLI output), same as `KSeFToken`:

```bash
sam deploy --parameter-overrides \
  KSeFCertContent="$(cat company.crt)" \
  KSeFKeyContent="$(cat company.key)" \
  KSeFKeyPassword=<only-if-encrypted> \
  KSeFNip=<your-nip> \
  KSeFEnv=PRODUCTION
```

Leave `KSeFToken` unset in that case (both work, but pick one - see README "Certificate-Based Auth"). `$(cat ...)` preserves the real newlines the PEM format needs - don't flatten it to a single line.

## Architecture

```
Client → Lambda Function URL → KSeF Gateway (.NET 9) → KSeF API
                                     ↓ (optional)
                               PDF Service URL
```

Uses Lambda Function URL instead of API Gateway to avoid the 29-second integration timeout (invoice send polls KSeF for up to 60s).

## PDF Service

The PDF service (`ksef-pdf`) is a separate Node.js container. Options:

1. **Skip it** - leave `PdfServiceUrl` empty, PDF endpoints will return errors
2. **AWS App Runner** - deploy `pdf-service/` as a container (~$5/month)
3. **Separate Lambda** - deploy as a Node.js Lambda behind a Function URL

Set `PdfServiceUrl` to the deployed PDF service URL.

## Notes

- **Cold start**: ~3-5s for the .NET 9 container Lambda
- **TokenPool**: re-authenticates with KSeF on cold start (~2s), then caches in memory for the instance lifetime
- **Rate limiting**: per-instance only (acceptable for serverless workloads)
- **Multi-NIP**: use `KSEF_TOKEN` + `KSEF_NIP` env vars (single NIP). For multi-NIP, bundle `contexts.json` in the image or mount from S3
- **Timeout**: 900s (15 min) Lambda timeout, more than enough for the 60s polling loop
