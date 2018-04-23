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
import { shallow } from 'enzyme';

import { App, AppProps } from './App';
import * as routes from './data/routes';

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
    setRoute: jest.fn(),
  };
}

it('renders the dashboard', () => {
  const props = appProps(routes.RouteLocation.Dashboard);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("dashboard");
  expect(header).toMatchSnapshot("dashboard header");

  // tslint:disable-next-line:no-any
  const onTabChange = header.find("WithStyles(Tabs)").first().prop("onChange") as any;
  onTabChange(null, routes.RouteLocation.CostPerPiece);
  expect(props.setRoute).toHaveBeenCalledWith({ty: routes.RouteLocation.CostPerPiece, curSt: props.route});
});

it('renders the load monitor', () => {
  const props = appProps(routes.RouteLocation.LoadMonitor);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("station monitor");
  expect(header).toMatchSnapshot("station monitor header");
});

it('renders the inspection monitor', () => {
  const props = appProps(routes.RouteLocation.InspectionMonitor);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("inspection monitor");
  expect(header).toMatchSnapshot("inspection monitor header");
});

it('renders the wash monitor', () => {
  const props = appProps(routes.RouteLocation.WashMonitor);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("wash monitor");
  expect(header).toMatchSnapshot("wash monitor header");
});

it('renders the queue monitor', () => {
  const props = appProps(routes.RouteLocation.Queues);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("queue monitor");
  expect(header).toMatchSnapshot("queue monitor header");
});

it('renders the all material monitor', () => {
  const props = appProps(routes.RouteLocation.AllMaterial);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("all material monitor");
  expect(header).toMatchSnapshot("all material monitor header");
});

it('renders the cost per piece', () => {
  const props = appProps(routes.RouteLocation.CostPerPiece);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("cost per piece");
  expect(header).toMatchSnapshot("cost per piece header");
});

it('renders the efficiency', () => {
  const props = appProps(routes.RouteLocation.Efficiency);
  const val = shallow(<App {...props}/>);
  const header = val.find("Header").dive();
  expect(val).toMatchSnapshot("efficiency");
  expect(header).toMatchSnapshot("efficiency header");
});