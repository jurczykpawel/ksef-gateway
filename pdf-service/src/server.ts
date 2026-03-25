import express from "express";
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

app.post("/pdf/invoice", async (req, res) => {
  const xml = typeof req.body === "string" ? req.body : req.body?.xml;

  if (!xml) {
    res.status(400).json({ error: "Missing invoice XML. Send as body with Content-Type: application/xml" });
    return;
  }

  try {
    const parsed = parseXmlString(xml) as any;
    const invoice = parsed?.Faktura;
    if (!invoice) {
      res.status(400).json({ error: "Invalid XML: no <Faktura> root element found" });
      return;
    }

    const kodSystemowy = invoice?.Naglowek?.KodFormularza?._attributes?.kodSystemowy;

    // Dynamic import of the appropriate generator
    let generateFn: (invoice: any, additionalData: any) => any;

    switch (kodSystemowy) {
      case "FA (3)":
        const { generateFA3 } = await import("../lib/src/lib-public/FA3-generator.js");
        generateFn = generateFA3;
        break;
      case "FA (2)":
        const { generateFA2 } = await import("../lib/src/lib-public/FA2-generator.js");
        generateFn = generateFA2;
        break;
      case "FA (1)":
        const { generateFA1 } = await import("../lib/src/lib-public/FA1-generator.js");
        generateFn = generateFA1;
        break;
      default:
        res.status(400).json({ error: `Unsupported schema: ${kodSystemowy}. Expected FA (1), FA (2), or FA (3)` });
        return;
    }

    const pdf = generateFn(invoice, {});

    // pdfmake getBuffer for Node.js
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

app.listen(PORT, "0.0.0.0", () => {
  console.log(`ksef-pdf-service listening on port ${PORT}`);
});
