# n8n Integration Workflows

Example n8n workflows for connecting e-commerce platforms to KSeF Gateway - both sending invoices out and pulling received ones in.

Each workflow ships in two languages - the logic is byte-identical, only the sticky-note text differs:

- `*.json` - English sticky notes
- `*-PL.json` - polskie sticky notes (node names and code stay English either way)

Import whichever file matches your preference (n8n → Workflows → Import from File).

## Receive & Download Invoices

**Files:** `receive-invoices.json` / `receive-invoices-PL.json`

For finding out what's been issued *to* you - KSeF has no email/webhook notifications, so this polls instead. See [Receiving Invoices](../../README.md#receiving-invoices) in the main README for the underlying endpoints.

### What it does

1. Runs on a schedule (every 20 minutes by default)
2. Reads the last checkpoint from the workflow's own static data (starts from "now" on first run)
3. Calls `GET /ksef/invoices/received/new?since=<checkpoint>`
4. Saves the new `nextSince` checkpoint - only on a successful poll (a failed/rate-limited one leaves the checkpoint untouched, so nothing gets silently skipped), regardless of whether any invoices actually came back
5. For each new invoice, downloads its PDF (`GET /ksef/invoice/{ksefNumber}/pdf`) and saves it to disk
6. Leaves a `Notify (TODO)` node where you wire up Slack, email, or Discord

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `receive-invoices.json` (or the `-PL` variant)
2. **Edit the "Configuration (EDIT ME)" node**:
   - `ksefGatewayUrl` - your KSeF Gateway base URL
   - `gatewayApiKey` - your gateway's `GATEWAY_API_KEY` (sent as `X-Api-Key` - see [Security](../../README.md#security) in the main README)
   - `ksefNip` - the NIP whose inbox to poll (sent as `X-KSeF-NIP`)
   - `ksefInvoicesDir` - where to save downloaded PDFs on the n8n host
3. **Wire up notifications** - replace the `Notify (TODO)` node with a Slack/Email/Discord node of your choice
4. **Activate the workflow** in n8n

### Customization

- **Poll interval** - default 20 minutes, matches KSeF's own rate-limit guidance (don't go below ~15 min - `query/metadata` is capped at 20 requests/hour)
- **Checkpoint storage** - uses n8n's workflow static data; move it to a database node if you run multiple n8n instances without shared storage
- **Multi-NIP** - duplicate the workflow per NIP, or extend the Configuration node into a loop over several NIPs

## WooCommerce → KSeF

**Files:** `woocommerce-ksef.json` / `woocommerce-ksef-PL.json`

### What it does

1. Receives a signed WooCommerce order webhook and verifies its `X-WC-Webhook-Signature` header
2. Checks if the buyer has a NIP (Polish tax ID) in order meta data - consumers without a NIP are skipped (KSeF is only required for B2B)
3. Transforms the WooCommerce order into KSeF invoice format, auto-detecting the VAT rate per line item from WooCommerce's tax totals
4. Sends `POST /ksef/invoice` to your KSeF Gateway
5. Returns the KSeF number (or a skip/error reason) in the webhook response

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `woocommerce-ksef.json` (or the `-PL` variant)
2. **Edit the "Configuration (EDIT ME)" node** - fill in:
   - `ksefGatewayUrl` - your KSeF Gateway base URL
   - `gatewayApiKey` - your gateway's `GATEWAY_API_KEY` (sent as `X-Api-Key` - see [Security](../../README.md#security) in the main README)
   - `sellerNip`, `sellerName`, `sellerStreet`, `sellerCity` - your company's invoicing details
   - `webhookSecret` - the same secret you'll set in WooCommerce's webhook below
3. **Set `NODE_FUNCTION_ALLOW_BUILTIN=crypto`** in your n8n environment and restart (needed by the **Verify Signature** node - see the red sticky note on the canvas)
4. In WooCommerce, add a custom checkout field storing the buyer's NIP as order meta `_billing_nip` (or `billing_nip`)
5. **Configure the WooCommerce webhook:**
   - WooCommerce → Settings → Advanced → Webhooks → Add Webhook
   - Topic: `Order completed`
   - Delivery URL: this workflow's Production URL (Webhook node)
   - Secret: the same value as `webhookSecret` above
6. **Activate the workflow** in n8n

### NIP detection

The workflow looks for the buyer's NIP in WooCommerce meta fields `_billing_nip` (most Polish WooCommerce plugins use this) or `billing_nip`. If no NIP is found, the order is treated as a consumer purchase and skipped - KSeF isn't required for B2C.

### Customization

- **VAT rate detection** - auto-detects from WooCommerce tax amounts, defaults to 23%
- **Invoice number format** - defaults to `WOO/{order_number}`, edit in the **Transform to KSeF** node
- **Payment method mapping** - `cod` → cash, everything else → transfer

---

## Sellf → KSeF

**Files:** `sellf-ksef.json` / `sellf-ksef-PL.json`

For [Sellf](https://github.com/jurczykpawel/sellf) digital products platform. This is the
**source of truth** - the same two files are mirrored into the Sellf repo (`n8n/`) by a
scheduled sync there, so edit them here only.

### What it does

1. Receives a signed Sellf `purchase.completed` webhook and verifies its `X-Sellf-Signature` header
2. Checks if the buyer requested an invoice (`needsInvoice`) and provided a NIP
3. Builds a KSeF invoice payload from your seller details and the order
4. Sends `POST /ksef/invoice` to your KSeF Gateway
5. On success: captures the returned `ksefNumber` (ready to save to your DB)
6. On failure: captures the error (connect to Slack/email for alerts)

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `sellf-ksef.json` (or the `-PL` variant)
2. **Edit the "Configuration (EDIT ME)" node** - fill in:
   - `ksefGatewayUrl` - your KSeF Gateway base URL
   - `gatewayApiKey` - your gateway's `GATEWAY_API_KEY` (sent as `X-Api-Key` - see [Security](../../README.md#security) in the main README)
   - `sellerNip`, `sellerName`, `sellerStreet`, `sellerCity`, `defaultVatRate` - your company's invoicing details
   - `webhookSecret` - the signing secret from Sellf → Settings → Webhooks
3. **Set `NODE_FUNCTION_ALLOW_BUILTIN=crypto`** in your n8n environment and restart (needed by the **Verify Signature** node - see the red sticky note on the canvas)
4. **Configure the Sellf webhook** - Sellf admin → Settings → Webhooks:
   - URL: this workflow's Production URL (Webhook node)
   - Event: `purchase.completed`
5. **Activate the workflow** in n8n

### Customization

- **VAT rate** - set via `defaultVatRate` in Configuration, edit further in **Build KSeF Payload**
- **Invoice number format** - `FV/YYYY/MM/DD/XXXX` (XXXX = last 4 chars of Sellf's `sessionId`)
- **Error alerts** - connect **Log Error** to Slack, email, or Supabase

## Render keep-alive (stop free services spinning down)

**File:** `render-keepalive.json`

Only relevant if you deployed on **Render's free tier**. Free web services spin down after 15 minutes with no traffic and take ~1 minute to wake on the next request. This workflow pings `/health` on a schedule so the service stays warm - useful when a slow first response would hurt (e.g. a public demo).

### What it does

1. Runs on a schedule (default: every 10 minutes, **Mon-Fri 07:00-19:00** in your n8n's timezone)
2. Reads the list of health URLs from the "Configuration (EDIT ME)" node
3. Sends `GET <url>/health` to each (no API key needed - `/health` bypasses every gate), ignoring failures so a cold-start timeout doesn't fail the run

### ⚠️ Read this before widening the schedule

Render gives each **workspace** 750 free instance-hours **per month, shared across all your free services** - and spun-down time doesn't count. Keeping one service awake 24/7 is already ~730h (almost the whole budget). Keeping **two** services awake around the clock (~1460h) blows past 750, and Render **suspends** your free services until the next month. That's why the default schedule is work-hours-only (~290h/service/month). Widen it only if you've got the hours to spare - or put the service on a **paid** plan, which never sleeps (then you don't need this workflow at all).

For a demo that must feel instant 24/7, free tier can't do it for two services no matter how you schedule it - host it on an always-on box (a small VPS you already run) or a paid instance instead.

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `render-keepalive.json`
2. **Edit the "Configuration (EDIT ME)" node** - set your gateway's public `…onrender.com/health` URL (and uncomment the PDF service line if you render PDFs)
3. **Adjust the schedule** if needed - the trigger uses a cron expression (`*/10 7-19 * * 1-5`) in your n8n's timezone
4. **Activate the workflow** in n8n
