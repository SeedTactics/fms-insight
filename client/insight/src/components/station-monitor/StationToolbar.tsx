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
import Select from "@material-ui/core/Select";
import MenuItem from "@material-ui/core/MenuItem";
import Input from "@material-ui/core/Input";
import FormControl from "@material-ui/core/FormControl";
import { HashSet } from "prelude-ts";

import * as routes from "../../data/routes";
import { Store, connect } from "../../store/store";
import * as api from "../../data/api";

const toolbarStyle = {
  display: "flex",
  backgroundColor: "#E0E0E0",
  paddingLeft: "24px",
  paddingRight: "24px",
  paddingBottom: "4px",
  height: "2.5em",
  alignItems: "flex-end" as "flex-end"
};

const inHeaderStyle = {
  display: "flex",
  flexGrow: 1,
  alignSelf: "center",
  alignItems: "flex-end" as "flex-end"
};

interface StationToolbarProps {
  readonly full: boolean;
  readonly allowChangeType: boolean;
  readonly current_route: routes.State;
  readonly queues: { [key: string]: api.IQueueSize };
  readonly insp_types: HashSet<string>;

  readonly displayLoadStation: (num: number, queues: ReadonlyArray<string>, freeMaterial: boolean) => void;
  readonly displayInspection: (type: string | undefined) => void;
  readonly displayWash: () => void;
  readonly displayQueues: (queues: ReadonlyArray<string>, freeMaterial: boolean) => void;
  readonly displayAllMaterial: () => void;
}

const freeMaterialSym = "@@insight_free_material@@";
const allInspSym = "@@all_inspection_display@@";

enum StationMonitorType {
  LoadUnload = "LoadUnload",
  Inspection = "Inspection",
  Wash = "Wash",
  Queues = "Queues",
  AllMaterial = "AllMaterial"
}

function StationToolbar(props: StationToolbarProps) {
  const queueNames = Object.keys(props.queues).sort();

  function setStation(s: string) {
    const type = s as StationMonitorType;
    switch (type) {
      case StationMonitorType.LoadUnload:
        props.displayLoadStation(
          props.current_route.selected_load_id,
          props.current_route.load_queues,
          props.current_route.load_free_material
        );
        break;

      case StationMonitorType.Inspection:
        props.displayInspection(props.current_route.selected_insp_type);
        break;

      case StationMonitorType.Wash:
        props.displayWash();
        break;

      case StationMonitorType.Queues:
        props.displayQueues(props.current_route.standalone_queues, props.current_route.standalone_free_material);
        break;

      case StationMonitorType.AllMaterial:
        props.displayAllMaterial();
        break;
    }
  }

  function setLoadNumber(valStr: string) {
    const val = parseFloat(valStr);
    if (!isNaN(val) && isFinite(val)) {
      props.displayLoadStation(val, props.current_route.load_queues, props.current_route.load_free_material);
    }
  }

  function setInspType(type: string) {
    props.displayInspection(type === allInspSym ? undefined : type);
  }

  let curLoadQueueCount = props.current_route.load_queues.length;
  if (curLoadQueueCount > 3) {
    curLoadQueueCount = 3;
  }

  let curStandaloneCount = props.current_route.standalone_queues.length;
  if (curStandaloneCount > 3) {
    curStandaloneCount = 3;
  }

  // the material-ui type bindings specify `e.target.value` to have type string, but
  // when multiple selects are enabled it is actually a type string[]
  // tslint:disable-next-line:no-any
  function setLoadQueues(newQueuesAny: any) {
    const newQueues = newQueuesAny as ReadonlyArray<string>;
    const free = newQueues.includes(freeMaterialSym);
    props.displayLoadStation(
      props.current_route.selected_load_id,
      newQueues.slice(0, 3).filter(q => q !== freeMaterialSym),
      free
    );
  }

  let loadqueues: string[] = [...props.current_route.load_queues];
  if (props.current_route.load_free_material) {
    loadqueues.push(freeMaterialSym);
  }

  // tslint:disable-next-line:no-any
  function setStandaloneQueues(newQueuesAny: any) {
    const newQueues = newQueuesAny as ReadonlyArray<string>;
    const free = newQueues.includes(freeMaterialSym);
    props.displayQueues(newQueues.slice(0, 3).filter(q => q !== freeMaterialSym), free);
  }

  let standalonequeues: string[] = [...props.current_route.standalone_queues];
  if (props.current_route.standalone_free_material) {
    standalonequeues.push(freeMaterialSym);
  }

  let curType = StationMonitorType.LoadUnload;
  switch (props.current_route.current) {
    case routes.RouteLocation.Station_LoadMonitor:
      curType = StationMonitorType.LoadUnload;
      break;
    case routes.RouteLocation.Station_InspectionMonitor:
      curType = StationMonitorType.Inspection;
      break;
    case routes.RouteLocation.Station_Queues:
      curType = StationMonitorType.Queues;
      break;
    case routes.RouteLocation.Station_WashMonitor:
      curType = StationMonitorType.Wash;
      break;
    default:
      curType = StationMonitorType.AllMaterial;
      break;
  }

  return (
    <nav style={props.full ? toolbarStyle : inHeaderStyle}>
      {props.allowChangeType ? (
        <Select name="choose-station-type-select" value={curType} onChange={e => setStation(e.target.value)} autoWidth>
          <MenuItem value={StationMonitorType.LoadUnload}>Load Station</MenuItem>
          <MenuItem value={StationMonitorType.Inspection}>Inspection</MenuItem>
          <MenuItem value={StationMonitorType.Wash}>Wash</MenuItem>
          <MenuItem value={StationMonitorType.Queues}>Queues</MenuItem>
          <MenuItem value={StationMonitorType.AllMaterial}>All Material</MenuItem>
        </Select>
      ) : (
        undefined
      )}
      {curType === StationMonitorType.LoadUnload ? (
        <Input
          type="number"
          placeholder="Load Station Number"
          key="loadnumselect"
          value={props.current_route.selected_load_id}
          onChange={e => setLoadNumber(e.target.value)}
          style={{ width: "3em", marginLeft: "1em" }}
        />
      ) : (
        undefined
      )}
      {curType === StationMonitorType.Inspection ? (
        <Select
          key="inspselect"
          value={props.current_route.selected_insp_type || allInspSym}
          onChange={e => setInspType(e.target.value)}
          style={{ marginLeft: "1em" }}
        >
          <MenuItem key={allInspSym} value={allInspSym}>
            <em>All</em>
          </MenuItem>
          {props.insp_types.toArray({ sortOn: x => x }).map(ty => (
            <MenuItem key={ty} value={ty}>
              {ty}
            </MenuItem>
          ))}
        </Select>
      ) : (
        undefined
      )}
      {curType === StationMonitorType.LoadUnload ? (
        <FormControl style={{ marginLeft: "1em" }}>
          {loadqueues.length === 0 ? (
            <label
              style={{
                position: "absolute",
                top: "10px",
                left: 0,
                color: "rgba(0,0,0,0.54)",
                fontSize: "0.9rem"
              }}
            >
              Display queue(s)
            </label>
          ) : (
            undefined
          )}
          <Select
            multiple
            name="station-monitor-queue-select"
            data-testid="station-monitor-queue-select"
            key="queueselect"
            displayEmpty
            value={loadqueues}
            inputProps={{ id: "queueselect" }}
            style={{ minWidth: "10em", marginTop: "0" }}
            onChange={e => setLoadQueues(e.target.value)}
          >
            <MenuItem key={freeMaterialSym} value={freeMaterialSym}>
              Free Material
            </MenuItem>
            {queueNames.map((q, idx) => (
              <MenuItem key={idx} value={q}>
                {q}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      ) : (
        undefined
      )}
      {curType === StationMonitorType.Queues ? (
        <FormControl style={{ marginLeft: "1em", minWidth: "10em" }}>
          {standalonequeues.length === 0 ? (
            <label
              style={{
                position: "absolute",
                top: "10px",
                left: 0,
                color: "rgba(0,0,0,0.54)",
                fontSize: "0.9rem"
              }}
            >
              Select queue(s)
            </label>
          ) : (
            undefined
          )}
          <Select
            multiple
            name="station-monitor-queue-select"
            data-testid="station-monitor-queue-select"
            key="queueselect"
            displayEmpty
            value={standalonequeues}
            inputProps={{ id: "queueselect" }}
            style={{ marginTop: "0" }}
            onChange={e => setStandaloneQueues(e.target.value)}
          >
            <MenuItem key={freeMaterialSym} value={freeMaterialSym}>
              Free Material
            </MenuItem>
            {queueNames.map((q, idx) => (
              <MenuItem key={idx} value={q}>
                {q}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      ) : (
        undefined
      )}
    </nav>
  );
}

export default connect(
  (st: Store) => ({
    current_route: st.Route,
    queues: st.Current.current_status.queues,
    insp_types: st.Events.last30.mat_summary.inspTypes
  }),
  {
    displayLoadStation: routes.displayLoadStation,
    displayInspection: routes.displayInspectionType,
    displayWash: routes.displayWash,
    displayQueues: routes.displayQueues,
    displayAllMaterial: () => ({ type: routes.RouteLocation.Operations_AllMaterial })
  }
)(StationToolbar);
