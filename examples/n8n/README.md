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
2. **Wakes the services** - calls `/health` on the gateway (and the PDF service) and retries every 30 s, up to 8 times. Only matters on Render's free tier, where services sleep after ~15 minutes and the first request gets an error page; elsewhere it passes straight through
3. Reads the last checkpoint from the workflow's own static data (first run: the gateway returns the last 30 days)
4. Calls `GET /ksef/invoices/received/new?since=<checkpoint>`
5. *Remembers* the new `nextSince` - it is only **committed after the PDFs are downloaded, saved and notified** (or immediately when nothing new came back). A failed poll or a failure midway leaves the checkpoint untouched, so nothing is skipped; the same window is retried (at-least-once: a notification can repeat after a failed run, an invoice is never lost)
6. For each new invoice, downloads its PDF (`GET /ksef/invoice/{ksefNumber}/pdf`, one at a time - the gateway allows 16 `invoices/get` per minute) and saves it to disk
7. Leaves a `Notify (TODO)` node where you wire up Slack, email, or Discord
8. After 6 failed polls in a row (~2 h) the run fails on purpose - set an **Error Workflow** in the workflow settings to get alerted

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `receive-invoices.json` (or the `-PL` variant)
2. **Create a credential** - *Header Auth*, name `X-Api-Key`, value = your gateway's `GATEWAY_API_KEY` (see [Security](../../README.md#security) in the main README). Select it on the three HTTP Request nodes (`Get New Invoices`, `Download Invoice PDF`; the `Wake` nodes need no key). The key is deliberately **not** stored in the workflow JSON
3. **Edit the "Configuration (EDIT ME)" node**:
   - `ksefGatewayUrl` - your KSeF Gateway base URL
   - `ksefNip` - the NIP whose inbox to poll (sent as `X-KSeF-NIP`)
   - `ksefInvoicesDir` - where to save downloaded PDFs on the n8n host. The folder must **exist**, be writable by the n8n user, and lie inside n8n's allowed file path (default `~/.n8n-files`, controlled by `N8N_RESTRICT_FILE_ACCESS_TO`) - otherwise `Save PDF To Disk` fails with *Access to the file is not allowed*. The default value assumes the official Docker image (`/home/node/.n8n-files/ksef-invoices`; create it first)
   - `pdfServiceUrl` - only on Render's free tier: the public URL of the PDF service (e.g. `https://your-pdf.onrender.com`). Leave empty elsewhere
4. **Wire up notifications** - replace the `Notify (TODO)` node with a Slack/Email/Discord node of your choice
5. **Activate the workflow** in n8n. Static data is only saved in production runs (triggered by the schedule) - a manual test run does **not** move the checkpoint, so every test fetches the last 30 days again

### Customization

- **Poll interval** - default 20 minutes, matches KSeF's own rate-limit guidance (don't go below ~15 min - `query/metadata` is capped at 20 requests/hour)
- **Checkpoint storage** - uses n8n's workflow static data (scope `global` - the `node` scope is private to the node that wrote it and would never be read back); move it to a database node if you run multiple n8n instances without shared storage
- **Render free tier** - the wake loop above is enough for a poller. You do **not** need `render-keepalive.json` for this: keeping a service awake 24/7 eats the shared 750 h/month (see below)
- **Multi-NIP** - duplicate the workflow per NIP, or extend the Configuration node into a loop over several NIPs (the gateway needs a [multi-NIP license](../../README.en.md#multi-nip-licensing) for more than one)

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
   - `sellerNip`, `sellerName`, `sellerStreet`, `sellerCity` - your company's invoicing details
3. **Configure secrets and serialization** as described in **Commercial workflow safety** below (restart n8n only when changing environment settings)
4. In WooCommerce, add a custom checkout field storing the buyer's NIP as order meta `_billing_nip` (or `billing_nip`)
5. **Configure the WooCommerce webhook:**
   - WooCommerce → Settings → Advanced → Webhooks → Add Webhook
   - Topic: `Order completed`
   - Delivery URL: this workflow's Production URL (Webhook node)
   - Secret: the same value as `WOO_WEBHOOK_SECRET` or the Configuration `webhookSecret` fallback
6. **Activate the workflow** in n8n

### NIP detection

The workflow looks for the buyer's NIP in WooCommerce meta fields `_billing_nip` (most Polish WooCommerce plugins use this) or `billing_nip`. If no NIP is found, the order is treated as a consumer purchase and skipped - KSeF isn't required for B2C.

### Customization

- **VAT rate detection** - auto-detects from WooCommerce tax amounts, defaults to 23%; line totals already include discounts, and shipping/fees become additional lines
- **Invoice number format** - defaults to `WOO/{order.id}` (full immutable order ID), edit in the **Transform to KSeF** node
- **Payment method mapping** - `cod` → cash, everything else → transfer

---

## Sellf → KSeF

**Files:** `sellf-ksef.json` / `sellf-ksef-PL.json`

For [Sellf](https://github.com/jurczykpawel/sellf) digital products platform. This is the
**source of truth** - the same two files are mirrored into the Sellf repo (`n8n/`) by a
scheduled sync there, so edit them here only.

### What it does

1. Receives a signed Sellf `purchase.completed` webhook and verifies its `X-Sellf-Signature` header
2. Checks `needsInvoice`, then classifies the business identifier and VAT rate using the invoice country
3. Builds a KSeF invoice payload from your seller details and the order
4. Sends `POST /ksef/invoice` to your KSeF Gateway
5. On success: captures the returned `ksefNumber` (ready to save to your DB)
6. On an ambiguous failure: records `uncertain` and requires manual reconciliation before resending

### Setup

1. **Import workflow** - n8n → Workflows → Import from File → select `sellf-ksef.json` (or the `-PL` variant)
2. **Edit the "Configuration (EDIT ME)" node** - fill in:
   - `ksefGatewayUrl` - your KSeF Gateway base URL
   - `sellerNip`, `sellerName`, `sellerStreet`, `sellerCity`, `defaultVatRate` - your company's invoicing details
   - `euB2bVatRate` (default `"np II"`) and `exportVatRate` (default `"np I"`) - rates for EU and non-EU business buyers
3. **Configure secrets and serialization** as described in **Commercial workflow safety** below (restart n8n only when changing environment settings)
4. **Configure the Sellf webhook** - Sellf admin → Settings → Webhooks:
   - URL: this workflow's Production URL (Webhook node)
   - Event: `purchase.completed`
5. **Activate the workflow** in n8n

### Customization

- **VAT rates** - set `defaultVatRate`, `euB2bVatRate` and `exportVatRate` in Configuration; see the buyer classification below
- **Invoice number format** - `FV/{sessionId}` (full Sellf session ID)
- **Error alerts** - inspect the final `status` response and add your own alert after **Record Send Result**

### Sellf buyer country and identifier → fields and rate

The template reads `invoice.country` and `invoice.nip` from the signed Sellf payload (extracted as `invoiceCountry` and `invoiceNip`). All business identifiers, including foreign ones, use Sellf's `invoice.nip` field.

| Country / identifier | Gateway buyer field | Configuration rate (shipped default) |
|---|---|---|
| `PL` or missing country, 10-digit NIP | `buyer.nip` (spaces, hyphens and `PL` prefix removed) | `defaultVatRate` (`23`) |
| EU other than PL, VAT ID with or without its country prefix | `buyer.euVatNumber` (uppercase, prefix added if missing) | `euB2bVatRate` (`"np II"`) |
| Non-EU, including `US` / `GB`, with a tax ID | `buyer.taxId` and `buyer.address.country` | `exportVatRate` (`"np I"`) |
| Missing/unusable ID, invalid NIP/country, or inconsistent EU prefix | No invoice or reservation; HTTP 200 with `skipped: true`, `reason: "pominięto: <powód>"` | No gateway POST |

Country codes are normalized to uppercase. The EU VAT prefix list matches FA(3) `TKodyKrajowUE`: Greece uses address `GR` and VAT prefix `EL` (`EL` input country is normalized to `GR`); Northern Ireland uses `XI`. A `GB` address follows the non-EU path; use `XI` in Sellf to select Northern Ireland's EU VAT path. EU IDs must have 1–12 letters/digits/`+`/`*` after the prefix and contain a digit; no VIES or tax eligibility lookup is performed. Conflicting EU prefixes are skipped. NIP validation checks 10 ASCII digits, without a checksum lookup.

All three rate fields are validated: numbers `23`, `8`, `5`, `0`, or strings `"np II"`, `"np I"`, `"0 WDT"`, `"0 EX"`, `"zw"`. Invalid configuration skips the invoice with a reason. The gateway additionally requires an EU VAT ID for `"0 WDT"`; for `"zw"`, add a nonempty `exemptionBasis` string to Configuration, which is forwarded to the invoice. Without these required fields the template skips the invoice.

Defaults target services/digital content sold to businesses. **Foreign B2C/OSS is outside this template's scope**; missing identifiers are skipped, never sent as `noId`. Configure rates for your transaction type; structural validation does not establish legal eligibility. Skip reasons contain no customer details; avoid logging full customer payloads in custom alert nodes.

## Commercial workflow safety (Sellf and WooCommerce)

Before activation, configure **Header Auth** with name `X-Api-Key` and value equal to `GATEWAY_API_KEY`; select this credential on **Send to KSeF Gateway**. No gateway key belongs in Configuration or exported workflow JSON.

The Code node cannot access Header Auth credentials directly. **Verify Signature** selects the signing secret in this order:

1. `$env.SELLF_WEBHOOK_SECRET` or `$env.WOO_WEBHOOK_SECRET`, when access is allowed and the value is nonempty.
2. Otherwise, `webhookSecret` in **Configuration (EDIT ME)**. Replace `your-webhook-secret` with the same secret configured at the webhook provider. This fallback works with blocked environment access, including n8n Cloud.

[n8n 2.x blocks environment access in Code nodes by default](https://docs.n8n.io/changelog/v20-breaking-changes/#block-environment-variable-access-from-code-node-by-default). Setting `N8N_BLOCK_ENV_ACCESS_IN_NODE=false` is optional, for self-hosted installations choosing environment secrets; only trusted workflow editors should have this access. Self-hosted execution runtimes/task runners also need `NODE_FUNCTION_ALLOW_BUILTIN=crypto` and a restart after environment changes. Empty/missing secrets and the shipped placeholder deny the webhook (401) with a configuration warning that does not print the secret. A nonempty environment value takes precedence even if it is a placeholder (which is rejected).

**Fallback tradeoff:** the secret in the Configuration field appears in that node's output and in workflow exports. Prefer an environment variable or your own credential-backed verification node; **do not share workflow exports containing a secret**. The template reads the fallback only inside Verify Signature and does not copy it to later node outputs, webhook responses or gateway requests. Successful/error execution saving, manual saving and execution progress saving are disabled in the templates; inspect these settings after import. Disabling execution saving does not remove the secret from the workflow definition or its Configuration output during a run.

Webhook **Raw Body** stays enabled, and Configuration preserves binary data. Verification uses `this.helpers.getBinaryDataBuffer(0, 'data')`, supporting n8n-managed binary storage. Missing raw bytes, invalid JSON, malformed hex/base64, wrong HMAC or an expired Sellf timestamp deny with 401. There is no JSON reserialization fallback. See the [official binary helper documentation](https://docs.n8n.io/code/cookbook/code-node/get-binary-data-buffer/) and [environment variable access reference](https://docs.n8n.io/code/builtin/n8n-metadata/).

### Duplicate deliveries and uncertain outcomes

**Reserve Invoice** stores up to 1,000 order keys and statuses in workflow static data (`global`), before sending. Sellf uses the full session ID; WooCommerce uses the immutable order ID, so different webhook deliveries for the same order cannot create another invoice. A repeat returns HTTP 200 with `duplicate: true`, its status and the KSeF number when known. An unresolved record is never evicted automatically; at capacity, only an old `sent` entry can be evicted. Once evicted, that old order is outside the deduplication window.

Statuses: `in_progress` before POST, `sent` after a confirmed KSeF number, `not_accepted` after exhausted health/safe rejection attempts, and `uncertain` after timeout, transport errors, gateway JSON errors or an unconfirmed response. All these records block another POST. Terminal handled outcomes return HTTP 200 (with `success: false` for failures), allowing n8n to complete normally and persist state; **200 does not imply an invoice was issued**. Monitor these results externally. Sellf no longer acknowledges before processing; provider/proxy timeouts during cold starts are possible.

**Important durability boundary:** n8n persists static data only after successful production executions, not at the line that assigns `in_progress`; manual runs do not persist it. Static data is not an atomic distributed lock. Run these templates with **production concurrency 1 on a single execution instance** (for example a dedicated regular-mode instance with `N8N_CONCURRENCY_PRODUCTION_LIMIT=1`); avoid queue/multiple-worker deployment. See [n8n static data limitations](https://docs.n8n.io/code/cookbook/builtin/get-workflow-static-data/). A process crash, cancelled execution, or workflow failure after POST can lose the reservation even with concurrency 1. These JSON-only templates cannot guarantee exactly-once invoicing across such failures. For unattended crash-safe operation, add a durable external reservation/job store with a unique order key and reconciliation before enabling retries. This is deliberately outside these database-free examples.

For `uncertain`, interrupted executions or `not_accepted`: inspect KSeF and the gateway logs using the full invoice/order identifier, record any existing KSeF number, and only then manually reconcile the static-data entry. Do not delete the entry and rerun blindly. Preserve/backup workflow static data when migrating or reimporting; a fresh workflow has a fresh deduplication window.

### Cold starts and retry policy

The gateway `/health` loop has a 60-second request timeout and waits 30 seconds between attempts (9 probes maximum). Exhaustion ends without any invoice POST. POST has a **120-second timeout**, no n8n automatic retry, and at most **two additional attempts** only for:

- HTTP 429 with a valid `Retry-After` (seconds or HTTP date) between 0 and 300 seconds; wait at least 1 second.
- An upstream HTTP 503 with an empty or HTML body; wait 5, then 10 seconds. This assumes your reverse proxy produces these responses **before forwarding**. Disable this retry branch if your proxy can return HTML 503 after forwarding a request.

Gateway JSON 503, 502, timeouts and transport failures are **uncertain** and never retried automatically. Waiting does not clear the reservation, and repeated webhook delivery never invokes the retry loop. The optional PDF service is not called by these issuing workflows and is not woken.

### Amounts and supported orders

Sellf `order.amount` is the amount charged, in minor units. When available, `order.netTotal` (also minor units) supplies the net amount, including zero. Otherwise numeric rates use `gross / (1 + selectedRate / 100)`, rounded to cents; `"np I"` / `"np II"` use `netAmount ?? amount` without VAT addition or deduction (the other nonnumeric rates also have zero VAT). Domestic numeric-rate behavior is unchanged. Set the three rate fields to the actual rates for your sales. This single-product template assumes one VAT rate and a two-decimal currency: baskets with bumps/mixed tax rates or other currency minor units require explicit per-line mapping before use.

WooCommerce uses post-discount `line_items[].total` and each line's tax total, includes `shipping_lines` and `fee_lines`, and preserves `total / quantity` precision. Thus 10.00 net across 3 units stays 10.00 after line rounding instead of 9.99. It does not subtract coupons a second time. Supported VAT rates are 23/8/5/0; review unusual tax classes, mixed taxes on one line, refunds and credit notes separately.

Run the offline harness with `node --test "examples/n8n/test/*.test.mjs"` (also runs in CI). It checks code with mocked n8n helpers and workflow graph structure; live import/runner and provider timeout validation remain deployment checks.

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
