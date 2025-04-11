/// <reference types="vitest" />
/// <reference types="vite/client" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import jotaiDebugLabel from "jotai/babel/plugin-debug-label";
import jotaiReactRefresh from "jotai/babel/plugin-react-refresh";

export default defineConfig({
  root: "src",
  plugins: [
    react({
      babel: { plugins: [jotaiDebugLabel, jotaiReactRefresh] },
    }),
  ],
  base: "/",
  server: {
    port: 1234,
    proxy: {
      "/api/v1/events": {
        target: "ws://localhost:5000",
        changeOrigin: true,
        ws: true,
      },
      "/api": {
        target: "http://localhost:5000",
        changeOrigin: true,
      },
    },
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
