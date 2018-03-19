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
  current: routes.RouteLocation.LoadMonitor,
  station_monitor: routes.StationMonitorType.LoadUnload,
  selected_load_id: 4,
  station_queues: ["a"],
  station_free_material: false,
};

it('displays the toolbar for load with one queue', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();

  let val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{"a": {}, "b": {}}}
      insp_types={["i1", "i2"]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);
  expect(val).toMatchSnapshot('load with one queue');
});

it('displays the load with no queues', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const st = {...basicState,
    station_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={["i1", "i2"]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);
  expect(val).toMatchSnapshot('load with no queues');
});

it('displays the toolbar for load with three queues', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.LoadMonitor,
    station_monitor: routes.StationMonitorType.LoadUnload,
    station_queues: ["a", "b", "c"],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{"a": {}, "b": {}, "c": {}, "d": {}}}
      insp_types={["i1", "i2"]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);
  expect(val).toMatchSnapshot('load toolbar');
});

it('displays the toolbar for wash', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.WashMonitor,
    station_monitor: routes.StationMonitorType.Wash,
    station_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={["i1", "i2"]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);
  expect(val).toMatchSnapshot('wash toolbar');
});

it('displays the toolbar for all inspection', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.InspectionMonitor,
    station_monitor: routes.StationMonitorType.Inspection,
    station_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={["i1", "i2"]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);
  expect(val).toMatchSnapshot('inspection all toolbar');
});

it('displays the toolbar for single inspection type', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.InspectionMonitor,
    station_monitor: routes.StationMonitorType.Inspection,
    selected_inspection_type: "i1",
    station_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={["i1", "i2"]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);
  expect(val).toMatchSnapshot('inspection i1 selected toolbar');
});

it("changes the station type", () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{}}
      insp_types={[]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Select)").first().prop("onChange") as any;

  onChange({target: {value: routes.StationMonitorType.Wash}});
  expect(displayWash).toHaveBeenCalled();
  displayWash.mockReset();

  onChange({target: {value: routes.StationMonitorType.Inspection}});
  expect(displayInsp).toHaveBeenCalledWith(undefined);
  displayInsp.mockReset();

  onChange({target: {value: routes.StationMonitorType.LoadUnload}});
  expect(displayLoad).toHaveBeenCalledWith(
    basicState.selected_load_id,
    basicState.station_queues,
    false
  );
  displayLoad.mockReset();
});

it("changes the load station number", () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{}}
      insp_types={[]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Input)").first().prop("onChange") as any;
  onChange({target: {value: "12"}});

  expect(displayLoad).toHaveBeenCalledWith(
    12,
    basicState.station_queues,
    false
  );

  expect(displayInsp).not.toHaveBeenCalled();
  expect(displayWash).not.toHaveBeenCalled();
});

it("changes the queues", () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{}}
      insp_types={[]}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Select)").last().prop("onChange") as any;

  onChange({target: {value: ["a", "b"]}});
  expect(displayLoad).toHaveBeenCalledWith(
    basicState.selected_load_id,
    ["a", "b"],
    false
  );

  displayLoad.mockReset();

  onChange({target: {value: []}});
  expect(displayLoad).toHaveBeenCalledWith(
    basicState.selected_load_id,
    [],
    false
  );

  expect(displayWash).not.toHaveBeenCalled();
  expect(displayInsp).not.toHaveBeenCalled();
});