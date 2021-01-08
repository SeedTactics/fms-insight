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
import { render, cleanup, fireEvent, within, waitFor } from "@testing-library/react";
afterEach(cleanup);
import "@testing-library/jest-dom/extend-expect";

import Efficiency from "./Efficiency";
import { createTestStore } from "../../test-util";
import LoadingIcon from "../LoadingIcon";
import { RecoilRoot } from "recoil";

it("renders the efficiency page", async () => {
  const store = await createTestStore();

  const result = render(
    <store.Provider>
      <RecoilRoot>
        <div>
          <LoadingIcon />
          <Efficiency allowSetType={true} />
        </div>
      </RecoilRoot>
    </store.Provider>
  );

  expect(result.getByTestId("completed-heatmap").querySelector("div.rv-xy-plot")).toBeEmptyDOMElement();

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
});
