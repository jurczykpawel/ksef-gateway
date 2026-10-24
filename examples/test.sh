#!/usr/bin/env bash
# KSeF Gateway - test script
# Usage: ./examples/test.sh
#
# Prerequisites:
#   1. Gateway running: docker compose up
#   2. Authenticated: check curl http://localhost:8080/health
#
# Replace demo NIP 3202004132 in example files with your actual NIP from .env

set -euo pipefail

API="http://localhost:8080"
NIP="${KSEF_NIP:?Set KSEF_NIP}"
: "${GATEWAY_API_KEY:?Set GATEWAY_API_KEY}"
RUN_ID="$(date +%s)"

echo "=== KSeF Gateway Test ==="
echo ""

# Health check
echo "1. Health check"
curl -s "$API/health" | python3 -m json.tool
echo ""

# Status
echo "2. Gateway status"
curl -H "X-Api-Key: $GATEWAY_API_KEY" -s "$API/ksef/status" | python3 -m json.tool
echo ""

# Contexts (multi-NIP)
echo "3. Configured contexts"
curl -H "X-Api-Key: $GATEWAY_API_KEY" -s "$API/ksef/contexts" | python3 -m json.tool
echo ""

# ─── Send invoice via XML ───────────────────────────────────────
echo "4. Send invoice (XML)"
echo "   POST $API/ksef/send"
XML=$(cat examples/invoice.xml | sed "s/3202004132/$NIP/g; s#FV/EXAMPLE/001/2026#FV/$RUN_ID/XML#g")
RESULT=$(echo "$XML" | curl -H "X-Api-Key: $GATEWAY_API_KEY" -s -X POST "$API/ksef/send" \
  -H "Content-Type: application/xml" \
  --max-time 120 \
  --data-binary @-)
echo "$RESULT" | python3 -m json.tool
KSEF_NR=$(echo "$RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('data',{}).get('ksefNumber',''))" 2>/dev/null || echo "")
echo ""

# ─── Send invoice via JSON (xml-js format) ──────────────────────
echo "5. Send invoice (JSON - xml-js format)"
echo "   POST $API/ksef/send/json"
JSON=$(cat examples/invoice.json | sed "s/3202004132/$NIP/g; s#FV/EXAMPLE/001/2026#FV/$RUN_ID/JSON#g")
echo "$JSON" | curl -H "X-Api-Key: $GATEWAY_API_KEY" -s -X POST "$API/ksef/send/json" \
  -H "Content-Type: application/json" \
  --max-time 120 \
  -d @- | python3 -m json.tool
echo ""

# ─── Send invoice via friendly JSON ────────────────────────────
echo "6. Send invoice (friendly JSON)"
echo "   POST $API/ksef/invoice"
SIMPLE=$(cat examples/invoice-simple.json | sed "s/3202004132/$NIP/g; s#FV/EXAMPLE/001/2026#FV/$RUN_ID/DTO#g")
echo "$SIMPLE" | curl -H "X-Api-Key: $GATEWAY_API_KEY" -s -X POST "$API/ksef/invoice" \
  -H "Content-Type: application/json" \
  --max-time 120 \
  -d @- | python3 -m json.tool
echo ""

# ─── Download invoice XML ──────────────────────────────────────
if [ -n "$KSEF_NR" ] && [ "$KSEF_NR" != "null" ]; then
  echo "7. Download invoice XML"
  echo "   GET $API/ksef/invoice/$KSEF_NR"
  curl -H "X-Api-Key: $GATEWAY_API_KEY" -s "$API/ksef/invoice/$KSEF_NR" | head -5
  echo "   ..."
  echo ""

  echo "8. Download invoice PDF"
  echo "   GET $API/ksef/invoice/$KSEF_NR/pdf"
  curl -H "X-Api-Key: $GATEWAY_API_KEY" -s -o /tmp/ksef-test-invoice.pdf "$API/ksef/invoice/$KSEF_NR/pdf"
  echo "   Saved to /tmp/ksef-test-invoice.pdf ($(wc -c < /tmp/ksef-test-invoice.pdf) bytes)"
  echo ""
else
  echo "7-8. Skipped (no KSeF number from step 4)"
  echo ""
fi

echo "=== Done ==="
