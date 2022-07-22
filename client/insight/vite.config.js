/// <reference types="vitest" />
/// <reference types="vite/client" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  root: "src",
  plugins: [react()],
  base: "/",
  server: {
    port: 1234,
  },
  build: {
    outDir: "../dist", // relative to root
    chunkSizeWarningLimit: 1800,
    emptyOutDir: true,
  },
  test: {
    environment: "jsdom",
  },
});
