import crypto from "crypto";
import { xml2js } from "xml-js";

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

// Build KSeF QR verification URL from invoice XML
// Format: https://qr-test.ksef.mf.gov.pl/invoice/{NIP_sprzedawcy}/{P_1_DD-MM-RRRR}/{SHA256-Base64URL}
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

  const qrBaseUrl = process.env.KSEF_QR_URL || "https://qr-test.ksef.mf.gov.pl";
  return `${qrBaseUrl}/invoice/${nip}/${dateFormatted}/${hashB64Url}`;
}
