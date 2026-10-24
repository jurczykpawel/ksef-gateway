import { defineConfig } from "vitest/config";
import { fileURLToPath } from "node:url";

export default defineConfig({
  resolve: {
    alias: { "@shared": fileURLToPath(new URL("./lib/src/shared", import.meta.url)) },
  },
  test: {
    exclude: ["lib/**", "node_modules/**"],
    passWithNoTests: true,
  },
});
