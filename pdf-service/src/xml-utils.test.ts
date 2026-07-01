import { describe, it, expect, afterEach } from "vitest";
import crypto from "crypto";
import { readFileSync } from "fs";
import { stripPrefixes, parseXmlString, buildVerificationUrl } from "./xml-utils.js";

const fixtureXml = readFileSync(
  new URL("../test/fixtures/invoice-fa3.xml", import.meta.url),
  "utf8"
);

describe("stripPrefixes", () => {
  it("strips a namespace prefix from an object key", () => {
    expect(stripPrefixes({ "ns:Faktura": "value" })).toEqual({ Faktura: "value" });
  });

  it("leaves keys without a prefix untouched", () => {
    expect(stripPrefixes({ Faktura: "value" })).toEqual({ Faktura: "value" });
  });

  it("strips prefixes recursively in nested objects", () => {
    expect(stripPrefixes({ "a:Foo": { "b:Bar": { "c:Baz": 1 } } })).toEqual({
      Foo: { Bar: { Baz: 1 } },
    });
  });

  it("strips prefixes inside arrays of objects", () => {
    expect(stripPrefixes({ "a:Items": [{ "b:Id": 1 }, { "b:Id": 2 }] })).toEqual({
      Items: [{ Id: 1 }, { Id: 2 }],
    });
  });

  it("keeps only the segment right after the first colon (XML QNames never have a second one)", () => {
    expect(stripPrefixes({ "a:b:Foo": 1 })).toEqual({ b: 1 });
  });

  it("leaves xml-js compact-format keys (_attributes, _text) untouched", () => {
    expect(stripPrefixes({ "ns:Tag": { _attributes: { id: "1" }, _text: "hi" } })).toEqual({
      Tag: { _attributes: { id: "1" }, _text: "hi" },
    });
  });

  it("passes through primitives, null and undefined unchanged", () => {
    expect(stripPrefixes(42)).toBe(42);
    expect(stripPrefixes("plain")).toBe("plain");
    expect(stripPrefixes(null)).toBe(null);
    expect(stripPrefixes(undefined)).toBe(undefined);
    expect(stripPrefixes(true)).toBe(true);
  });

  it("returns an empty array for an empty array", () => {
    expect(stripPrefixes([])).toEqual([]);
  });
});

describe("parseXmlString", () => {
  it("parses namespaced XML into compact JSON with prefixes stripped", () => {
    const xml = '<ns:Root xmlns:ns="urn:test"><ns:Child>hello</ns:Child></ns:Root>';
    const result = parseXmlString(xml) as any;
    expect(result.Root.Child._text).toBe("hello");
    expect(result.Root["ns:Child"]).toBeUndefined();
  });

  it("parses the FA(3) example fixture into a Faktura root with Naglowek and Fa sections", () => {
    const result = parseXmlString(fixtureXml) as any;
    expect(result.Faktura).toBeDefined();
    expect(result.Faktura.Naglowek.KodFormularza._attributes.kodSystemowy).toBe("FA (3)");
    expect(result.Faktura.Podmiot1.DaneIdentyfikacyjne.NIP._text).toBe("1234567890");
    expect(result.Faktura.Fa.P_1._text).toBe("2026-03-24");
    expect(result.Faktura.Fa.FaWiersz).toHaveLength(2);
  });

  it("throws on malformed XML", () => {
    expect(() => parseXmlString("<Faktura><Unclosed></Faktura>")).toThrow();
  });
});

describe("buildVerificationUrl", () => {
  afterEach(() => {
    delete process.env.KSEF_QR_URL;
  });

  it("builds a URL containing the seller NIP and DD-MM-YYYY formatted P_1 date", () => {
    const url = buildVerificationUrl(fixtureXml, "ignored-ksef-number");
    expect(url).toContain("/invoice/1234567890/24-03-2026/");
  });

  it("uses the default qr-test.ksef.mf.gov.pl base URL when KSEF_QR_URL is unset", () => {
    delete process.env.KSEF_QR_URL;
    const url = buildVerificationUrl(fixtureXml, "ignored");
    expect(url.startsWith("https://qr-test.ksef.mf.gov.pl/invoice/")).toBe(true);
  });

  it("respects KSEF_QR_URL env override for the base URL", () => {
    process.env.KSEF_QR_URL = "https://qr.ksef.mf.gov.pl";
    const url = buildVerificationUrl(fixtureXml, "ignored");
    expect(url.startsWith("https://qr.ksef.mf.gov.pl/invoice/")).toBe(true);
  });

  it("hashes the exact XML bytes as SHA-256 base64url, independent of the KSeF number argument", () => {
    const expectedHash = crypto.createHash("sha256").update(fixtureXml, "utf8").digest("base64url");
    const url = buildVerificationUrl(fixtureXml, "this-argument-is-unused-by-design");
    expect(url.endsWith(`/${expectedHash}`)).toBe(true);

    const urlWithDifferentKsefNumber = buildVerificationUrl(fixtureXml, "completely-different-number");
    expect(urlWithDifferentKsefNumber).toBe(url);
  });

  it("produces a different hash when the XML content changes by even one byte", () => {
    const urlA = buildVerificationUrl(fixtureXml, "x");
    const urlB = buildVerificationUrl(fixtureXml + " ", "x");
    expect(urlA).not.toBe(urlB);
  });

  it("renders an empty NIP segment when Podmiot1 NIP is missing", () => {
    const xml = '<Faktura><Fa><P_1>2026-01-02</P_1></Fa></Faktura>';
    const url = buildVerificationUrl(xml, "x");
    expect(url).toContain("/invoice//02-01-2026/");
  });
});
