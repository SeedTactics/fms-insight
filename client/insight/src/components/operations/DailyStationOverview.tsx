/* Copyright (c) 2020, John Lenz

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
import { Card } from "@mui/material";
import { CardHeader } from "@mui/material";
import { CardContent } from "@mui/material";
import BugIcon from "@mui/icons-material/BugReport";
import { addDays, startOfToday } from "date-fns";
import { Tooltip } from "@mui/material";
import WorkIcon from "@mui/icons-material/Work";
import { IconButton } from "@mui/material";
import { Select } from "@mui/material";
import ImportExport from "@mui/icons-material/ImportExport";
import { MenuItem } from "@mui/material";
import HourglassIcon from "@mui/icons-material/HourglassFull";

import StationDataTable from "../analysis/StationDataTable";
import { PartIdenticon } from "../station-monitor/Material";
import {
  filterStationCycles,
  outlierMachineCycles,
  outlierLoadCycles,
  FilterAnyMachineKey,
  copyCyclesToClipboard,
  plannedOperationSeries,
  loadOccupancyCycles,
  LoadCycleData,
  FilterAnyLoadKey,
  PartAndProcess,
} from "../../data/results.cycles";
import * as matDetails from "../../cell-status/material-details";
import { CycleChart, CycleChartPoint, ExtraTooltip } from "../analysis/CycleChart";
import { OEEChart, OEETable } from "./OEEChart";
import { copyOeeToClipboard, buildOeeSeries } from "../../data/results.oee";
import { useRecoilValue, useSetRecoilState } from "recoil";
import { last30SimStationUse } from "../../cell-status/sim-station-use";
import { last30MaterialSummary } from "../../cell-status/material-summary";
import { last30EstimatedCycleTimes, PartAndStationOperation } from "../../cell-status/estimated-cycle-times";
import { last30StationCycles } from "../../cell-status/station-cycles";

// -----------------------------------------------------------------------------------
// Outliers
// -----------------------------------------------------------------------------------

interface OutlierCycleProps {
  readonly showLabor: boolean;
}

const OutlierCycles = React.memo(function OutlierCycles(props: OutlierCycleProps) {
  const matSummary = useRecoilValue(last30MaterialSummary);
  const default_date_range = [addDays(startOfToday(), -4), addDays(startOfToday(), 1)];
  const estimatedCycleTimes = useRecoilValue(last30EstimatedCycleTimes);
  const allCycles = useRecoilValue(last30StationCycles);
  const points = React.useMemo(() => {
    const today = startOfToday();
    if (props.showLabor) {
      return outlierLoadCycles(allCycles, addDays(today, -4), addDays(today, 1), estimatedCycleTimes);
    } else {
      return outlierMachineCycles(allCycles, addDays(today, -4), addDays(today, 1), estimatedCycleTimes);
    }
  }, [props.showLabor, estimatedCycleTimes, allCycles]);

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <BugIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Outlier Cycles</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyCyclesToClipboard(points, matSummary.matsById, undefined)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
        subheader={
          (props.showLabor ? "Load/Unload" : "Machine") +
          " cycles from the past 5 days statistically outside expected range"
        }
      />
      <CardContent>
        <StationDataTable
          points={points.data}
          matsById={matSummary.matsById}
          default_date_range={default_date_range}
          current_date_zoom={{ start: default_date_range[0], end: default_date_range[1] }}
          set_date_zoom_range={undefined}
          last30_days={true}
          showWorkorderAndInspect={false}
          defaultSortDesc
        />
      </CardContent>
    </Card>
  );
});

// -----------------------------------------------------------------------------------
// OEE/Hours
// -----------------------------------------------------------------------------------

const StationOEEChart = React.memo(function StationOEEChart({ showLabor }: { readonly showLabor: boolean }) {
  const [showChart, setShowChart] = React.useState(true);

  const start = addDays(startOfToday(), -6);
  const end = addDays(startOfToday(), 1);

  const cycles = useRecoilValue(last30StationCycles);
  const statUse = useRecoilValue(last30SimStationUse);
  const points = React.useMemo(
    () => buildOeeSeries(start, end, showLabor, cycles, statUse),
    [start, end, showLabor, cycles, statUse]
  );

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <HourglassIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Hours</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyOeeToClipboard(points)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
            <Select
              name="Station-OEE-chart-or-table-select"
              autoWidth
              value={showChart ? "chart" : "table"}
              onChange={(e) => setShowChart(e.target.value === "chart")}
            >
              <MenuItem key="chart" value="chart">
                Chart
              </MenuItem>
              <MenuItem key="table" value="table">
                Table
              </MenuItem>
            </Select>
          </div>
        }
      />
      <CardContent>
        {showChart ? (
          <OEEChart start={start} end={end} showLabor={showLabor} points={points} />
        ) : (
          <OEETable start={start} end={end} showLabor={showLabor} points={points} />
        )}
      </CardContent>
    </Card>
  );
});

// --------------------------------------------------------------------------------
// Station Cycles
// --------------------------------------------------------------------------------

interface PartStationCycleChartProps {
  readonly showLabor: boolean;
}

const PartStationCycleCart = React.memo(function PartStationCycleChart(props: PartStationCycleChartProps) {
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);
  const extraStationCycleTooltip = React.useCallback(
    function extraStationCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
      const partC = point as LoadCycleData;
      const ret = [];
      if (partC.operations) {
        for (const mat of partC.operations) {
          ret.push({
            title: (mat.mat.serial ? mat.mat.serial : "Material") + " " + mat.operation,
            value: "Open Card",
            link: () => setMatToShow({ type: "LogMat", logMat: mat.mat }),
          });
        }
      } else {
        for (const mat of partC.material) {
          ret.push({
            title: mat.serial ? mat.serial : "Material",
            value: "Open Card",
            link: () => setMatToShow({ type: "LogMat", logMat: mat }),
          });
        }
      }
      return ret;
    },
    [setMatToShow]
  );

  const [showGraph, setShowGraph] = React.useState(true);
  const [chartZoom, setChartZoom] = React.useState<{ zoom?: { start: Date; end: Date } }>({});
  const [selectedPart, setSelectedPart] = React.useState<PartAndProcess>();
  const [selectedOperation, setSelectedOperation] = React.useState<PartAndStationOperation>();
  const [selectedPallet, setSelectedPallet] = React.useState<string>();

  const estimatedCycleTimes = useRecoilValue(last30EstimatedCycleTimes);
  const default_date_range = [addDays(startOfToday(), -4), addDays(startOfToday(), 1)];

  const cycles = useRecoilValue(last30StationCycles);
  const matSummary = useRecoilValue(last30MaterialSummary);
  const points = React.useMemo(() => {
    const today = startOfToday();
    if (selectedOperation) {
      return filterStationCycles(cycles, {
        zoom: { start: addDays(today, -4), end: addDays(today, 1) },
        pallet: selectedPallet,
        operation: selectedOperation,
      });
    } else if (props.showLabor && showGraph) {
      return loadOccupancyCycles(cycles, {
        zoom: { start: addDays(today, -4), end: addDays(today, 1) },
        partAndProc: selectedPart,
        pallet: selectedPallet,
      });
    } else {
      return filterStationCycles(cycles, {
        zoom: { start: addDays(today, -4), end: addDays(today, 1) },
        partAndProc: selectedPart,
        pallet: selectedPallet,
        station: props.showLabor ? FilterAnyLoadKey : FilterAnyMachineKey,
      });
    }
  }, [cycles, props.showLabor, selectedPart, selectedPallet, selectedOperation, showGraph]);
  const curOperation = selectedPart ? selectedOperation ?? points.allMachineOperations[0] : undefined;
  const plannedSeries = React.useMemo(() => {
    if (curOperation) {
      return plannedOperationSeries(points, false);
    } else {
      return undefined;
    }
  }, [points, curOperation]);

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <WorkIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>
              Recent {props.showLabor ? "Load/Unload Occupancy" : "Machine Cycles"}
            </div>
            <div style={{ flexGrow: 1 }} />
            {points.data.length() > 0 ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  onClick={() => copyCyclesToClipboard(points, matSummary.matsById, undefined, props.showLabor)}
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                  size="large"
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : undefined}
            <Select
              name="Station-Cycles-chart-or-table-select"
              autoWidth
              value={showGraph ? "graph" : "table"}
              onChange={(e) => setShowGraph(e.target.value === "graph")}
            >
              <MenuItem key="graph" value="graph">
                Graph
              </MenuItem>
              <MenuItem key="table" value="table">
                Table
              </MenuItem>
            </Select>
            <Select
              autoWidth
              displayEmpty
              value={
                selectedPart
                  ? points.allPartAndProcNames.findIndex(
                      (o) => selectedPart.part === o.part && selectedPart.proc === o.proc
                    )
                  : -1
              }
              style={{ marginLeft: "1em" }}
              onChange={(e) => {
                setSelectedPart(
                  e.target.value === -1 ? undefined : points.allPartAndProcNames[e.target.value as number]
                );
                setSelectedOperation(undefined);
              }}
            >
              <MenuItem key={0} value={-1}>
                <em>Any Part</em>
              </MenuItem>
              {points.allPartAndProcNames.map((n, idx) => (
                <MenuItem key={idx} value={idx}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <PartIdenticon part={n.part} size={20} />
                    <span style={{ marginRight: "1em" }}>
                      {n.part}-{n.proc}
                    </span>
                  </div>
                </MenuItem>
              ))}
            </Select>
            {!props.showLabor ? (
              <Select
                name="Station-Cycles-cycle-chart-station-select"
                autoWidth
                displayEmpty
                value={curOperation ? points.allMachineOperations.findIndex((o) => curOperation.equals(o)) : -1}
                style={{ marginLeft: "1em" }}
                onChange={(e) => setSelectedOperation(points.allMachineOperations[e.target.value as number])}
              >
                {points.allMachineOperations.length === 0 ? (
                  <MenuItem value={-1}>
                    <em>Any Operation</em>
                  </MenuItem>
                ) : (
                  points.allMachineOperations.map((oper, idx) => (
                    <MenuItem key={idx} value={idx}>
                      {oper.statGroup} {oper.operation}
                    </MenuItem>
                  ))
                )}
              </Select>
            ) : undefined}
            <Select
              name="Station-Cycles-cycle-chart-station-pallet"
              autoWidth
              displayEmpty
              value={selectedPallet || ""}
              style={{ marginLeft: "1em" }}
              onChange={(e) => setSelectedPallet(e.target.value === "" ? undefined : (e.target.value as string))}
            >
              <MenuItem key={0} value="">
                <em>Any Pallet</em>
              </MenuItem>
              {points.allPalletNames.map((n) => (
                <MenuItem key={n} value={n}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <span style={{ marginRight: "1em" }}>{n}</span>
                  </div>
                </MenuItem>
              ))}
            </Select>
          </div>
        }
      />
      <CardContent>
        {showGraph ? (
          <CycleChart
            points={points.data}
            series_label={points.seriesLabel}
            default_date_range={default_date_range}
            extra_tooltip={extraStationCycleTooltip}
            current_date_zoom={chartZoom.zoom}
            set_date_zoom_range={setChartZoom}
            stats={curOperation ? estimatedCycleTimes.get(curOperation).getOrUndefined() : undefined}
            partCntPerPoint={
              curOperation
                ? points.data
                    .findAny(() => true)
                    .map(([, cs]) => cs[0]?.material.length)
                    .getOrUndefined()
                : undefined
            }
            plannedSeries={plannedSeries}
          />
        ) : (
          <StationDataTable
            points={points.data}
            matsById={matSummary.matsById}
            default_date_range={default_date_range}
            current_date_zoom={undefined}
            set_date_zoom_range={undefined}
            last30_days={true}
            showWorkorderAndInspect={true}
            hideMedian={props.showLabor}
          />
        )}
      </CardContent>
    </Card>
  );
});

// -----------------------------------------------------------------------------------
// Main
// -----------------------------------------------------------------------------------

export function LoadUnloadRecentOverview(): JSX.Element {
  return (
    <>
      <div data-testid="outlier-cycles">
        <OutlierCycles showLabor={true} />
      </div>
      <div data-testid="oee-cycles" style={{ marginTop: "3em" }}>
        <StationOEEChart showLabor={true} />
      </div>
      <div data-testid="all-cycles" style={{ marginTop: "3em" }}>
        <PartStationCycleCart showLabor={true} />
      </div>
    </>
  );
}

export function OperationLoadUnload(): JSX.Element {
  React.useEffect(() => {
    document.title = "Load/Unload Management - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <LoadUnloadRecentOverview />
    </main>
  );
}

export function MachinesRecentOverview(): JSX.Element {
  return (
    <>
      <div data-testid="outlier-cycles">
        <OutlierCycles showLabor={false} />
      </div>
      <div data-testid="oee-cycles" style={{ marginTop: "3em" }}>
        <StationOEEChart showLabor={false} />
      </div>
      <div data-testid="all-cycles" style={{ marginTop: "3em" }}>
        <PartStationCycleCart showLabor={false} />
      </div>
    </>
  );
}

export function OperationMachines(): JSX.Element {
  React.useEffect(() => {
    document.title = "Machine Management - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <MachinesRecentOverview />
    </main>
  );
}
