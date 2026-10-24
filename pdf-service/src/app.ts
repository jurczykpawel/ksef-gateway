import express, { type ErrorRequestHandler } from "express";
import crypto from "node:crypto";
import { js2xml } from "xml-js";
import pdfMake from "pdfmake/build/pdfmake.js";
import pdfFonts from "pdfmake/build/vfs_fonts.js";
import { parseXmlString, buildVerificationUrl } from "./xml-utils.js";
import { i18nReady } from "../lib/src/lib-public/i18n/i18n-init.js";

pdfMake.addVirtualFileSystem(pdfFonts);

export function createApp() {
  const app = express();
  const maxRenders = Number(process.env.PDF_MAX_CONCURRENT_RENDERS ?? "2");
  if (!Number.isSafeInteger(maxRenders) || maxRenders <= 0)
    throw new Error("PDF_MAX_CONCURRENT_RENDERS must be a positive integer");
  let activeRenders = 0;

  // Optional shared-secret gate (opt-in). When PDF_SERVICE_SECRET is set, every request except
  // /health must carry it in the X-Pdf-Secret header. This lets the service be reached over the
  // public internet (Render free web services can't receive private-network traffic) without being
  // an open XML→PDF endpoint. Unset = open, for private networks (local compose / paid private
  // networking). Runs before the body parsers so unauthenticated requests are rejected before a
  // 10mb body is read.
  const pdfSecret = process.env.PDF_SERVICE_SECRET;
  if (pdfSecret && pdfSecret.trim()) {
    const expected = Buffer.from(pdfSecret);
    app.use((req, res, next) => {
      if (req.path === "/health") return next();
      const provided = Buffer.from(req.get("X-Pdf-Secret") ?? "");
      if (provided.length !== expected.length || !crypto.timingSafeEqual(provided, expected)) {
        res.status(403).json({ success: false, error: "This PDF service only accepts authenticated requests." });
        return;
      }
      next();
    });
  }

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
    if (activeRenders >= maxRenders) {
      res.setHeader("Retry-After", "1");
      res.status(503).json({ success: false, error: "PDF renderer busy" });
      return;
    }
    activeRenders++;
    try {
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
          success: false,
          error: "Missing invoice XML. Send as XML body or JSON { xml, nrKSeF?, qrCode? }",
        });
        return;
      }

      if (typeof xml !== "string") {
        res.status(400).json({ success: false, error: "Invoice XML must be a string" });
        return;
      }

      let parsed: any;
      try {
        parsed = parseXmlString(xml);
        // QR generation also parses XML and must use the same safe input-error path.
        if (nrKSeF && !qrCode) {
          qrCode = buildVerificationUrl(xml, nrKSeF);
        }
      } catch (err) {
        console.error("Invalid invoice XML:", err);
        res.status(400).json({ success: false, error: "Invalid invoice XML" });
        return;
      }

      const invoice = parsed?.Faktura;
      if (!invoice) {
        res.status(400).json({ success: false, error: "Invalid XML: no <Faktura> root element found" });
        return;
      }

      const kodSystemowy = invoice?.Naglowek?.KodFormularza?._attributes?.kodSystemowy;

      let generateFn: (invoice: any, additionalData: any) => any;

      await i18nReady;
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
          res.status(400).json({ success: false, error: `Unsupported schema: ${kodSystemowy}` });
          return;
      }

      const additionalData = { nrKSeF: nrKSeF ?? "", qrCode };
      const pdf = generateFn(invoice, additionalData);

      const buffer = await pdf.getBuffer();
      res.contentType("application/pdf");
      res.setHeader("Content-Disposition", "inline; filename=faktura.pdf");
      res.send(Buffer.from(buffer));
    } finally {
      // Hold the slot until rendering settles, even if the HTTP caller disconnects.
      activeRenders--;
    }
  });

  // POST /json-to-xml - converts JSON (xml-js compact format) to FA(3) XML
  // The JSON structure mirrors XML 1:1. No manual mapping needed.
  // When XSD changes, JSON structure changes automatically.
  app.post("/json-to-xml", (req, res) => {
    const json = req.body;

    if (!json || !json.Faktura) {
      res.status(400).json({
        success: false,
        error: 'Missing root "Faktura" element. Send JSON in xml-js compact format.',
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
    } catch (err) {
      console.error("Invalid invoice JSON:", err);
      res.status(400).json({ success: false, error: "Invalid invoice JSON" });
    }
  });

  // Express 5 forwards synchronous throws and rejected async handlers here, including
  // body-parser errors. Never expose exception messages or stacks to callers.
  const errorHandler: ErrorRequestHandler = (err, _req, res, next) => {
    console.error("PDF service error:", err);
    if (res.headersSent) return next(err);
    const status = Number.isInteger(err?.status) && err.status >= 400 && err.status < 500
      ? err.status : 500;
    const error = status === 413 ? "Request body too large"
      : status < 500 ? "Invalid request body" : "Internal server error";
    res.status(status).json({ success: false, error });
  };
  app.use(errorHandler);

  return app;
}
