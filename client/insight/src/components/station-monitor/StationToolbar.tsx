/* Copyright (c) 2021, John Lenz

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
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Input } from "@mui/material";
import { FormControl } from "@mui/material";

import { useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status.js";
import { RouteLocation, useCurrentRoute } from "../routes.js";
import { last30InspectionTypes } from "../../cell-status/names.js";
import { LazySeq } from "@seedtactics/immutable-collections";

const toolbarStyle = {
  display: "flex",
  backgroundColor: "#E0E0E0",
  paddingLeft: "24px",
  paddingRight: "24px",
  paddingBottom: "4px",
  height: "2.5em",
  alignItems: "flex-end",
};

const inHeaderStyle = {
  display: "flex",
  flexGrow: 1,
  alignSelf: "center",
  alignItems: "flex-end",
};

interface StationToolbarProps {
  readonly full: boolean;
}

const freeMaterialSym = "@@insight_free_material@@";
const allInspSym = "@@all_inspection_display@@";

enum StationMonitorType {
  LoadUnload = "LoadUnload",
  Inspection = "Inspection",
  Wash = "Wash",
  Queues = "Queues",
  AllMaterial = "AllMaterial",
}

function StationToolbar(props: StationToolbarProps): JSX.Element {
  const [route, setRoute] = useCurrentRoute();
  const inspTypes = useRecoilValue(last30InspectionTypes);
  const queueNames = Object.keys(useRecoilValue(currentStatus).queues).sort();

  function setLoadNumber(valStr: string) {
    const val = parseFloat(valStr);
    if (!isNaN(val) && isFinite(val)) {
      if (route.route === RouteLocation.Station_LoadMonitor) {
        setRoute({ route: RouteLocation.Station_LoadMonitor, loadNum: val, free: route.free, queues: route.queues });
      } else {
        setRoute({ route: RouteLocation.Station_LoadMonitor, loadNum: val, free: false, queues: [] });
      }
    }
  }

  function setInspType(type: string) {
    if (type === allInspSym) {
      setRoute({ route: RouteLocation.Station_InspectionMonitor });
    } else {
      setRoute({ route: RouteLocation.Station_InspectionMonitorWithType, inspType: type });
    }
  }

  // the material-ui type bindings specify `e.target.value` to have type string, but
  // when multiple selects are enabled it is actually a type string[]
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  function setLoadQueues(newQueuesAny: any) {
    let newQueues = newQueuesAny as ReadonlyArray<string>;
    const free = newQueues.includes(freeMaterialSym);
    newQueues = newQueues.filter((q) => q !== freeMaterialSym).slice(0, 3);
    if (route.route === RouteLocation.Station_LoadMonitor) {
      setRoute({ route: RouteLocation.Station_LoadMonitor, loadNum: route.loadNum, free: free, queues: newQueues });
    } else {
      setRoute({ route: RouteLocation.Station_LoadMonitor, loadNum: 1, free: free, queues: newQueues });
    }
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  function setStandaloneQueues(newQueuesAny: any) {
    let newQueues = newQueuesAny as ReadonlyArray<string>;
    const free = newQueues.includes(freeMaterialSym);
    newQueues = newQueues.filter((q) => q !== freeMaterialSym);
    setRoute({ route: RouteLocation.Station_Queues, free: free, queues: newQueues });
  }

  let curType = StationMonitorType.LoadUnload;
  let loadNum = 1;
  let curInspType: string | null = null;
  let currentQueues: string[] = [];
  switch (route.route) {
    case RouteLocation.Station_LoadMonitor:
      curType = StationMonitorType.LoadUnload;
      loadNum = route.loadNum;
      currentQueues = [...route.queues];
      if (route.free) {
        currentQueues.push(freeMaterialSym);
      }
      break;
    case RouteLocation.Station_InspectionMonitor:
      curType = StationMonitorType.Inspection;
      curInspType = "";
      break;
    case RouteLocation.Station_InspectionMonitorWithType:
      curType = StationMonitorType.Inspection;
      curInspType = route.inspType;
      break;
    case RouteLocation.Station_Queues:
      curType = StationMonitorType.Queues;
      currentQueues = [...route.queues];
      if (route.free) {
        currentQueues.push(freeMaterialSym);
      }
      break;
    case RouteLocation.Station_WashMonitor:
      curType = StationMonitorType.Wash;
      break;
    default:
      curType = StationMonitorType.AllMaterial;
      break;
  }

  return (
    <nav style={props.full ? toolbarStyle : inHeaderStyle}>
      {curType === StationMonitorType.LoadUnload ? (
        <Input
          type="number"
          placeholder="Load Station Number"
          key="loadnumselect"
          value={loadNum}
          onChange={(e) => setLoadNumber(e.target.value)}
          style={{ width: "3em", marginLeft: "1em" }}
        />
      ) : undefined}
      {curType === StationMonitorType.Inspection ? (
        <Select
          key="inspselect"
          value={curInspType || allInspSym}
          onChange={(e) => setInspType(e.target.value)}
          variant="standard"
          style={{ marginLeft: "1em" }}
        >
          <MenuItem key={allInspSym} value={allInspSym}>
            <em>All</em>
          </MenuItem>
          {LazySeq.ofIterable(inspTypes)
            .sortBy((x) => x)
            .map((ty) => (
              <MenuItem key={ty} value={ty}>
                {ty}
              </MenuItem>
            ))}
        </Select>
      ) : undefined}
      {curType === StationMonitorType.LoadUnload ? (
        <FormControl style={{ marginLeft: "1em" }}>
          {currentQueues.length === 0 ? (
            <label
              style={{
                position: "absolute",
                top: "10px",
                left: 0,
                color: "rgba(0,0,0,0.54)",
                fontSize: "0.9rem",
              }}
            >
              Display queue(s)
            </label>
          ) : undefined}
          <Select
            multiple
            name="station-monitor-queue-select"
            data-testid="station-monitor-queue-select"
            key="queueselect"
            displayEmpty
            variant="standard"
            value={currentQueues}
            inputProps={{ id: "queueselect" }}
            style={{ minWidth: "10em", marginTop: "0" }}
            onChange={(e) => setLoadQueues(e.target.value)}
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
      ) : undefined}
      {curType === StationMonitorType.Queues ? (
        <FormControl style={{ marginLeft: "1em", minWidth: "10em" }}>
          {currentQueues.length === 0 ? (
            <label
              style={{
                position: "absolute",
                top: "10px",
                left: 0,
                color: "rgba(0,0,0,0.54)",
                fontSize: "0.9rem",
              }}
            >
              Select queue(s)
            </label>
          ) : undefined}
          <Select
            multiple
            name="station-monitor-queue-select"
            data-testid="station-monitor-queue-select"
            key="queueselect"
            displayEmpty
            variant="standard"
            value={currentQueues}
            inputProps={{ id: "queueselect" }}
            style={{ marginTop: "0" }}
            onChange={(e) => setStandaloneQueues(e.target.value)}
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
      ) : undefined}
    </nav>
  );
}

export default StationToolbar;
