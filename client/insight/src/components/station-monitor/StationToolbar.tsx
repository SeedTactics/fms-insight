/* Copyright (c) 2023, John Lenz

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
import { Box, Checkbox, Divider, FormControlLabel, Select, useMediaQuery, useTheme } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Input } from "@mui/material";
import { FormControl } from "@mui/material";

import { currentStatus } from "../../cell-status/current-status.js";
import { RouteLocation, currentRoute } from "../routes.js";
import { last30InspectionTypes } from "../../cell-status/names.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { SystemOverviewDialogButton } from "./SystemOverview.js";
import { useAtom, useAtomValue } from "jotai";
import { ReactNode } from "react";
import { hideNonLoadingMaterialOnLoadStation } from "../../data/queue-material.js";

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
  alignSelf: "center",
  alignItems: "flex-end",
};

const allInspSym = "@@all_inspection_display@@";
const completedSym = "@@recent_completed_material@@";

enum StationMonitorType {
  LoadUnload = "LoadUnload",
  Inspection = "Inspection",
  CloseOut = "CloseOut",
  Queues = "Queues",
  AllMaterial = "AllMaterial",
}

function HideNonLoadingCheckbox() {
  const [hide, setHide] = useAtom(hideNonLoadingMaterialOnLoadStation);
  return (
    <FormControlLabel
      control={<Checkbox size="small" checked={hide} onChange={(e) => setHide(e.target.checked)} />}
      label="Hide Non-Loading Material"
      sx={{ pl: 2, pr: 2 }}
    />
  );
}

export function StationToolbar(): ReactNode {
  const [route, setRoute] = useAtom(currentRoute);
  const inspTypes = useAtomValue(last30InspectionTypes);
  const queueNames = Object.keys(useAtomValue(currentStatus).queues).sort((a, b) => a.localeCompare(b));
  const theme = useTheme();
  const full = useMediaQuery(theme.breakpoints.down("md"));

  function setLoadNumber(valStr: string) {
    const val = parseFloat(valStr);
    if (!isNaN(val) && isFinite(val)) {
      if (route.route === RouteLocation.Station_LoadMonitor) {
        setRoute({
          route: RouteLocation.Station_LoadMonitor,
          loadNum: val,
          queues: route.queues,
          completed: route.completed,
        });
      } else {
        setRoute({ route: RouteLocation.Station_LoadMonitor, loadNum: val, queues: [], completed: false });
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

  function setLoadQueues(newQueues: ReadonlyArray<string>) {
    const completed = newQueues.includes(completedSym);
    newQueues = newQueues.filter((q) => q !== completedSym).slice(0, 2);
    if (route.route === RouteLocation.Station_LoadMonitor) {
      setRoute({
        route: RouteLocation.Station_LoadMonitor,
        loadNum: route.loadNum,
        queues: newQueues,
        completed: completed,
      });
    } else {
      setRoute({ route: RouteLocation.Station_LoadMonitor, loadNum: 1, queues: newQueues, completed });
    }
  }

  function setStandaloneQueues(newQueues: ReadonlyArray<string>) {
    setRoute({ route: RouteLocation.Station_Queues, queues: newQueues });
  }

  let curType = StationMonitorType.LoadUnload;
  let loadNum = 1;
  let curInspType: string | null = null;
  let currentQueues: ReadonlyArray<string> = [];
  switch (route.route) {
    case RouteLocation.Station_LoadMonitor:
      curType = StationMonitorType.LoadUnload;
      loadNum = route.loadNum;
      currentQueues = route.queues;
      if (route.completed) {
        currentQueues = currentQueues.concat(completedSym);
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
      currentQueues = route.queues;
      break;
    case RouteLocation.Station_Closeout:
      curType = StationMonitorType.CloseOut;
      break;
    default:
      curType = StationMonitorType.AllMaterial;
      break;
  }

  return (
    <Box component="nav" sx={full ? toolbarStyle : inHeaderStyle}>
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
          {LazySeq.of(inspTypes)
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
            displayEmpty
            variant="standard"
            value={currentQueues}
            inputProps={{ id: "queueselect" }}
            style={{ minWidth: "10em", marginTop: "0" }}
            onChange={(e) => setLoadQueues(e.target.value as ReadonlyArray<string>)}
          >
            {queueNames.map((q, idx) => (
              <MenuItem key={idx} value={q}>
                {q}
              </MenuItem>
            ))}
            <MenuItem value={completedSym}>
              <i>Completed</i>
            </MenuItem>
            <Divider />
            <HideNonLoadingCheckbox />
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
            displayEmpty
            variant="standard"
            value={currentQueues}
            inputProps={{ id: "queueselect" }}
            style={{ marginTop: "0" }}
            onChange={(e) => setStandaloneQueues(e.target.value as ReadonlyArray<string>)}
          >
            {queueNames.map((q, idx) => (
              <MenuItem key={idx} value={q}>
                {q}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      ) : undefined}
    </Box>
  );
}

export function StationToolbarOverviewButton() {
  const theme = useTheme();
  const full = useMediaQuery(theme.breakpoints.down("md"));
  return (
    <Box display="flex" alignItems="center" height="100%" bgcolor={full ? "#E0E0E0" : undefined}>
      <SystemOverviewDialogButton full={full} />
    </Box>
  );
}
