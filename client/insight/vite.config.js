/**
 * @type {import('vite').UserConfig}
 */
import reactRefresh from "@vitejs/plugin-react-refresh";
import legacy from "@vitejs/plugin-legacy";

export default {
  root: "src",
  plugins: [reactRefresh(), legacy({ targets: ["defaults", "not IE 11", "edge 18"] })],
  base: "./",
  server: {
    port: 1234,
  },
  build: {
    outDir: "../dist", // relative to root
    chunkSizeWarningLimit: 1800,
    emptyOutDir: true,
  },
};
