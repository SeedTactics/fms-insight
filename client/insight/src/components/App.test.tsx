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

function mockComponent(name: string): (props: {[key: string]: object}) => JSX.Element {
  return props => (
    <div data-mock-component={name}>
      {Object.getOwnPropertyNames(props).sort().map(p =>
        <span data-prop={p}>
          {JSON.stringify(props[p], null, 2)}
        </span>
      )}
    </div>
  );
}

jest.mock("./LoadingIcon", () => ({
  default: mockComponent("LoadingIcon")
}));
jest.mock("./cost-per-piece/CostPerPiece", () => ({
  default: mockComponent("CostPerPiece")
}));
jest.mock("./dashboard/Dashboard", () => ({
  default: mockComponent("Dashboard")
}));
jest.mock("./efficiency/Efficiency", () => ({
  default: mockComponent("Efficiency")
}));
jest.mock("./station-monitor/StationMonitor", () => ({
  default: mockComponent("StationMonitor")
}));
jest.mock("./data-export/DataExport", () => ({
  default: mockComponent("DataExport")
}));

import * as rtest from 'react-testing-library';

import { App, AppProps, TabType } from './App';
import * as routes from '../data/routes';

function appProps(current: routes.RouteLocation): AppProps {
  let monitor: routes.StationMonitorType = routes.StationMonitorType.LoadUnload;
  switch (current) {
    case routes.RouteLocation.InspectionMonitor:
      monitor = routes.StationMonitorType.Inspection;
      break;
    case routes.RouteLocation.WashMonitor:
      monitor = routes.StationMonitorType.Wash;
  }
  return {
    route: {
      current,
      station_monitor: monitor,
      selected_load_id: 5,
      load_queues: ["q1", "q1"],
      load_free_material: false,
      standalone_queues: ["r1", "r2"],
      standalone_free_material: true,
    },
    fmsInfo: {
      name: "apptest",
      version: "1.2.3.4",
      requireScanAtWash: false,
      requireWorkorderBeforeAllowWashComplete: false,
    },
    latestVersion: {
      date: new Date(2018, 7, 5, 15, 45, 3),
      version: "9.8.7"
    },
    setRoute: jest.fn(),
  };
}

afterEach(rtest.cleanup);

describe("Application", () => {
  it('renders the app', () => {
    const props = appProps(routes.RouteLocation.Dashboard);
    const result = rtest.render(<App {...props}/>);
    expect(result.container).toMatchSnapshot("dashboard");
  });

  it("switches a tab", () => {
    const props = appProps(routes.RouteLocation.Dashboard);
    const result = rtest.render(<App {...props}/>);

    rtest.fireEvent.click(result.getByText("Cost/Piece"));
    expect(props.setRoute).toHaveBeenCalledWith({ty: TabType.CostPerPiece, curSt: props.route});

    rtest.fireEvent.click(result.getByText("Station Monitor"));
    expect(props.setRoute).toHaveBeenCalledWith({ty: TabType.StationMonitor, curSt: props.route});

    rtest.fireEvent.click(result.getByText("Data Export"));
    expect(props.setRoute).toHaveBeenCalledWith({ty: TabType.DataExport, curSt: props.route});

    rtest.fireEvent.click(result.getByText("Efficiency"));
    expect(props.setRoute).toHaveBeenCalledWith({ty: TabType.Efficiency, curSt: props.route});

    rtest.fireEvent.click(result.getByText("Dashboard"));
    expect(props.setRoute).toHaveBeenCalledWith({ty: TabType.Dashboard, curSt: props.route});
  });

});