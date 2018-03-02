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

import { StationToolbar } from './StationToolbar';
import * as routes from '../../data/routes';

const basicState: routes.State = {
  current: routes.RouteLocation.StationMonitor,
  selected_station_type: routes.SelectedStationType.Inspection,
  selected_station_id: 4,
  station_queues: ["a"],
};

it('displays the toolbar for inspection with one queue', () => {
  const setRoute = jest.fn();

  let val = shallow(
    <StationToolbar
      current_route={basicState}
      setStationRoute={setRoute}
    />);
  expect(val).toMatchSnapshot('inspection toolbar');
});

it('displays the toolbar for wash with no queues', () => {
  const setRoute = jest.fn();
  const st = {...basicState,
    selected_station_type: routes.SelectedStationType.Wash,
    station_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      setStationRoute={setRoute}
    />);
  expect(val).toMatchSnapshot('wash toolbar');
});

it('displays the toolbar for load with three queues', () => {
  const setRoute = jest.fn();
  const st = {...basicState,
    selected_station_type: routes.SelectedStationType.Inspection,
    station_queues: ["a", "b", "c"],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      setStationRoute={setRoute}
    />);
  expect(val).toMatchSnapshot('load toolbar');
});

it("changes the station", () => {
  const setRoute = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      setStationRoute={setRoute}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Select)").first().prop("onChange") as any;
  onChange({target: {value: routes.SelectedStationType.Wash}});

  expect(setRoute).toHaveBeenCalledWith(
    routes.SelectedStationType.Wash,
    basicState.selected_station_id,
    basicState.station_queues
  );
});

it("changes the station number", () => {
  const setRoute = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      setStationRoute={setRoute}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Input)").first().prop("onChange") as any;
  onChange({target: {value: "12"}});

  expect(setRoute).toHaveBeenCalledWith(
    basicState.selected_station_type,
    12,
    basicState.station_queues
  );
});

it("changes the queues", () => {
  const setRoute = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      setStationRoute={setRoute}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Select)").last().prop("onChange") as any;

  onChange({target: {value: "4"}});
  expect(setRoute).toHaveBeenCalledWith(
    basicState.selected_station_type,
    basicState.selected_station_id,
    ["a", "", "", ""]
  );

  setRoute.mockReset();

  onChange({target: {value: "0"}});
  expect(setRoute).toHaveBeenCalledWith(
    basicState.selected_station_type,
    basicState.selected_station_id,
    []
  );
});