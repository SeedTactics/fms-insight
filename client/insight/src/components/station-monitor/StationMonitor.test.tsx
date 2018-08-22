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
import { render, cleanup, fireEvent, wait } from "react-testing-library";
afterEach(cleanup);
import { Provider } from "react-redux";
import { Simulate } from "react-dom/test-utils";
import "jest-dom/extend-expect";
import { mockComponent } from "../../test-util";

jest.mock("react-timeago", () => ({
  default: mockComponent("timeago")
}));
jest.mock("../DateTimeDisplay", () => ({
  default: mockComponent("DateTimeDisplay")
}));

import StationMonitor from "./StationMonitor";
import { createTestStore } from "../../test-util";
import { connect } from "../../store/store";

const ConnStatMonitor = connect(s => ({
  monitor_type: s.Route.station_monitor
}))(StationMonitor);

it("renders the load station page", async () => {
  const store = await createTestStore();

  const result = render(
    <Provider store={store}>
      <div>
        <ConnStatMonitor />
      </div>
    </Provider>
  );

  expect(result.getByTestId("stationmonitor-load")).toMatchSnapshot("load 1");

  const loadNum = result.getByPlaceholderText("Load Station Number") as HTMLInputElement;
  loadNum.value = "2";
  Simulate.change(loadNum);

  expect(result.getByTestId("stationmonitor-load")).toMatchSnapshot("load 2");

  loadNum.value = "3";
  Simulate.change(loadNum);

  expect(result.getByTestId("stationmonitor-load")).toMatchSnapshot("load 3");

  loadNum.value = "1";
  Simulate.change(loadNum);

  fireEvent.click(result.getByText("NBTGI", {exact: false}));

  await wait(() =>
    expect(result.queryByTestId("material-events-loading")).not.toBeInTheDocument()
  );

  expect(result.baseElement.querySelector("div[role='dialog']")).toMatchSnapshot("NBTGI dialog");

  /*
  fireEvent.click(result.getByText("Change Serial"));

  const enterSerial = result.getByLabelText("Serial") as HTMLInputElement;
  enterSerial.value = "ABCDEF";
  Simulate.change(enterSerial);
  Simulate.keyPress(enterSerial, {key: "Enter"});
  */
});
