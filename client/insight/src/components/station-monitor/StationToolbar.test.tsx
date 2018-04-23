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
import { Set } from 'immutable';

import { StationToolbar } from './StationToolbar';
import * as routes from '../../data/routes';

const basicState: routes.State = {
  current: routes.RouteLocation.LoadMonitor,
  station_monitor: routes.StationMonitorType.LoadUnload,
  selected_load_id: 4,
  load_queues: ["a"],
  load_free_material: false,
  standalone_queues: ["b"],
  standalone_free_material: false,
};

it('displays the toolbar for load with one queue', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();

  let val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{"a": {}, "b": {}}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('load with one queue');
});

it('displays the load with no queues', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();
  const st = {...basicState,
    load_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('load with no queues');
});

it('displays the toolbar for load with three queues', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.LoadMonitor,
    station_monitor: routes.StationMonitorType.LoadUnload,
    load_queues: ["a", "b", "c"],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{"a": {}, "b": {}, "c": {}, "d": {}}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('load toolbar');
});

it('displays the toolbar for wash', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.WashMonitor,
    station_monitor: routes.StationMonitorType.Wash,
    load_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('wash toolbar');
});

it('displays the toolbar for all material', () => {
  const st = {...basicState,
    current_route: routes.RouteLocation.AllMaterial,
    station_monitor: routes.StationMonitorType.AllMaterial,
    load_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={jest.fn()}
      displayInspection={jest.fn()}
      displayWash={jest.fn()}
      displayQueues={jest.fn()}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('all material toolbar');
});

it('displays the toolbar for all inspection', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.InspectionMonitor,
    station_monitor: routes.StationMonitorType.Inspection,
    load_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('inspection all toolbar');
});

it('displays the toolbar for single inspection type', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.InspectionMonitor,
    station_monitor: routes.StationMonitorType.Inspection,
    selected_inspection_type: "i1",
    load_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('inspection i1 selected toolbar');
});

it('displays an empty queue page', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.Queues,
    station_monitor: routes.StationMonitorType.Queues,
    standalone_queues: [],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('empty queues');
});

it('displays the toolbar for queue page with three queues', () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();
  const st = {...basicState,
    current_route: routes.RouteLocation.Queues,
    station_monitor: routes.StationMonitorType.Queues,
    standalone_queues: ["a", "b", "c"],
  };

  let val = shallow(
    <StationToolbar
      current_route={st}
      queues={{"a": {}, "b": {}, "c": {}, "d": {}}}
      insp_types={Set(["i1", "i2"])}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);
  expect(val).toMatchSnapshot('queue toolbar');
});

it("changes the station type", () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{}}
      insp_types={Set()}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
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
    basicState.load_queues,
    false
  );
  displayLoad.mockReset();

  onChange({target: {value: routes.StationMonitorType.Queues}});
  expect(displayQueues).toHaveBeenCalledWith(
    basicState.standalone_queues,
    false
  );
  displayQueues.mockReset();
});

it("changes the load station number", () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{}}
      insp_types={Set()}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Input)").first().prop("onChange") as any;
  onChange({target: {value: "12"}});

  expect(displayLoad).toHaveBeenCalledWith(
    12,
    basicState.load_queues,
    false
  );

  expect(displayInsp).not.toHaveBeenCalled();
  expect(displayWash).not.toHaveBeenCalled();
  expect(displayQueues).not.toHaveBeenCalled();
});

it("changes the load queues", () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();

  const val = shallow(
    <StationToolbar
      current_route={basicState}
      queues={{}}
      insp_types={Set()}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
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
  expect(displayQueues).not.toHaveBeenCalled();
});

it("changes the standalone queues", () => {
  const displayLoad = jest.fn();
  const displayInsp = jest.fn();
  const displayWash = jest.fn();
  const displayQueues = jest.fn();

  const st = {...basicState,
    current_route: routes.RouteLocation.Queues,
    station_monitor: routes.StationMonitorType.Queues,
  };

  const val = shallow(
    <StationToolbar
      current_route={st}
      queues={{}}
      insp_types={Set()}
      displayLoadStation={displayLoad}
      displayInspection={displayInsp}
      displayWash={displayWash}
      displayQueues={displayQueues}
      displayAllMaterial={jest.fn()}
      operators={Set(["o1", "o2"])}
      currentOperator="o1"
      setOperator={jest.fn()}
      removeOperator={jest.fn()}
    />);

  // tslint:disable-next-line:no-any
  const onChange = val.find("WithStyles(Select)").last().prop("onChange") as any;

  onChange({target: {value: ["a", "b"]}});
  expect(displayQueues).toHaveBeenCalledWith(
    ["a", "b"],
    false
  );

  displayQueues.mockReset();

  onChange({target: {value: []}});
  expect(displayQueues).toHaveBeenCalledWith(
    [],
    false
  );

  expect(displayWash).not.toHaveBeenCalled();
  expect(displayInsp).not.toHaveBeenCalled();
  expect(displayLoad).not.toHaveBeenCalled();
});