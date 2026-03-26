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
