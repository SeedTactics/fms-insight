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
import { render, cleanup, fireEvent, wait, within } from "react-testing-library";
afterEach(cleanup);
import { Provider } from "react-redux";
import { Simulate } from "react-dom/test-utils";
import "jest-dom/extend-expect";

import Efficiency from "./Efficiency";
import { createTestStore } from "../../test-util";
import LoadingIcon from "../LoadingIcon";

it("renders the cost/piece page", async () => {
  const store = await createTestStore();

  const result = render(
    <Provider store={store}>
      <div>
        <LoadingIcon />
        <Efficiency />
      </div>
    </Provider>
  );

  expect(result.getByTestId("completed-heatmap").querySelector("div.rv-xy-plot")).toBeEmpty();

  // now go to July 2018 which has the test store data
  const chooseMonth = result.getByPlaceholderText("Choose Month") as HTMLInputElement;
  chooseMonth.value = "2018-07";
  Simulate.change(chooseMonth);
  fireEvent.blur(chooseMonth);
  fireEvent.click(result.getByLabelText("Select Month"));
  await wait(() => expect(result.queryByTestId("loading-icon")).not.toBeInTheDocument());

  // part cycles
  fireEvent.click(within(result.getByTestId("part-cycle-chart")).getByText("Select Part"));
  fireEvent.click(
    within(document.getElementById("menu-Station-Cycles-cycle-chart-select") as HTMLElement).getByText("aaa-1")
  );
  expect(
    result.getByTestId("part-cycle-chart").querySelectorAll("g.rv-xy-plot__series > circle").length
  ).toBeGreaterThan(0);

  // pallet cycles
  fireEvent.click(within(result.getByTestId("pallet-cycle-chart")).getByText("Select Pallet"));
  fireEvent.click(
    within(document.getElementById("menu-Pallet-Cycles-cycle-chart-select") as HTMLElement).getByText("3")
  );
  expect(
    result.getByTestId("pallet-cycle-chart").querySelectorAll("g.rv-xy-plot__series > circle").length
  ).toBeGreaterThan(0);

  // station oee heatmap
  expect(
    result.getByTestId("station-oee-heatmap").querySelectorAll("g.rv-xy-plot__series--heatmap > rect").length
  ).toBeGreaterThan(0);
  fireEvent.click(within(result.getByTestId("station-oee-heatmap")).getByText("Actual"));
  fireEvent.click(
    within(document.getElementById("menu-Station-OEE-heatchart-planned-or-actual") as HTMLElement).getByText("Planned")
  );
  expect(
    result.getByTestId("station-oee-heatmap").querySelectorAll("g.rv-xy-plot__series--heatmap > rect").length
  ).toBeGreaterThan(0);

  // completed counts
  expect(
    result.getByTestId("completed-heatmap").querySelectorAll("g.rv-xy-plot__series--heatmap > rect").length
  ).toBeGreaterThan(0);
  fireEvent.click(within(result.getByTestId("completed-heatmap")).getByText("Actual"));
  fireEvent.click(
    within(document.getElementById("menu-Part-Production-heatchart-planned-or-actual") as HTMLElement).getByText(
      "Planned"
    )
  );
  expect(
    result.getByTestId("completed-heatmap").querySelectorAll("g.rv-xy-plot__series--heatmap > rect").length
  ).toBeGreaterThan(0);

  // inspection sankey
  fireEvent.click(within(result.getByTestId("inspection-sankey")).getByText("Select Inspection Type"));
  fireEvent.click(
    within(document.getElementById("menu-inspection-sankey-select-type") as HTMLElement).getByText("CMM")
  );
  fireEvent.click(within(result.getByTestId("inspection-sankey")).getByText("Select Part"));
  fireEvent.click(
    within(document.getElementById("menu-inspection-sankey-select-part") as HTMLElement).getByText("bbb")
  );
  expect(result.getByTestId("inspection-sankey").querySelectorAll("path.rv-sankey__link").length).toBeGreaterThan(0);
});
