import { createApp } from "./app.js";

const PORT = parseInt(process.env.PORT || "3000", 10);

createApp().listen(PORT, "0.0.0.0", () => {
  console.log(`ksef-pdf-service listening on port ${PORT}`);
});
