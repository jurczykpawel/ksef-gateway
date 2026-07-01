import express from "express";
import { js2xml } from "xml-js";
import pdfMake from "pdfmake/build/pdfmake.js";
import pdfFonts from "pdfmake/build/vfs_fonts.js";
import { parseXmlString, buildVerificationUrl } from "./xml-utils.js";

// @ts-ignore - pdfmake needs vfs for fonts
pdfMake.vfs = pdfFonts.vfs;

export function createApp() {
  const app = express();

  app.use(express.text({ type: ["application/xml", "text/xml"], limit: "10mb" }));
  app.use(express.json({ limit: "10mb" }));

  app.get("/health", (_req, res) => {
    res.json({ status: "ok" });
  });

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

      const buffer = await pdf.getBuffer();
      res.contentType("application/pdf");
      res.setHeader("Content-Disposition", "inline; filename=faktura.pdf");
      res.send(Buffer.from(buffer));
    } catch (err: any) {
      console.error("PDF generation error:", err);
      res.status(500).json({ error: `PDF generation failed: ${err.message}` });
    }
  });

  // POST /json-to-xml - converts JSON (xml-js compact format) to FA(3) XML
  // The JSON structure mirrors XML 1:1. No manual mapping needed.
  // When XSD changes, JSON structure changes automatically.
  app.post("/json-to-xml", (req, res) => {
    const json = req.body;

    if (!json || !json.Faktura) {
      res.status(400).json({
        error: 'Missing root "Faktura" element. Send JSON in xml-js compact format.',
        example: {
          Faktura: {
            _attributes: { xmlns: "http://crd.gov.pl/wzor/2025/06/25/13775/" },
            Naglowek: {
              KodFormularza: {
                _attributes: { kodSystemowy: "FA (3)", wersjaSchemy: "1-0E" },
                _text: "FA",
              },
              WariantFormularza: { _text: "3" },
              DataWytworzeniaFa: { _text: "2026-01-01T00:00:00Z" },
              SystemInfo: { _text: "my-system" },
            },
            Podmiot1: {
              DaneIdentyfikacyjne: {
                NIP: { _text: "1234567890" },
                Nazwa: { _text: "Seller sp. z o.o." },
              },
              Adres: {
                KodKraju: { _text: "PL" },
                AdresL1: { _text: "ul. Test 1" },
                AdresL2: { _text: "00-001 Warszawa" },
              },
            },
            "...": "see FA(3) XSD for full structure",
          },
        },
      });
      return;
    }

    try {
      // Inject namespace if missing
      if (!json.Faktura._attributes?.xmlns) {
        json.Faktura._attributes = {
          ...json.Faktura._attributes,
          xmlns: "http://crd.gov.pl/wzor/2025/06/25/13775/",
        };
      }

      const xml =
        '<?xml version="1.0" encoding="utf-8"?>' +
        js2xml(json, { compact: true, spaces: 0 });

      res.contentType("application/xml").send(xml);
    } catch (err: any) {
      res.status(500).json({ error: `JSON to XML conversion failed: ${err.message}` });
    }
  });

  return app;
}
