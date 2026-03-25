import express from "express";

const app = express();
const PORT = parseInt(process.env.PORT || "3000", 10);

app.use(express.json({ limit: "10mb" }));
app.use(express.text({ type: "application/xml", limit: "10mb" }));

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

// TODO: Wire up CIRFMF ksef-pdf-generator library
// For now, placeholder endpoints that return 501

app.post("/pdf/invoice", async (req, res) => {
  const xml = typeof req.body === "string" ? req.body : req.body?.xml;

  if (!xml) {
    res.status(400).json({ error: "Missing invoice XML. Send as body or { xml: '...' }" });
    return;
  }

  // TODO: Import and use ksef-pdf-generator to convert XML -> PDF
  // const pdfBuffer = await generateInvoicePdf(xml);
  // res.contentType('application/pdf').send(pdfBuffer);

  res.status(501).json({
    error: "PDF generation not yet wired. Waiting for ksef-pdf-generator integration.",
  });
});

app.post("/pdf/upo", async (req, res) => {
  const xml = typeof req.body === "string" ? req.body : req.body?.xml;

  if (!xml) {
    res.status(400).json({ error: "Missing UPO XML. Send as body or { xml: '...' }" });
    return;
  }

  // TODO: Import and use ksef-pdf-generator to convert UPO XML -> PDF
  res.status(501).json({
    error: "UPO PDF generation not yet wired. Waiting for ksef-pdf-generator integration.",
  });
});

app.listen(PORT, "0.0.0.0", () => {
  console.log(`ksef-pdf-service listening on port ${PORT}`);
});
