# n8n Integration Workflows

Example n8n workflows for connecting e-commerce platforms to KSeF Gateway.

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
