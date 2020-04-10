/* Copyright (c) 2018, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

import * as React from "react";
import { wait, render, cleanup } from "@testing-library/react";
afterEach(cleanup);
import "@testing-library/jest-dom/extend-expect";
import { differenceInSeconds, addDays } from "date-fns";

import { mockComponent } from "../test-util";
import { initStore } from "../store/store";
import { loadMockData } from "../mock-data/load";
import * as serverSettings from "../data/server-settings";

jest.mock("./analysis/CostPerPiece", () => ({
  default: mockComponent("CostPerPiece"),
}));
jest.mock("./operations/Dashboard", () => ({
  default: mockComponent("Dashboard"),
}));
jest.mock("./analysis/Efficiency", () => ({
  default: mockComponent("Efficiency"),
}));
jest.mock("./analysis/DataExport", () => ({
  default: mockComponent("DataExport"),
}));

import App from "./App";
import { PledgeStatus } from "../store/middleware";
import { registerMockBackend } from "../data/backend";
import * as events from "../data/events";

it("renders the app shell", async () => {
  window.matchMedia = (query) =>
    ({
      matches: query === "(min-width:600px)", // true for Hidden smDown screens
      addListener: () => undefined,
      removeListener: () => undefined,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);

  const store = initStore({ useRouter: false });
  registerMockBackend();
  store.dispatch(events.loadLast30Days());
  store.dispatch(serverSettings.loadServerSettings());
  store.dispatch({
    type: serverSettings.ActionType.Load,
    pledge: {
      status: PledgeStatus.Completed,
      result: {
        fmsInfo: {
          name: "test",
          version: "1.2.2",
          requireScanAtWash: false,
          requireWorkorderBeforeAllowWashComplete: false,
        },
        latestVersion: {
          version: "1.2.3",
          date: new Date(Date.UTC(2018, 7, 6, 13, 32, 0)),
        },
      },
    },
  });

  const result = render(
    <store.Provider>
      <App demo={false} />
    </store.Provider>
  );

  expect(result.getByTestId("loading-icon")).toBeInTheDocument();

  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(addDays(new Date(Date.UTC(2018, 7, 6, 15, 39, 0)), -28), jan18);

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).FMS_INSIGHT_RESOLVE_MOCK_DATA(loadMockData(offsetSeconds));

  await wait(() => expect(result.queryByTestId("loading-icon")).not.toBeInTheDocument());

  expect(result.queryByTestId("mock-component-Dashboard")).not.toBeInTheDocument();
  expect(result.queryByTestId("mock-component-StationMonitor")).not.toBeInTheDocument();
  expect(result.queryByTestId("mock-component-Efficiency")).not.toBeInTheDocument();
  expect(result.queryByTestId("mock-component-CostPerPiece")).not.toBeInTheDocument();
});
