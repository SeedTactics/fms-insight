process.env.NODE_ENV = "production";
process.env.REACT_APP_MOCK_DATA = "true";
process.env.PUBLIC_URL = "/demo";

const webpackProdConfig = require("react-scripts-ts/config/webpack.config.prod.js");

module.exports = Object.assign({}, webpackProdConfig, {
  entry: {
      app: [
        require.resolve("react-scripts-ts/config/polyfills"),
        "./src/index.tsx",
      ],
      mockData: "./src/mock-data/index.ts",
  }
});