import { afterAll, beforeAll, describe, expect, it, vi } from "vitest";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import type { Server } from "node:http";
import request from "supertest";
import { getDocument } from "pdfjs-dist/legacy/build/pdf.mjs";
import { createApp } from "./app.js";

const fixture = (name: string) => readFileSync(new URL(`../test/fixtures/${name}.xml`, import.meta.url), "utf8");
let server: Server;
beforeAll(async () => {
  vi.stubEnv("PDF_SERVICE_SECRET", undefined);
  server = createApp().listen(0, "127.0.0.1");
  await new Promise<void>(resolve => server.once("listening", resolve));
});
afterAll(async () => {
  await new Promise<void>((resolve, reject) => server.close(e => e ? reject(e) : resolve()));
  vi.unstubAllEnvs();
});

async function render(xml: string, nrKSeF?: string) {
  const response = await request(server).post("/pdf/invoice").send({ xml, nrKSeF });
  expect(response.status).toBe(200);
  expect(response.headers["content-type"]).toContain("application/pdf");
  const pdf = await getDocument({ data: new Uint8Array(response.body), useSystemFonts: false }).promise;
  try {
    const pages: string[] = [];
    const urls: string[] = [];
    for (let i = 1; i <= pdf.numPages; i++) {
      const page = await pdf.getPage(i);
      const text = await page.getTextContent();
      pages.push(text.items.map(item => "str" in item ? item.str : "").join(" ").replace(/\s+/g, " ").trim());
      urls.push(...(await page.getAnnotations()).flatMap(a => a.url ? [a.url] : []));
    }
    return { pages, text: pages.join(" "), compact: pages.join("").replace(/\s/g, ""), urls };
  } finally {
    await pdf.destroy();
  }
}

describe("PDF content from real MF and upstream invoices", () => {
  it.each([
    ["mf-fa1", "FV2022/02/150", "1666,66", "383,33", "2051,00"],
    ["mf-fa2", "FV2022/02/150", "1666,66", "383,33", "2051,00"],
    ["mf-fa3", "FV2026/02/150", "1666,66", "383,33", "2051,00"],
    ["mf-fa3-correction", "FK2026/03/200", "-162,60", "-37,40", "-200,00"],
    ["mf-fa3-advance", "FZ2026/02/150", "16260,16", "3739,84", "20000,00"],
    ["mf-fa3-eur", "FV2026/02/150", "13560,00", "3118,80", "16678,80"],
  ])("preserves invoice number, seller NIP, net, VAT and P_15: %s", async (name, number, net, vat, total) => {
    const pdf = await render(fixture(name));
    for (const value of [number, "9999999999", net, vat, total]) expect.soft(pdf.compact).toContain(value);
    if (name === "mf-fa3-eur") {
      expect(pdf.compact).toContain("16678,80EUR");
      expect(pdf.compact).toContain("14036,16"); // VAT converted to PLN in MF example 20.
    }
    expect(pdf.text).toContain("Kwota");
    expect(pdf.text).not.toMatch(/invoice\.(?:rows|header|summary)/);
    expect(pdf.pages.length).toBeGreaterThanOrEqual(1);
    for (const page of pdf.pages) expect(page.replace(/\d+ z \d+$/, "").trim().length).toBeGreaterThan(50);
  });

  it("preserves Polish glyphs and the last row on a multipage invoice, without a blank final page", async () => {
    const pdf = await render(fixture("upstream-fa3-large"));
    for (const value of ["FA/YUCFO-4342344706/03/2026", "5265877635", "27292,28", "6277,22", "33569,50", "Dróżdż", "Górecki", "Czarnków"]) {
      expect.soft(pdf.compact).toContain(value);
    }
    expect(pdf.pages.length).toBeGreaterThan(1);
    expect(pdf.compact.match(/HandcraftedWoodenGloves/g)).toHaveLength(13);
    expect(pdf.compact).toContain("13HandcraftedWoodenGloves");
    for (const page of pdf.pages) expect(page.replace(/\d+ z \d+$/, "").trim().length).toBeGreaterThan(50);
  });

  it("shows the line discount and discounted totals in a real PDF", async () => {
    const xml = fixture("mf-fa3")
      .replace("<P_13_1>1666.66</P_13_1>", "<P_13_1>99.89</P_13_1>")
      .replace("<P_14_1>383.33</P_14_1>", "<P_14_1>22.97</P_14_1>")
      .replace(/<P_13_3>.*?<\/P_13_3>|<P_14_3>.*?<\/P_14_3>/g, "")
      .replace("<P_15>2051</P_15>", "<P_15>122.86</P_15>")
      .replace(/<FaWiersz>[\s\S]*<\/FaWiersz>/, `<FaWiersz>
        <NrWierszaFa>1</NrWierszaFa><P_7>Usługa z rabatem</P_7><P_8A>szt.</P_8A>
        <P_8B>3</P_8B><P_9A>33.33</P_9A><P_10>0.10</P_10><P_11>99.89</P_11><P_12>23</P_12>
      </FaWiersz>`);
    const pdf = await render(xml);
    expect(pdf.text).toContain("Rabat");
    for (const value of ["33,33", "0,10", "99,89", "22,97", "122,86"])
      expect(pdf.compact).toContain(value);
  });

  it("always displays zero P_15 on an identity-only correction", async () => {
    const pdf = await render(fixture("mf-fa3-zero-correction"));
    expect(pdf.compact).toContain("FK2026/04/23");
    expect(pdf.compact).toMatch(/Korektakwotynależnościogółem:0PLN/);
  });

  it("keeps cents in a large exempt tax summary instead of rounding them again", async () => {
    const pdf = await render(fixture("fa3-rounding"));
    const summary = pdf.compact.split("Podsumowaniestawekpodatku")[1];
    expect(summary).toContain("100000000000000,03");
    expect(summary).not.toContain("100000000000000,05");
  });

  it.each([
    ["TEST", "https://qr-test.ksef.mf.gov.pl"],
    ["DEMO", "https://qr-demo.ksef.mf.gov.pl"],
    ["PRODUCTION", "https://qr.ksef.mf.gov.pl"],
  ])("embeds the exact environment verification link in the PDF: %s", async (environment, host) => {
    vi.stubEnv("KSEF_ENV", environment);
    vi.stubEnv("KSEF_QR_URL", undefined);
    const xml = fixture("mf-fa3");
    const pdf = await render(xml, "9999999999-20260215-ABCDEFABCDEF-AB");
    const hash = createHash("sha256").update(xml).digest("base64url");
    expect(pdf.urls).toContain(`${host}/invoice/9999999999/15-02-2026/${hash}`);
  });
});
