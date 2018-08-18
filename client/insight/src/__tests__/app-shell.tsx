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

import * as React from 'react';
import { render, cleanup, fireEvent } from 'react-testing-library';
afterEach(cleanup);
import { Provider } from 'react-redux';
import 'jest-dom/extend-expect';

function mockComponent(name: string): (props: {[key: string]: object}) => JSX.Element {
  return props => (
    <div data-testid={"mock-component-" + name}>
      {Object.getOwnPropertyNames(props).sort().map((p, idx) =>
        <span key={idx} data-prop={p}>
          {JSON.stringify(props[p], null, 2)}
        </span>
      )}
    </div>
  );
}

jest.mock("../components/cost-per-piece/CostPerPiece", () => ({
  default: mockComponent("CostPerPiece")
}));
jest.mock("../components/dashboard/Dashboard", () => ({
  default: mockComponent("Dashboard")
}));
jest.mock("../components/efficiency/Efficiency", () => ({
  default: mockComponent("Efficiency")
}));
jest.mock("../components/station-monitor/StationMonitor", () => ({
  default: mockComponent("StationMonitor")
}));
jest.mock("../components/data-export/DataExport", () => ({
  default: mockComponent("DataExport")
}));

(window as any).FMS_INSIGHT_DEMO_MODE = true;
import { initStore } from '../store/store';
import App from '../components/App';

it("renders the app shell", () => {
  const store = initStore();

  const result = render(
    <Provider store={store}>
      <App />
    </Provider>
  );

  expect(
    result.getByTestId("mock-component-Dashboard")
  ).toBeDefined();
  expect(
    result.queryByTestId("mock-component-StationMonitor")
  ).not.toBeInTheDocument();
  expect(
    result.queryByTestId("mock-component-Efficiency")
  ).not.toBeInTheDocument();
  expect(
    result.queryByTestId("mock-component-CostPerPiece")
  ).not.toBeInTheDocument();

  fireEvent.click(result.getByText("Station Monitor"));
  expect(
    result.queryByTestId("mock-component-StationMonitor")
  ).toBeInTheDocument();

  fireEvent.click(result.getByText("Efficiency"));
  expect(
    result.queryByTestId("mock-component-Efficiency")
  ).toBeInTheDocument();

  fireEvent.click(result.getByText("Cost/Piece"));
  expect(
    result.queryByTestId("mock-component-CostPerPiece")
  ).toBeInTheDocument();

  fireEvent.click(result.getByText("Dashboard"));
  expect(
    result.queryByTestId("mock-component-Dashboard")
  ).toBeInTheDocument();
});

