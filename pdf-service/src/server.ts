import express from "express";
import crypto from "crypto";
import { xml2js } from "xml-js";
import pdfMake from "pdfmake/build/pdfmake.js";
import pdfFonts from "pdfmake/build/vfs_fonts.js";

// @ts-ignore - pdfmake needs vfs for fonts
pdfMake.vfs = pdfFonts.vfs;

const app = express();
const PORT = parseInt(process.env.PORT || "3000", 10);

app.use(express.text({ type: ["application/xml", "text/xml"], limit: "10mb" }));
app.use(express.json({ limit: "10mb" }));

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

// Reuse CIRFMF's stripPrefixes logic
function stripPrefixes<T>(obj: T): T {
  if (Array.isArray(obj)) {
    return obj.map(stripPrefixes) as T;
  } else if (typeof obj === "object" && obj !== null) {
    return Object.fromEntries(
      Object.entries(obj).map(([key, value]: [string, unknown]): [string, unknown] => [
        key.includes(":") ? key.split(":")[1] : key,
        stripPrefixes(value),
      ])
    ) as T;
  }
  return obj;
}

function parseXmlString(xmlStr: string): unknown {
  return stripPrefixes(xml2js(xmlStr, { compact: true }));
}

// POST /pdf/invoice
// Content-Type: application/xml → raw XML body
// Content-Type: application/json → { xml: "...", nrKSeF?: "...", qrCode?: "..." }
// Query params: ?nrKSeF=...&qrCode=...
app.post("/pdf/invoice", async (req, res) => {
  let xml: string;
  let nrKSeF: string | undefined;
  let qrCode: string | undefined;

  if (typeof req.body === "string") {
    // XML body
    xml = req.body;
    nrKSeF = req.query.nrKSeF as string | undefined;
    qrCode = req.query.qrCode as string | undefined;
  } else if (req.body?.xml) {
    // JSON body
    xml = req.body.xml;
    nrKSeF = req.body.nrKSeF;
    qrCode = req.body.qrCode;
  } else {
    res.status(400).json({
      error: "Missing invoice XML. Send as XML body or JSON { xml, nrKSeF?, qrCode? }",
    });
    return;
  }

  // Auto-generate QR verification URL if nrKSeF provided but qrCode not
  if (nrKSeF && !qrCode) {
    qrCode = buildVerificationUrl(xml, nrKSeF);
  }

  try {
    const parsed = parseXmlString(xml) as any;
    const invoice = parsed?.Faktura;
    if (!invoice) {
      res.status(400).json({ error: "Invalid XML: no <Faktura> root element found" });
      return;
    }

    const kodSystemowy = invoice?.Naglowek?.KodFormularza?._attributes?.kodSystemowy;

    let generateFn: (invoice: any, additionalData: any) => any;

    switch (kodSystemowy) {
      case "FA (3)": {
        const { generateFA3 } = await import("../lib/src/lib-public/FA3-generator.js");
        generateFn = generateFA3;
        break;
      }
      case "FA (2)": {
        const { generateFA2 } = await import("../lib/src/lib-public/FA2-generator.js");
        generateFn = generateFA2;
        break;
      }
      case "FA (1)": {
        const { generateFA1 } = await import("../lib/src/lib-public/FA1-generator.js");
        generateFn = generateFA1;
        break;
      }
      default:
        res.status(400).json({ error: `Unsupported schema: ${kodSystemowy}` });
        return;
    }

    const additionalData = { nrKSeF: nrKSeF ?? "", qrCode };
    const pdf = generateFn(invoice, additionalData);

    pdf.getBuffer((buffer: Buffer) => {
      res.contentType("application/pdf");
      res.setHeader("Content-Disposition", "inline; filename=faktura.pdf");
      res.send(Buffer.from(buffer));
    });
  } catch (err: any) {
    console.error("PDF generation error:", err);
    res.status(500).json({ error: `PDF generation failed: ${err.message}` });
  }
});

// Build KSeF QR verification URL from invoice XML
// Format: https://qr-test.ksef.mf.gov.pl/invoice/{NIP}/{DD-MM-RRRR}/{SHA256-Base64URL}
function buildVerificationUrl(xml: string, _nrKSeF: string): string {
  const hash = crypto.createHash("sha256").update(xml, "utf8").digest();
  const hashB64Url = hash.toString("base64url");

  // Extract NIP and date from KSeF number: NIP-RRRRMMDD-...-..
  const parts = _nrKSeF.split("-");
  const nip = parts[0];
  const dateRaw = parts[1]; // RRRRMMDD
  const dateFormatted = `${dateRaw.slice(6, 8)}-${dateRaw.slice(4, 6)}-${dateRaw.slice(0, 4)}`; // DD-MM-RRRR

  // Use TEST environment URL (TODO: make configurable)
  return `https://qr-test.ksef.mf.gov.pl/invoice/${nip}/${dateFormatted}/${hashB64Url}`;
}

app.listen(PORT, "0.0.0.0", () => {
  console.log(`ksef-pdf-service listening on port ${PORT}`);
});
