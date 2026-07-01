# Azure Container Apps Deployment

Deploy KSeF Gateway as Azure Container Apps - zero code changes, mirrors Docker Compose.

## Prerequisites

- Azure CLI (`brew install azure-cli`)
- `az login` completed
- Container images pushed to a registry (GHCR, ACR, or Docker Hub)

## Deploy

```bash
# Create resource group
az group create --name ksef-gateway --location westeurope

# Deploy
az deployment group create \
  --resource-group ksef-gateway \
  --template-file main.bicep \
  --parameters \
    gatewayApiKey=$(openssl rand -hex 32) \
    ksefToken=<your-token> \
    ksefNip=<your-nip> \
    ksefEnv=TEST \
    apiImage=ghcr.io/jurczykpawel/ksef-gateway-api:latest \
    pdfImage=ghcr.io/jurczykpawel/ksef-gateway-pdf:latest
```

The output includes the public API URL. **Keep the `gatewayApiKey` value** - the gateway has no other caller-facing auth (see README "Security"), so every request needs it back as the `X-Api-Key` header.

### Certificate-based auth instead of a token

Container Apps has no convenient way to mount an arbitrary file, so certificate auth here takes the cert/key as **PEM content**, not a file path - both land in Container Apps' own `secrets` store (same mechanism as `ksefToken`), not a plain env var:

```bash
az deployment group create \
  --resource-group ksef-gateway \
  --template-file main.bicep \
  --parameters \
    gatewayApiKey=$(openssl rand -hex 32) \
    ksefCertContent="$(cat company.crt)" \
    ksefKeyContent="$(cat company.key)" \
    ksefKeyPassword=<only-if-encrypted> \
    ksefNip=<your-nip> \
    ksefEnv=PRODUCTION \
    apiImage=ghcr.io/jurczykpawel/ksef-gateway-api:latest \
    pdfImage=ghcr.io/jurczykpawel/ksef-gateway-pdf:latest
```

Leave `ksefToken` unset in that case (both work, but pick one - see README "Certificate-Based Auth").

## Architecture

```
Client → Azure Container Apps (ksef-api:8080) → KSeF API
                    ↓ (internal)
         Azure Container Apps (ksef-pdf:3000)
```

Mirrors the Docker Compose setup exactly. Both containers run in the same Container Apps Environment with internal networking.

## Notes

- **Scale to zero**: both apps scale to 0 replicas when idle (cost-effective)
- **Request timeout**: 240s (covers the 60s KSeF polling loop)
- **TokenPool**: works normally (real container, not serverless)
- **PDF service**: runs as internal container, accessible at `http://ksef-pdf`
- **Cold start**: ~5-10s when scaling from zero. Set `minReplicas: 1` to avoid
- **Multi-NIP**: add `KSEF_CONTEXTS_FILE` env var and mount `contexts.json` via Azure Files
- **Cost**: ~$10-15/month at low traffic with scale-to-zero

## Using Azure Container Registry

If you want to use ACR instead of GHCR:

```bash
# Create ACR
az acr create --name ksefgateway --resource-group ksef-gateway --sku Basic

# Build & push
az acr build --registry ksefgateway --image ksef-api:latest -f src/KSeFGateway.Api/Dockerfile src/
az acr build --registry ksefgateway --image ksef-pdf:latest -f pdf-service/Dockerfile pdf-service/

# Deploy with ACR images
az deployment group create ... --parameters apiImage=ksefgateway.azurecr.io/ksef-api:latest ...
```
