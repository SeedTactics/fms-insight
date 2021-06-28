/**
 * @type {import('vite').UserConfig}
 */
import reactRefresh from "@vitejs/plugin-react-refresh";

export default {
  root: "src",
  plugins: [reactRefresh()],
  base: "./",
  server: {
    port: 1234,
  },
  build: {
    outDir: "../dist", // relative to root
    chunkSizeWarningLimit: 1800,
  },
};
