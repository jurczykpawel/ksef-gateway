import crypto from "crypto";
import { xml2js } from "xml-js";

const xmlName = /^[A-Za-z_][A-Za-z0-9._-]*$/;
const forbiddenCompactKeys = new Set(["_doctype", "_instruction", "_declaration", "_comment", "_cdata", "_parent"]);
const invalidXmlCharacters = /[^\u0009\u000A\u000D\u0020-\uD7FF\uE000-\uFFFD\u{10000}-\u{10FFFF}]/u;

// Bound every JSON value (including arrays, attributes and text), without recursion.
// The service emits its own declaration; only _attributes and _text are supported metadata.
export function validateInvoiceJson(json: unknown): void {
  type Kind = "document" | "element" | "attributes" | "text";
  const pending: { value: unknown; depth: number; kind: Kind; allowArray: boolean }[] = [
    { value: json, depth: 0, kind: "document", allowArray: false },
  ];
  let nodes = 0;
  const invalid = (): never => { throw new Error("Invalid invoice JSON"); };
  while (pending.length) {
    const { value, depth, kind, allowArray } = pending.pop()!;
    if (++nodes > 100000 || depth > 100) invalid();
    if (Array.isArray(value)) {
      if (!allowArray) invalid();
      for (const item of value) {
        pending.push({ value: item, depth: depth + 1, kind, allowArray: false });
      }
    } else if (value !== null && typeof value === "object") {
      if (kind === "text") invalid();
      const entries = Object.entries(value);
      if (kind === "document" && (entries.length !== 1 || entries[0][0] !== "Faktura")) invalid();
      for (const [key, child] of entries) {
        if (!xmlName.test(key) || (kind !== "attributes" && forbiddenCompactKeys.has(key))) invalid();
        const childKind: Kind = kind === "attributes" || key === "_text" ? "text"
          : key === "_attributes" ? "attributes" : "element";
        if ((childKind === "attributes" || kind === "document") &&
            (child === null || typeof child !== "object" || Array.isArray(child))) invalid();
        pending.push({ value: child, depth: depth + 1, kind: childKind,
          allowArray: kind !== "attributes" && kind !== "document" && childKind !== "attributes" });
      }
    } else {
      if (kind === "document" || kind === "attributes") invalid();
      if (typeof value === "string" && invalidXmlCharacters.test(value)) invalid();
    }
  }
}

export function escapeXmlAttribute(value: unknown): string {
  return String(value).replace(/&/g, "&amp;").replace(/</g, "&lt;")
    .replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&apos;")
    .replace(/\t/g, "&#9;").replace(/\n/g, "&#10;").replace(/\r/g, "&#13;");
}

// Reuse CIRFMF's stripPrefixes logic
export function stripPrefixes<T>(obj: T): T {
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

export function parseXmlString(xmlStr: string): unknown {
  return stripPrefixes(xml2js(xmlStr, { compact: true }));
}

let warnedUnknownEnvironment = false;

function qrBaseUrl(): string {
  if (process.env.KSEF_QR_URL) return process.env.KSEF_QR_URL;

  const env = (process.env.KSEF_ENV ?? "TEST").toUpperCase();
  switch (env) {
    case "PRODUCTION":
    case "PRD":
      return "https://qr.ksef.mf.gov.pl";
    case "DEMO":
      return "https://qr-demo.ksef.mf.gov.pl";
    case "TEST":
      return "https://qr-test.ksef.mf.gov.pl";
    default:
      if (!warnedUnknownEnvironment) {
        console.warn("Unknown KSEF_ENV in PDF service; falling back to TEST QR URLs.");
        warnedUnknownEnvironment = true;
      }
      return "https://qr-test.ksef.mf.gov.pl";
  }
}

// Build KSeF QR verification URL from invoice XML
// Format: {environment QR host}/invoice/{NIP_sprzedawcy}/{P_1_DD-MM-RRRR}/{SHA256-Base64URL}
export function buildVerificationUrl(xml: string, _nrKSeF: string): string {
  // SHA-256 of the invoice XML file
  const hash = crypto.createHash("sha256").update(xml, "utf8").digest();
  const hashB64Url = hash.toString("base64url");

  // Extract NIP and P_1 date from XML (not from KSeF number)
  const parsed = parseXmlString(xml) as any;
  const invoice = parsed?.Faktura;

  const nip = invoice?.Podmiot1?.DaneIdentyfikacyjne?.NIP?._text || "";
  const p1 = invoice?.Fa?.P_1?._text || ""; // YYYY-MM-DD

  // Convert P_1 from YYYY-MM-DD to DD-MM-YYYY
  const [year, month, day] = p1.split("-");
  const dateFormatted = `${day}-${month}-${year}`;

  return `${qrBaseUrl()}/invoice/${nip}/${dateFormatted}/${hashB64Url}`;
}
