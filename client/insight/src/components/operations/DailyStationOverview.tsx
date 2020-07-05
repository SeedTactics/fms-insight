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
import Card from "@material-ui/core/Card";
import CardHeader from "@material-ui/core/CardHeader";
import CardContent from "@material-ui/core/CardContent";
import BugIcon from "@material-ui/icons/BugReport";
import { createSelector } from "reselect";
import { Vector } from "prelude-ts";
import { addDays, startOfToday } from "date-fns";
import Tooltip from "@material-ui/core/Tooltip";
import WorkIcon from "@material-ui/icons/Work";
import IconButton from "@material-ui/core/IconButton";
import Select from "@material-ui/core/Select";
import ImportExport from "@material-ui/icons/ImportExport";
import MenuItem from "@material-ui/core/MenuItem";
import HourglassIcon from "@material-ui/icons/HourglassFull";

import StationDataTable from "../analysis/StationDataTable";
import { connect, Store, mkAC, useSelector } from "../../store/store";
import { PartIdenticon } from "../station-monitor/Material";
import { PartCycleData, EstimatedCycleTimes, PartAndStationOperation, PartAndProcess } from "../../data/events.cycles";
import {
  filterStationCycles,
  outlierMachineCycles,
  outlierLoadCycles,
  FilteredStationCycles,
  FilterAnyMachineKey,
  FilterAnyLoadKey,
  copyCyclesToClipboard,
  plannedOperationSeries,
} from "../../data/results.cycles";
import * as events from "../../data/events";
import * as matDetails from "../../data/material-details";
import { CycleChart, CycleChartPoint, ExtraTooltip } from "../analysis/CycleChart";
import * as guiState from "../../data/gui-state";
import { OEEProps, OEEChart, OEETable } from "./OEEChart";
import { copyOeeToClipboard, buildOeeSeries, OEEBarSeries } from "../../data/results.oee";
import { LazySeq } from "../../data/lazyseq";

// -----------------------------------------------------------------------------------
// Outliers
// -----------------------------------------------------------------------------------

interface OutlierCycleProps {
  readonly showLabor: boolean;
  readonly points: FilteredStationCycles;
  readonly default_date_range: Date[];
  readonly openMaterial: (matId: number) => void;
}

function OutlierCycles(props: OutlierCycleProps) {
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
                onClick={() => copyCyclesToClipboard(props.points, undefined)}
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
        subheader={
          (props.showLabor ? "Load/Unload" : "Machine") +
          " cycles from the past 3 days statistically outside expected range"
        }
      />
      <CardContent>
        <StationDataTable
          points={props.points.data}
          default_date_range={props.default_date_range}
          current_date_zoom={{ start: props.default_date_range[0], end: props.default_date_range[1] }}
          set_date_zoom_range={undefined}
          last30_days={true}
          openDetails={props.openMaterial}
          showWorkorderAndInspect={false}
        />
      </CardContent>
    </Card>
  );
}

const outlierLaborPointsSelector = createSelector(
  (st: Store, _: boolean, _t: Date) => st.Events.last30.cycles.part_cycles,
  (st: Store, _: boolean, _t: Date) => st.Events.last30.cycles.estimatedCycleTimes,
  (_: Store, _l: boolean, today: Date) => today,
  (cycles: Vector<PartCycleData>, estimated: EstimatedCycleTimes, today: Date) => {
    return outlierLoadCycles(cycles, addDays(today, -2), addDays(today, 1), estimated);
  }
);

const outlierMachinePointsSelector = createSelector(
  (st: Store, _: boolean, _t: Date) => st.Events.last30.cycles.part_cycles,
  (st: Store, _: boolean, _t: Date) => st.Events.last30.cycles.estimatedCycleTimes,
  (_: Store, _l: boolean, today: Date) => today,
  (cycles: Vector<PartCycleData>, estimated: EstimatedCycleTimes, today: Date) => {
    return outlierMachineCycles(cycles, addDays(today, -2), addDays(today, 1), estimated);
  }
);

const ConnectedOutlierLabor = connect(
  (st) => ({
    showLabor: true,
    points: outlierLaborPointsSelector(st, true, startOfToday()),
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)],
  }),
  {
    openMaterial: matDetails.openMaterialById,
  }
)(OutlierCycles);

const ConnectedOutlierMachines = connect(
  (st) => ({
    showLabor: false,
    points: outlierMachinePointsSelector(st, false, startOfToday()),
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)],
  }),
  {
    openMaterial: matDetails.openMaterialById,
  }
)(OutlierCycles);

// -----------------------------------------------------------------------------------
// OEE/Hours
// -----------------------------------------------------------------------------------

function StationOEEChart(p: OEEProps) {
  const [showChart, setShowChart] = React.useState(true);
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
                onClick={() => copyOeeToClipboard(p.points)}
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
      <CardContent>{showChart ? <OEEChart {...p} /> : <OEETable {...p} />}</CardContent>
    </Card>
  );
}

const oeePointsSelector = createSelector(
  (last30: events.Last30Days, _: boolean, _t: Date) => last30.cycles.part_cycles,
  (last30: events.Last30Days, _: boolean, _t: Date) => last30.sim_use.station_use,
  (_: events.Last30Days, showLabor: boolean, _t: Date) => showLabor,
  (_: events.Last30Days, _l: boolean, today: Date) => today,
  (cycles, statUse, showLabor, today): ReadonlyArray<OEEBarSeries> => {
    const start = addDays(today, -6);
    const end = addDays(today, 1);
    return buildOeeSeries(start, end, showLabor, cycles, statUse);
  }
);

const ConnectedLoadOEE = connect((st: Store) => {
  return {
    showLabor: true,
    start: addDays(startOfToday(), -6),
    end: addDays(startOfToday(), 1),
    points: oeePointsSelector(st.Events.last30, true, startOfToday()),
  };
})(StationOEEChart);

const ConnectedMachineOEE = connect((st: Store) => {
  return {
    showLabor: false,
    start: addDays(startOfToday(), -6),
    end: addDays(startOfToday(), 1),
    points: oeePointsSelector(st.Events.last30, false, startOfToday()),
  };
})(StationOEEChart);

// --------------------------------------------------------------------------------
// Station Cycles
// --------------------------------------------------------------------------------

interface PartStationCycleChartProps {
  readonly showLabor: boolean;
  readonly default_date_range: Date[];
  readonly openMaterial: (matId: number) => void;
}

function PartStationCycleChart(props: PartStationCycleChartProps) {
  function extraStationCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
    const partC = point as PartCycleData;
    const ret = [];
    for (const mat of partC.material) {
      ret.push({
        title: mat.serial ? mat.serial : "Material",
        value: "Open Card",
        link: () => props.openMaterial(mat.id),
      });
    }
    return ret;
  }

  const [showGraph, setShowGraph] = React.useState(true);
  const [chartZoom, setChartZoom] = React.useState<{ zoom?: { start: Date; end: Date } }>({});
  const [selectedPart, setSelectedPart] = React.useState<PartAndProcess>();
  const [selectedOperation, setSelectedOperation] = React.useState<number>();
  const [selectedPallet, setSelectedPallet] = React.useState<string>();

  const allParts = useSelector((st) => st.Events.last30.cycles.part_and_proc_names);
  const palletNames = useSelector((st) => st.Events.last30.cycles.pallet_names);
  const machineGroups = useSelector((st) => st.Events.last30.cycles.machine_groups);
  const estimatedCycleTimes = useSelector((st) => st.Events.last30.cycles.estimatedCycleTimes);
  const operationNames = React.useMemo(
    () =>
      !props.showLabor && selectedPart
        ? LazySeq.ofIterable(estimatedCycleTimes)
            .filter(
              ([k]) =>
                selectedPart.part === k.part && selectedPart.proc === k.proc && machineGroups.contains(k.statGroup)
            )
            .map(([k]) => k)
            .toVector()
            .sortOn(
              (k) => k.statGroup,
              (k) => k.operation
            )
        : Vector.empty<PartAndStationOperation>(),
    [props.showLabor, selectedPart, estimatedCycleTimes, machineGroups]
  );
  const curOperation = selectedPart ? operationNames.get(selectedOperation ?? 0).getOrNull() : null;

  const cycles = useSelector((st) => st.Events.last30.cycles.part_cycles);
  const points = React.useMemo(() => {
    const today = startOfToday();
    if (curOperation) {
      return filterStationCycles(cycles, {
        zoom: { start: addDays(today, -2), end: addDays(today, 1) },
        pallet: selectedPallet,
        operation: curOperation,
      });
    } else {
      return filterStationCycles(cycles, {
        zoom: { start: addDays(today, -2), end: addDays(today, 1) },
        partAndProc: selectedPart,
        pallet: selectedPallet,
        station: props.showLabor ? FilterAnyLoadKey : FilterAnyMachineKey,
      });
    }
  }, [cycles, props.showLabor, selectedPart, selectedPallet]);
  const plannedSeries = React.useMemo(() => {
    if (curOperation !== null) {
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
                  onClick={() => copyCyclesToClipboard(points, undefined, props.showLabor)}
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
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
              name="Station-Cycles-cycle-chart-select"
              autoWidth
              displayEmpty
              value={selectedPart || ""}
              style={{ marginLeft: "1em" }}
              onChange={(e) => {
                setSelectedPart(e.target.value === "" ? undefined : (e.target.value as PartAndProcess));
                setSelectedOperation(undefined);
              }}
            >
              <MenuItem key={0} value="">
                <em>Any Part</em>
              </MenuItem>
              {allParts.toArray({ sortOn: [(x) => x.part, (x) => x.proc] }).map((n, idx) => (
                <MenuItem key={idx} value={n as any}>
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
                value={selectedOperation ?? 0}
                style={{ marginLeft: "1em" }}
                onChange={(e) => setSelectedOperation(e.target.value as number)}
              >
                {operationNames.length() === 0 ? (
                  <MenuItem value={0}>
                    <em>Any Operation</em>
                  </MenuItem>
                ) : (
                  LazySeq.ofIterable(operationNames).map((oper, idx) => (
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
              {palletNames.toArray({ sortOn: (x) => x }).map((n) => (
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
            default_date_range={props.default_date_range}
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
            default_date_range={props.default_date_range}
            current_date_zoom={undefined}
            set_date_zoom_range={undefined}
            last30_days={true}
            openDetails={props.openMaterial}
            showWorkorderAndInspect={true}
            hideMedian={props.showLabor}
          />
        )}
      </CardContent>
    </Card>
  );
}

const ConnectedLaborCycleChart = connect(
  (st) => ({
    showLabor: true,
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)],
  }),
  {
    setSelected: mkAC(guiState.ActionType.SetSelectedStationCycle),
    setZoomRange: mkAC(guiState.ActionType.SetStationCycleDateZoom),
    openMaterial: matDetails.openMaterialById,
  }
)(PartStationCycleChart);

const ConnectedMachineCycleChart = connect(
  (st) => ({
    showLabor: false,
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)],
  }),
  {
    setSelected: mkAC(guiState.ActionType.SetSelectedStationCycle),
    setZoomRange: mkAC(guiState.ActionType.SetStationCycleDateZoom),
    openMaterial: matDetails.openMaterialById,
  }
)(PartStationCycleChart);

// -----------------------------------------------------------------------------------
// Main
// -----------------------------------------------------------------------------------

export function OperationLoadUnload() {
  React.useEffect(() => {
    document.title = "Load/Unload Management - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <div data-testid="outlier-cycles">
        <ConnectedOutlierLabor />
      </div>
      <div data-testid="oee-cycles" style={{ marginTop: "3em" }}>
        <ConnectedLoadOEE />
      </div>
      <div data-testid="all-cycles" style={{ marginTop: "3em" }}>
        <ConnectedLaborCycleChart />
      </div>
    </main>
  );
}

export function OperationMachines() {
  React.useEffect(() => {
    document.title = "Machine Management - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <div data-testid="outlier-cycles">
        <ConnectedOutlierMachines />
      </div>
      <div data-testid="oee-cycles" style={{ marginTop: "3em" }}>
        <ConnectedMachineOEE />
      </div>
      <div data-testid="all-cycles" style={{ marginTop: "3em" }}>
        <ConnectedMachineCycleChart />
      </div>
    </main>
  );
}
