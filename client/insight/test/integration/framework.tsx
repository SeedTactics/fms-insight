import { ReactNode } from "react";
import { CssBaseline } from "@mui/material";
import { brown, green } from "@mui/material/colors";
import { ThemeProvider, createTheme } from "@mui/material/styles";
import { Provider, createStore } from "jotai";
import { render as browserRender } from "vitest-browser-react";

import type {
  ICurrentStatus,
  ILogEntry,
  IRecentHistoricData,
  IServerEvent,
} from "../../src/network/api.js";
import {
  onLoadCurrentSt,
  onLoadLast30Jobs,
  onLoadLast30Log,
  onServerEvent,
} from "../../src/cell-status/loading.js";
import { currentOperator } from "../../src/data/operators.js";
import { hideNonLoadingMaterialOnLoadStation } from "../../src/data/queue-material.js";
import type { RouteState } from "../../src/components/routes.js";
import { currentRoute, isDemoAtom } from "../../src/components/routes.js";
import type { FMSInfoAndUser } from "../../src/network/server-settings.js";
import { fmsInformation } from "../../src/network/server-settings.js";

const theme = createTheme({
  palette: {
    primary: green,
    secondary: brown,
  },
});

type JotaiStore = ReturnType<typeof createStore>;

export interface InsightTestData {
  readonly currentStatus?: Readonly<ICurrentStatus>;
  readonly recentHistoricData?: Readonly<IRecentHistoricData>;
  readonly last30Log?: ReadonlyArray<Readonly<ILogEntry>>;
  readonly serverEvents?: ReadonlyArray<Readonly<IServerEvent>>;
  readonly fmsInfo?: Partial<FMSInfoAndUser>;
  readonly route?: RouteState;
  readonly hideNonLoadingMaterial?: boolean;
  readonly operator?: string | null;
  readonly seedStore?: (store: JotaiStore) => void;
}

function defaultFmsInfo(): FMSInfoAndUser {
  return {
    name: "FMS Insight",
    version: "integration-test",
  };
}

export function createInsightTestStore(data: InsightTestData = {}): JotaiStore {
  const store = createStore();

  store.set(isDemoAtom, false);
  store.set(fmsInformation, { ...defaultFmsInfo(), ...data.fmsInfo });
  store.set(hideNonLoadingMaterialOnLoadStation, data.hideNonLoadingMaterial ?? false);
  store.set(currentOperator, data.operator ?? null);

  if (data.route) {
    store.set(currentRoute, data.route);
  }

  if (data.currentStatus) {
    store.set(onLoadCurrentSt, data.currentStatus);
  }
  if (data.recentHistoricData) {
    store.set(onLoadLast30Jobs, data.recentHistoricData);
  }
  if (data.last30Log) {
    store.set(onLoadLast30Log, data.last30Log);
  }
  for (const evt of data.serverEvents ?? []) {
    store.set(onServerEvent, { evt, now: new Date(), expire: false });
  }

  data.seedStore?.(store);

  return store;
}

type BrowserRenderResult = Awaited<ReturnType<typeof browserRender>>;

export async function renderInsightPage(
  page: ReactNode,
  data: InsightTestData = {},
): Promise<BrowserRenderResult & { readonly store: JotaiStore }> {
  const store = createInsightTestStore(data);
  const rendered = await browserRender(
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <Provider store={store}>{page}</Provider>
    </ThemeProvider>,
  );

  return {
    ...rendered,
    store,
  };
}
