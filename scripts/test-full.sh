#!/usr/bin/env bash
# Full test suite: unit + integration against a live gateway.
# Used as pre-push hook (via pre-commit) and for manual runs.
#
# Requires: Docker, .env with GITHUB_PAT + KSEF_TOKEN + KSEF_NIP (TEST env).
# Usage: ./scripts/test-full.sh
# Skip:  SKIP_INTEGRATION=1 git push

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker-compose.yml"
ENV_FILE="$REPO_ROOT/.env"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
log()  { echo -e "${NC}[test-full] $*"; }
ok()   { echo -e "${GREEN}[test-full] $*${NC}"; }
warn() { echo -e "${YELLOW}[test-full] $*${NC}"; }
fail() { echo -e "${RED}[test-full] $*${NC}"; }

# ── Guards ────────────────────────────────────────────────────────────────────

if [ "${SKIP_INTEGRATION:-0}" = "1" ]; then
  warn "SKIP_INTEGRATION=1 set — skipping integration tests"
  exit 0
fi

if [ ! -f "$ENV_FILE" ]; then
  warn ".env not found — skipping integration tests (run: cp .env.example .env)"
  exit 0
fi

if ! docker info > /dev/null 2>&1; then
  warn "Docker not running — skipping integration tests"
  exit 0
fi

# ── Load credentials ──────────────────────────────────────────────────────────

GITHUB_PAT=$(grep '^GITHUB_PAT=' "$ENV_FILE" | cut -d= -f2-)
KSEF_NIP=$(grep '^KSEF_NIP=' "$ENV_FILE" | cut -d= -f2-)

if [ -z "$GITHUB_PAT" ] || [ -z "$KSEF_NIP" ]; then
  warn "GITHUB_PAT or KSEF_NIP missing in .env — skipping integration tests"
  exit 0
fi

# ── Gateway lifecycle ─────────────────────────────────────────────────────────

GATEWAY_URL="http://localhost:8080"
STARTED_GATEWAY=0

gateway_running() {
  curl -sf "$GATEWAY_URL/health" > /dev/null 2>&1
}

if gateway_running; then
  log "Gateway already running at $GATEWAY_URL"
else
  log "Starting gateway..."
  docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up -d --build --quiet-pull > /dev/null 2>&1
  STARTED_GATEWAY=1

  log "Waiting for gateway to be healthy..."
  for i in $(seq 1 30); do
    if gateway_running; then break; fi
    sleep 2
  done

  if ! gateway_running; then
    fail "Gateway failed to start. Check: docker compose logs"
    docker compose -f "$COMPOSE_FILE" down > /dev/null 2>&1
    exit 1
  fi
fi

ok "Gateway ready at $GATEWAY_URL"

# ── Cleanup on exit ───────────────────────────────────────────────────────────

cleanup() {
  if [ "$STARTED_GATEWAY" = "1" ]; then
    log "Stopping gateway..."
    docker compose -f "$COMPOSE_FILE" down > /dev/null 2>&1
  fi
}
trap cleanup EXIT

# ── Run tests ─────────────────────────────────────────────────────────────────

# Clean stale build artifacts to avoid cross-container cache conflicts
find "$REPO_ROOT/src" -name "obj" -type d -exec rm -rf {} + 2>/dev/null || true

log "Running full test suite (unit + integration)..."

docker run --rm \
  --add-host=host.docker.internal:host-gateway \
  -e GITHUB_PAT="$GITHUB_PAT" \
  -e KSEF_NIP="$KSEF_NIP" \
  -e GATEWAY_URL="http://host.docker.internal:8080" \
  -v "$REPO_ROOT/src:/src" \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  bash -c "
    set -euo pipefail
    cd /src
    dotnet restore KSeFGateway.Api.Tests/KSeFGateway.Api.Tests.csproj --configfile nuget.config -q
    dotnet test KSeFGateway.Api.Tests/KSeFGateway.Api.Tests.csproj -c Release --no-restore --verbosity normal
  "

ok "All tests passed ✓"
