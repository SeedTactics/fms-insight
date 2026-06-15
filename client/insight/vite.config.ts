/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { playwright } from "@vitest/browser-playwright";
import { existsSync } from "fs";

export default defineConfig({
  root: ".",
  plugins: [react()],
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
    outDir: "dist", // relative to root
    chunkSizeWarningLimit: 1800,
    emptyOutDir: true,
  },
  test: {
    projects: [
      {
        test: {
          include: ["src/**/*.{test,spec}.ts", "src/**/*.{test,spec}.tsx"],
          name: "unit",
          environment: "jsdom",
          server: {
            deps: {
              // MUI imports react-transition-group's context via a directory-style subpath.
              // Inline these so Vitest resolves it through Vite instead of Node's ESM loader.
              inline: ["@mui/material", "react-transition-group"],
            },
          },
        },
      },
      {
        test: {
          include: ["test/integration/**/*.{test,spec}.tsx"],
          name: "integration",
          browser: {
            enabled: true,
            provider: playwright(),
            instances: [
              {
                browser: "chromium",
                provider: playwright({
                  launchOptions: {
                    executablePath: existsSync("/usr/bin/chromium")
                      ? "/usr/bin/chromium"
                      : undefined,
                  },
                }),
              },
            ],
          },
        },
      },
    ],
  },
});
