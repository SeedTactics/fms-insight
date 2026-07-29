/**
 * Supported composition API for applications that embed FMS Insight with custom pages and
 * authenticated endpoints.
 */
export { bootstrapInsight } from "./bootstrap.js";
export { customState } from "./cell-status/custom-state.js";
export type { AppProps } from "./components/App.js";
export { defaultChooseModes } from "./components/ChooseMode.js";
export type { ChooseModeItem } from "./components/ChooseMode.js";
export { RouteLocation } from "./components/routes.js";
export { authenticatedFetch } from "./network/backend.js";
