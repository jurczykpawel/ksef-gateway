// @ts-check
import { defineConfig } from "astro/config";
import sitemap from "@astrojs/sitemap";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  site: "https://ksef-gateway.dev",
  output: "static",
  trailingSlash: "never",
  integrations: [sitemap()],
  devToolbar: { enabled: false },
  vite: {
    plugins: [tailwindcss()],
  },
  build: {
    inlineStylesheets: "auto",
  },
});
