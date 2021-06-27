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
};
