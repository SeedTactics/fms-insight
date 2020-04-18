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
import { render, cleanup, waitForElement } from "@testing-library/react";
afterEach(cleanup);
import "@testing-library/jest-dom/extend-expect";

import Dashboard from "./Dashboard";
import { createTestStore } from "../../test-util";

it("renders the dashboard", async () => {
  window.matchMedia = (query) =>
    ({
      matches: query === "(min-width:600px)", // true for Hidden smDown screens
      addListener: () => undefined,
      removeListener: () => undefined,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);

  const store = await createTestStore();

  const result = render(
    <store.Provider>
      <Dashboard />
    </store.Provider>
  );

  await waitForElement(() => result.container.querySelector("div.rv-xy-plot svg"));
  expect(result.container.querySelector("div.rv-xy-plot")).toMatchSnapshot("current jobs plot");
  expect(result.getByTestId("stationoee-container")).toMatchSnapshot("station oee");
});
