import { createStore } from "jotai";
import type { AppProps } from "./components/App.js";
import { registerNetworkBackend } from "./network/backend.js";
import { fmsInformation, loadInfo } from "./network/server-settings.js";
import { render } from "./renderer.js";

/**
 * Starts one complete FMS Insight application with optional statically composed custom pages.
 */
export async function bootstrapInsight(
  appProps: AppProps = {},
  rootElement?: HTMLElement,
): Promise<void> {
  const root = rootElement ?? document.getElementById("root");
  if (!root) {
    throw new Error("Unable to start FMS Insight: no root element was provided or found");
  }

  registerNetworkBackend();
  const store = createStore();
  store.set(fmsInformation, await loadInfo());
  render(appProps, root, store);

  if ("serviceWorker" in navigator) {
    void navigator.serviceWorker.getRegistrations().then((registrations) => {
      for (const registration of registrations) {
        void registration.unregister();
      }
    });
  }
}

export { customState } from "./cell-status/custom-state.js";
export type { AppProps };
export { defaultChooseModes } from "./components/ChooseMode.js";
export type { ChooseModeItem } from "./components/ChooseMode.js";
export { RouteLocation } from "./components/routes.js";
export { authenticatedFetch } from "./network/backend.js";
