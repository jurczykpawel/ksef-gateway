import { describe, it, expect, vi } from "vitest";
import { readFileSync } from "fs";
import request from "supertest";
import pdfMake from "pdfmake/build/pdfmake.js";
import { js2xml, xml2js } from "xml-js";
import { createApp } from "./app.js";

const fixtureXml = readFileSync(
  new URL("../test/fixtures/invoice-fa3.xml", import.meta.url),
  "utf8"
);
const fixtureJson = JSON.parse(
  readFileSync(new URL("../test/fixtures/invoice-fa3.json", import.meta.url), "utf8")
);

const app = createApp();

function withKodSystemowy(xml: string, kod: string): string {
  return xml.replace('kodSystemowy="FA (3)"', `kodSystemowy="${kod}"`);
}

describe("GET /health", () => {
  it("returns ok status", async () => {
    const res = await request(app).get("/health");
    expect(res.status).toBe(200);
    expect(res.body).toEqual({ status: "ok" });
  });
});

describe("POST /pdf/invoice", () => {
  it("generates a PDF from a raw XML body (FA(3))", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send(fixtureXml);

    expect(res.status).toBe(200);
    expect(res.headers["content-type"]).toContain("application/pdf");
    expect(res.headers["content-disposition"]).toContain("faktura.pdf");
    expect(Buffer.isBuffer(res.body)).toBe(true);
    expect(res.body.subarray(0, 5).toString()).toBe("%PDF-");
  });

  it("generates a PDF for FA(2)", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send(withKodSystemowy(fixtureXml, "FA (2)"));

    expect(res.status).toBe(200);
    expect(res.body.subarray(0, 5).toString()).toBe("%PDF-");
  });

  it("generates a PDF for FA(1)", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send(withKodSystemowy(fixtureXml, "FA (1)"));

    expect(res.status).toBe(200);
    expect(res.body.subarray(0, 5).toString()).toBe("%PDF-");
  });

  it("generates a PDF from a JSON body with xml + nrKSeF (auto QR)", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .send({ xml: fixtureXml, nrKSeF: "1234567890-20260324-ABCDEF" });

    expect(res.status).toBe(200);
    expect(res.body.subarray(0, 5).toString()).toBe("%PDF-");
  });

  it("generates a PDF from a JSON body with an explicit qrCode (no auto-generation)", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .send({ xml: fixtureXml, qrCode: "https://example.com/manual-qr" });

    expect(res.status).toBe(200);
    expect(res.body.subarray(0, 5).toString()).toBe("%PDF-");
  });

  it("rejects an empty body with 400", async () => {
    const res = await request(app).post("/pdf/invoice").send();
    expect(res.status).toBe(400);
    expect(res.body.error).toMatch(/Missing invoice XML/);
  });

  it("rejects JSON body without an xml field with 400", async () => {
    const res = await request(app).post("/pdf/invoice").send({ nrKSeF: "123" });
    expect(res.status).toBe(400);
    expect(res.body.error).toMatch(/Missing invoice XML/);
  });

  it("rejects XML without a <Faktura> root element with 400", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send("<NotAnInvoice><Foo>bar</Foo></NotAnInvoice>");

    expect(res.status).toBe(400);
    expect(res.body.error).toMatch(/no <Faktura> root element/);
  });

  it("rejects an unsupported kodSystemowy with 400", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send(withKodSystemowy(fixtureXml, "FA (99)"));

    expect(res.status).toBe(400);
    expect(res.body.error).toBe("Unsupported schema: FA (99)");
  });

  it("rejects malformed XML with a safe 400 response", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send("<Faktura><Unclosed></Faktura>");

    expect(res.status).toBe(400);
    expect(res.body).toEqual({ success: false, error: "Invalid invoice XML" });
  });
});

describe("POST /json-to-xml", () => {
  it("converts a Faktura JSON document to XML", async () => {
    const res = await request(app).post("/json-to-xml").send(fixtureJson);

    expect(res.status).toBe(200);
    expect(res.headers["content-type"]).toContain("application/xml");
    expect(res.text.startsWith('<?xml version="1.0" encoding="utf-8"?>')).toBe(true);
    expect(res.text).toContain("<Faktura");
    expect(res.text).toContain("<NIP>1234567890</NIP>");
  });

  it("injects the default xmlns when the input has none", async () => {
    const res = await request(app).post("/json-to-xml").send(fixtureJson);
    expect(res.status).toBe(200);
    expect(res.text).toContain('xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/"');
  });

  it("keeps a caller-supplied xmlns instead of overwriting it, matching js2xml's own output exactly", async () => {
    const customJson = {
      Faktura: {
        _attributes: { xmlns: "urn:custom" },
        Naglowek: { SystemInfo: { _text: "x" } },
      },
    };
    const res = await request(app).post("/json-to-xml").send(customJson);
    expect(res.status).toBe(200);
    expect(res.text).toContain('xmlns="urn:custom"');

    const expectedXml =
      '<?xml version="1.0" encoding="utf-8"?>' + js2xml(customJson, { compact: true, spaces: 0 });
    expect(res.text).toBe(expectedXml);
  });

  it("produces XML that round-trips back to the same structure via xml2js", async () => {
    const res = await request(app).post("/json-to-xml").send(fixtureJson);
    expect(res.status).toBe(200);

    const reparsed = xml2js(res.text, { compact: true }) as any;
    expect(reparsed.Faktura.Fa.P_2._text).toBe(fixtureJson.Faktura.Fa.P_2._text);
    expect(reparsed.Faktura.Fa.FaWiersz).toHaveLength(2);
    expect(reparsed.Faktura.Podmiot1.DaneIdentyfikacyjne.NIP._text).toBe("1234567890");
  });

  it("rejects a body without a Faktura root with a concise 400 response", async () => {
    const res = await request(app).post("/json-to-xml").send({ notFaktura: true });
    expect(res.status).toBe(400);
    expect(res.body.error).toMatch(/Missing root "Faktura" element/);
    expect(res.body).toEqual({ success: false, error: 'Missing root "Faktura" element. Send JSON in xml-js compact format.' });
  });

  it("rejects an empty body with 400", async () => {
    const res = await request(app).post("/json-to-xml").send();
    expect(res.status).toBe(400);
  });
});

describe("PDF_SERVICE_SECRET gate (opt-in)", () => {
  const SECRET = "test-pdf-secret-123";
  // createApp() reads PDF_SERVICE_SECRET at construction, so build a gated instance with it set.
  const gatedApp = (() => {
    const prev = process.env.PDF_SERVICE_SECRET;
    process.env.PDF_SERVICE_SECRET = SECRET;
    const a = createApp();
    process.env.PDF_SERVICE_SECRET = prev;
    return a;
  })();

  it("lets /health through without the secret", async () => {
    const res = await request(gatedApp).get("/health");
    expect(res.status).toBe(200);
  });

  it("rejects a protected route with no secret (403)", async () => {
    const res = await request(gatedApp)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send(fixtureXml);
    expect(res.status).toBe(403);
  });

  it("rejects a wrong secret (403)", async () => {
    const res = await request(gatedApp)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .set("X-Pdf-Secret", "nope")
      .send(fixtureXml);
    expect(res.status).toBe(403);
  });

  it("accepts the correct secret", async () => {
    const res = await request(gatedApp)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .set("X-Pdf-Secret", SECRET)
      .send(fixtureXml);
    expect(res.status).toBe(200);
    expect(res.headers["content-type"]).toContain("application/pdf");
  });

  it("stays open when no secret is configured (default app)", async () => {
    const res = await request(app)
      .post("/pdf/invoice")
      .set("Content-Type", "application/xml")
      .send(fixtureXml);
    expect(res.status).toBe(200);
  });
});

describe("safe error responses", () => {
  function expectError(res: request.Response, status: number, error: string) {
    expect(res.text).not.toMatch(/<pre>|\bat |\/(?:app|Users)\/|node_modules|RangeError/);
    expect(res.status).toBe(status);
    expect(res.headers["content-type"]).toContain("application/json");
    expect(res.body).toEqual({ success: false, error });
  }

  it.each([false, true])("rejects malformed XML (auto QR: %s) without an HTML stack", async (autoQr) => {
    const res = await request(app).post("/pdf/invoice").send({
      xml: "<Faktura><Unclosed></Faktura>",
      ...(autoQr ? { nrKSeF: "1234567890-20260324-ABCDEF" } : {}),
    });
    expectError(res, 400, "Invalid invoice XML");
  });

  it.each([false, true])("handles deeply nested XML (auto QR: %s) without leaking RangeError", async (autoQr) => {
    const res = await request(app).post("/pdf/invoice").send({
      xml: "<Faktura>" + "<a>".repeat(12000) + "</a>".repeat(12000) + "</Faktura>",
      ...(autoQr ? { nrKSeF: "1234567890-20260324-ABCDEF" } : {}),
    });
    expectError(res, 400, "Invalid invoice XML");
  });

  it.each([42, { path: "/app/private.xml" }])("rejects non-string XML: %j", async (xml) => {
    const res = await request(app).post("/pdf/invoice").send({ xml, nrKSeF: "123" });
    expectError(res, 400, "Invoice XML must be a string");
  });

  it("handles malformed JSON from the body parser", async () => {
    const res = await request(app).post("/pdf/invoice").type("json").send('{"xml":');
    expectError(res, 400, "Invalid request body");
  });

  it("preserves 415 for an unsupported request charset", async () => {
    const res = await request(app).post("/pdf/invoice")
      .set("Content-Type", "application/xml; charset=unsupported").send("<Faktura/>");
    expectError(res, 415, "Invalid request body");
  });

  it("handles oversized XML from the body parser", async () => {
    const res = await request(app).post("/pdf/invoice").type("application/xml").send("x".repeat(10 * 1024 * 1024 + 1));
    expectError(res, 413, "Request body too large");
  });

  it("hides unexpected renderer failures while logging the original error", async () => {
    const error = new Error("private renderer detail at /app/lib/internal.ts");
    const log = vi.spyOn(console, "error").mockImplementation(() => {});
    const render = vi.spyOn(pdfMake, "createPdf").mockImplementationOnce(() => { throw error; });
    try {
      const res = await request(app).post("/pdf/invoice").type("application/xml").send(fixtureXml);
      expectError(res, 500, "Internal server error");
      expect(log).toHaveBeenCalledWith("PDF service error:", error);
    } finally {
      render.mockRestore();
      log.mockRestore();
    }
  });

  it("rejects invalid JSON-to-XML input", async () => {
    const res = await request(app).post("/json-to-xml").send({ Faktura: { _cdata: {} } });
    expectError(res, 400, "Invalid invoice JSON");
  });
});
