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
import { RouteLocation } from "../../data/routes";

const ConnStatMonitor = connect(s => ({
  route_loc: s.Route.current,
  showToolbar: true
}))(StationMonitor);

it("renders the load station page", async () => {
  const store = await createTestStore();
  store.dispatch({ type: RouteLocation.Station_LoadMonitor, payload: { num: 1 } });

  const result = render(
    <store.Provider>
      <div>
        <ConnStatMonitor />
      </div>
    </store.Provider>
  );

  expect(result.getByTestId("stationmonitor-load")).toMatchSnapshot("load 1");

  const loadNum = result.getByPlaceholderText("Load Station Number") as HTMLInputElement;
  loadNum.value = "2";
  Simulate.change(loadNum);

  expect(result.getByTestId("stationmonitor-load")).toMatchSnapshot("load 2");

  loadNum.value = "3";
  Simulate.change(loadNum);

  expect(result.getByTestId("stationmonitor-load")).toMatchSnapshot("load 3");

  // go back to load 1 and open a queue
  loadNum.value = "1";
  Simulate.change(loadNum);
  fireEvent.click(result
    .getByTestId("station-monitor-queue-select")
    .querySelector("div[role='button']") as HTMLElement);
  fireEvent.click(
    within(document.getElementById("menu-station-monitor-queue-select") as HTMLElement).getByText("Queue1")
  );

  expect(result.getByTestId("stationmonitor-load")).toMatchSnapshot("load 1 and queue");

  fireEvent.click(result.getByText("NBTGI", { exact: false }));

  await wait(() => expect(result.queryByTestId("material-events-loading")).not.toBeInTheDocument());

  expect(result.baseElement.querySelector("div[role='dialog']")).toMatchSnapshot("NBTGI dialog");

  /*
  fireEvent.click(result.getByText("Change Serial"));

  const enterSerial = result.getByLabelText("Serial") as HTMLInputElement;
  enterSerial.value = "ABCDEF";
  Simulate.change(enterSerial);
  Simulate.keyPress(enterSerial, {key: "Enter"});
  */
});

it("renders the inspection page", async () => {
  const store = await createTestStore();
  store.dispatch({ type: RouteLocation.Station_LoadMonitor, payload: { num: 1 } });

  const result = render(
    <store.Provider>
      <div>
        <ConnStatMonitor />
      </div>
    </store.Provider>
  );

  fireEvent.click(result.getByText("Load Station"));

  fireEvent.click(
    within(document.getElementById("menu-choose-station-type-select") as HTMLElement).getByText("Inspection")
  );

  // inspection page only looks last 36 hours, but all events are in July 2018 so it will be empty
  // TODO: actually test inspection page

  expect(result.getByTestId("stationmonitor-inspection")).toMatchSnapshot("empty inspection");
});

it("renders the wash page", async () => {
  const store = await createTestStore();
  store.dispatch({ type: RouteLocation.Station_LoadMonitor, payload: { num: 1 } });

  const result = render(
    <store.Provider>
      <div>
        <ConnStatMonitor />
      </div>
    </store.Provider>
  );

  fireEvent.click(result.getByText("Load Station"));

  fireEvent.click(within(document.getElementById("menu-choose-station-type-select") as HTMLElement).getByText("Wash"));

  // wash page only looks last 36 hours, but all events are in July 2018 so it will be empty
  // TODO: actually test inspection page

  expect(result.getByTestId("stationmonitor-wash")).toMatchSnapshot("empty wash");
});

it("renders the queues page", async () => {
  const store = await createTestStore();
  store.dispatch({ type: RouteLocation.Station_LoadMonitor, payload: { num: 1 } });

  const result = render(
    <store.Provider>
      <div>
        <ConnStatMonitor />
      </div>
    </store.Provider>
  );

  fireEvent.click(result.getByText("Load Station"));

  fireEvent.click(
    within(document.getElementById("menu-choose-station-type-select") as HTMLElement).getByText("Queues")
  );

  expect(result.getByTestId("stationmonitor-queues")).toMatchSnapshot("empty queues");

  fireEvent.click(result
    .getByTestId("station-monitor-queue-select")
    .querySelector("div[role='button']") as HTMLElement);

  fireEvent.click(
    within(document.getElementById("menu-station-monitor-queue-select") as HTMLElement).getByText("Queue1")
  );

  expect(result.getByTestId("stationmonitor-queues")).toMatchSnapshot("with Queue1");

  // open the dialog
  fireEvent.click(result.getByText("MZQGQ", { exact: false }));

  await wait(() => expect(result.queryByTestId("material-events-loading")).not.toBeInTheDocument());

  expect(result.baseElement.querySelector("div[role='dialog']")).toMatchSnapshot("MZQGQ dialog");
});

it("renders the all material page", async () => {
  const store = await createTestStore();

  const result = render(
    <store.Provider>
      <div>
        <ConnStatMonitor />
      </div>
    </store.Provider>
  );

  expect(result.getByTestId("stationmonitor-allmaterial")).toMatchSnapshot("all material");
});
