// @ts-check

import eslint from "@eslint/js";
import tseslint from "typescript-eslint";
import prettier from "eslint-config-prettier";
import react from "eslint-plugin-react";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";

export default tseslint.config(
  eslint.configs.recommended,
  {
    settings: {
      react: {
        version: "detect",
      },
    },
  },
  tseslint.configs.recommendedTypeChecked,
  {
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_" }],
      "@typescript-eslint/unbound-method": ["error", { ignoreStatic: true }],
    },
  },
  {
    plugins: {
      // @ts-ignore
      "react-hooks": reactHooks,
    },
    // @ts-ignore
    rules: reactHooks.configs.recommended.rules,
  },
  // @ts-ignore
  react.configs.flat.recommended,
  // @ts-ignore
  react.configs.flat["jsx-runtime"],
  reactRefresh.configs.vite,
  prettier,
);
