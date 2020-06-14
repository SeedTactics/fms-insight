/* Copyright (c) 2019, John Lenz

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
import { render, cleanup, fireEvent, within, waitFor } from "@testing-library/react";
afterEach(cleanup);
import { Simulate } from "react-dom/test-utils";
import "@testing-library/jest-dom/extend-expect";

import CostPerPiece from "./CostPerPiece";
import { createTestStore } from "../../test-util";
import LoadingIcon from "../LoadingIcon";

it("renders the cost/piece page", async () => {
  const store = await createTestStore();

  const result = render(
    <store.Provider>
      <div>
        <LoadingIcon />
        <CostPerPiece />
      </div>
    </store.Provider>
  );

  expect(result.getByTestId("part-cost-table").querySelector("tbody")).toBeEmptyDOMElement();

  // now go to July 2018 which has the test store data
  fireEvent.click(result.getByTestId("open-month-select"));
  await waitFor(() => expect(result.queryByTestId("select-month-dialog-choose-month")).toBeInTheDocument());
  // go to 2018
  let curYear = new Date().getFullYear();
  while (curYear > 2018) {
    fireEvent.click(result.getByTestId("select-month-dialog-previous-year"));
    curYear -= 1;
  }
  await waitFor(() => {
    const curYearElem = result.queryByTestId("select-month-dialog-current-year");
    if (curYearElem) {
      expect(curYearElem.innerHTML).toBe("2018");
    } else {
      expect(curYearElem).not.toBeUndefined();
    }
  });
  fireEvent.click(within(result.getByTestId("select-month-dialog-choose-month")).getByText("Jul"));
  await waitFor(() => expect(result.queryByTestId("select-month-dialog-current-year")).not.toBeInTheDocument());
  fireEvent.click(result.getByLabelText("Select Month"));
  await waitFor(() => expect(result.queryByTestId("loading-icon")).not.toBeInTheDocument());

  // add cost inputs
  const numOper = result.getByLabelText("Number of Operators") as HTMLInputElement;
  numOper.value = "3";
  Simulate.change(numOper);
  const costPerOper = result.getByLabelText("Cost per operator per hour") as HTMLInputElement;
  costPerOper.value = "50";
  Simulate.change(costPerOper);
  const autoCost = result.getByLabelText("Cost for automated handling system per year") as HTMLInputElement;
  autoCost.value = "10000";
  Simulate.change(autoCost);

  const machRow = within(result.getByTestId("station-cost-table"))
    .getByText("Machine")
    .closest("tr") as HTMLTableRowElement;
  const machCost = machRow.cells[1].querySelector("input") as HTMLInputElement;
  machCost.value = "50000";
  Simulate.change(machCost);

  const aaaRow = within(result.getByTestId("part-cost-table")).getByText("aaa").closest("tr") as HTMLTableRowElement;
  const aaaMatCost = aaaRow.cells[1].querySelector("input") as HTMLInputElement;
  aaaMatCost.value = "40";
  Simulate.change(aaaMatCost);

  expect(result.getByTestId("part-cost-table")).toMatchSnapshot("calculated costs");
});
