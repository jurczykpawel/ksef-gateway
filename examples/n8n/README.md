# n8n Integration Workflows

Example n8n workflows for connecting e-commerce platforms to KSeF Gateway - both sending invoices out and pulling received ones in.

## Receive & Download Invoices

**File:** `receive-invoices.json`

For finding out what's been issued *to* you - KSeF has no email/webhook notifications, so this polls instead. See [Receiving Invoices](../../README.en.md#receiving-invoices) in the main README for the underlying endpoints.

### What it does

1. Runs on a schedule (every 20 minutes by default)
2. Reads the last checkpoint from the workflow's own static data (starts from "now" on first run)
3. Calls `GET /ksef/invoices/received/new?since=<checkpoint>`
4. Saves the new `nextSince` checkpoint - only on a successful poll (a failed/rate-limited one leaves the checkpoint untouched, so nothing gets silently skipped), regardless of whether any invoices actually came back
5. For each new invoice, downloads its PDF (`GET /ksef/invoice/{ksefNumber}/pdf`) and saves it to disk
6. Leaves a `Notify (TODO)` node where you wire up Slack, email, or Discord

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `receive-invoices.json`
2. **Edit the "Configuration (EDIT ME)" node**:
   - `ksefGatewayUrl` - your KSeF Gateway base URL
   - `ksefNip` - the NIP whose inbox to poll (sent as `X-KSeF-NIP`)
   - `ksefInvoicesDir` - where to save downloaded PDFs on the n8n host
3. **Wire up notifications** - replace the `Notify (TODO)` node with a Slack/Email/Discord node of your choice
4. **Activate the workflow** in n8n

### Customization

- **Poll interval** - default 20 minutes, matches KSeF's own rate-limit guidance (don't go below ~15 min - `query/metadata` is capped at 20 requests/hour)
- **Checkpoint storage** - uses n8n's workflow static data; move it to a database node if you run multiple n8n instances without shared storage
- **Multi-NIP** - duplicate the workflow per NIP, or extend the Configuration node into a loop over several NIPs

## WooCommerce → KSeF

**File:** `woocommerce-ksef.json`

### What it does

1. Receives WooCommerce order webhook (`order.completed`)
2. Checks if buyer has a NIP (Polish tax ID) - consumers without NIP are skipped
3. Transforms WooCommerce order data to KSeF invoice format
4. Sends `POST /ksef/invoice` to your KSeF Gateway
5. Returns KSeF number in the response

### Setup

1. **Import workflow** - open n8n, go to Workflows → Import from File → select `woocommerce-ksef.json`

2. **Set environment variable** in n8n:
   ```
   KSEF_GATEWAY_URL=https://your-ksef-gateway.onrender.com
   ```

3. **Edit seller data** in the "Transform to KSeF" node - replace placeholders:
   - `{{ SELLER_NIP }}` → your company NIP
   - `{{ SELLER_NAME }}` → your company name
   - `{{ SELLER_STREET }}` → your company address
   - `{{ SELLER_CITY }}` → your company city (with postal code)

4. **Configure WooCommerce webhook:**
   - WooCommerce → Settings → Advanced → Webhooks → Add Webhook
   - Topic: `Order completed`
   - Delivery URL: `https://your-n8n.example.com/webhook/woo-ksef`
   - Secret: (optional, for signature verification)

5. **Activate the workflow** in n8n

### NIP detection

The workflow looks for buyer's NIP in WooCommerce meta fields:
- `_billing_nip` (most Polish WooCommerce plugins use this)
- `billing_nip`

If no NIP is found, the order is skipped (consumer purchase - KSeF not required for B2C).

### Customization

- **VAT rate detection** - auto-detects from WooCommerce tax amounts, defaults to 23%
- **Invoice number format** - defaults to `WOO/{order_number}`, edit in Transform node
- **Payment method mapping** - `cod` → cash, everything else → transfer

---

## Sellf → KSeF

**File:** `sellf-ksef.json`

For [Sellf](https://github.com/jurczykpawel/sellf) digital products platform.

### What it does

1. Receives Sellf purchase webhook (`purchase.completed`)
2. Checks if buyer requested an invoice (`needsInvoice`) and provided NIP
3. Builds KSeF invoice with seller data from n8n variables
4. Sends `POST /ksef/invoice` to your KSeF Gateway
5. On success: extracts KSeF number (ready to save to DB)
6. On failure: logs error (connect to Slack/email for alerts)

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `sellf-ksef.json`

2. **Set n8n variables** (Settings → Variables):
   ```
   KSEF_GATEWAY_URL=https://your-ksef-gateway.onrender.com
   SELLER_NIP=your company NIP
   SELLER_NAME=Your Company sp. z o.o.
   SELLER_STREET=ul. Firmowa 1
   SELLER_CITY=00-001 Warszawa
   ```

3. **Configure Sellf webhook:**
   - Sellf admin → Settings → Webhooks
   - URL: `https://your-n8n.example.com/webhook/sellf-purchase-ksef`
   - Event: `purchase.completed`

4. **Activate the workflow** in n8n

### Customization

- **VAT rate** - hardcoded to 23%, edit in "Build KSeF Payload" node
- **Invoice number format** - `FV/YYYY/MM/DD/XXXX` (XXXX = last 4 chars of Sellf sessionId)
- **Error alerts** - connect "Log Error" node to Slack, email, or Supabase

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
