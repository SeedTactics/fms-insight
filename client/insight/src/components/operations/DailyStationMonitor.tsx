/* Copyright (c) 2019, John Lenz

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
import { Vector, HashSet } from "prelude-ts";
import { addDays, startOfToday } from "date-fns";
import Tooltip from "@material-ui/core/Tooltip";
import WorkIcon from "@material-ui/icons/Work";
import IconButton from "@material-ui/core/IconButton";
import Select from "@material-ui/core/Select";
import ImportExport from "@material-ui/icons/ImportExport";
import Grid from "@material-ui/core/Grid";
import MenuItem from "@material-ui/core/MenuItem";
import HourglassIcon from "@material-ui/icons/HourglassFull";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

import StationDataTable from "../analysis/StationDataTable";
import { connect, Store, DispatchAction, mkAC } from "../../store/store";
import { PartIdenticon } from "../station-monitor/Material";
import {
  PartCycleData,
  filterStationCycles,
  outlierCycles,
  FilteredStationCycles,
  FilterAnyMachineKey,
  FilterAnyLoadKey,
  DayAndStation
} from "../../data/events.cycles";
import * as events from "../../data/events";
import * as matDetails from "../../data/material-details";
import { CycleChart, CycleChartPoint, ExtraTooltip } from "../analysis/CycleChart";
import { copyCyclesToClipboard } from "../../data/clipboard-table";
import * as guiState from "../../data/gui-state";
import { LazySeq } from "../../data/lazyseq";
import { FlexibleWidthXYPlot, XAxis, YAxis, VerticalBarSeries, DiscreteColorLegend, Hint } from "react-vis";

// -----------------------------------------------------------------------------------
// Outliers
// -----------------------------------------------------------------------------------

export interface OutlierCycleProps {
  readonly showLabor: boolean;
  readonly points: FilteredStationCycles;
  readonly default_date_range: Date[];
  readonly openMaterial: (matId: number) => void;
}

export function OutlierCycles(props: OutlierCycleProps) {
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <BugIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Outlier Cycles</div>
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
          showMedian={true}
          showWorkorderAndInspect={false}
        />
      </CardContent>
    </Card>
  );
}

const outlierPointsSelector = createSelector(
  (st: Store, _: boolean) => st.Events.last30.cycles.part_cycles,
  (_: Store, showLabor: boolean) => showLabor,
  (cycles: Vector<PartCycleData>, showLabor: boolean) => {
    return outlierCycles(cycles, showLabor, addDays(startOfToday(), -2), addDays(startOfToday(), 1));
  }
);

const ConnectedOutlierLabor = connect(
  st => ({
    showLabor: true,
    points: outlierPointsSelector(st, true),
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)]
  }),
  {
    openMaterial: matDetails.openMaterialById
  }
)(OutlierCycles);

const ConnectedOutlierMachines = connect(
  st => ({
    showLabor: false,
    points: outlierPointsSelector(st, false),
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)]
  }),
  {
    openMaterial: matDetails.openMaterialById
  }
)(OutlierCycles);

// --------------------------------------------------------------------------------
// Station Cycles
// --------------------------------------------------------------------------------

interface PartStationCycleChartProps {
  readonly showLabor: boolean;
  readonly allParts: HashSet<string>;
  readonly palletNames: HashSet<string>;
  readonly points: FilteredStationCycles;
  readonly default_date_range: Date[];
  readonly selectedPart?: string;
  readonly selectedPallet?: string;
  readonly setSelected: DispatchAction<guiState.ActionType.SetSelectedStationCycle>;
  readonly openMaterial: (matId: number) => void;
}

function stripAfterDash(s: string): string {
  const idx = s.indexOf("-");
  if (idx >= 0) {
    return s.substring(0, idx);
  } else {
    return s;
  }
}

function PartStationCycleChart(props: PartStationCycleChartProps) {
  function extraStationCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
    const partC = point as PartCycleData;
    const ret = [];
    if (partC.serial) {
      ret.push({
        title: "Serial",
        value: partC.serial
      });
    }
    if (partC.matId >= 0) {
      ret.push({
        title: "Material",
        value: "Open Card",
        link: () => props.openMaterial(partC.matId)
      });
    }
    return ret;
  }

  const [showGraph, setShowGraph] = React.useState(true);
  const [chartZoom, setChartZoom] = React.useState<{ zoom?: { start: Date; end: Date } }>({});

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <WorkIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>
              All {props.showLabor ? "Load/Unload" : "Machines"} Cycles
            </div>
            <div style={{ flexGrow: 1 }} />
            {props.points.data.length() > 0 ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  onClick={() => copyCyclesToClipboard(props.points, undefined)}
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : (
              undefined
            )}
            <Select
              name="Station-Cycles-chart-or-table-select"
              autoWidth
              value={showGraph ? "graph" : "table"}
              onChange={e => setShowGraph(e.target.value === "graph")}
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
              value={props.selectedPart || ""}
              style={{ marginLeft: "1em" }}
              onChange={e =>
                props.setSelected({
                  part: e.target.value === "" ? undefined : e.target.value,
                  pallet: props.selectedPallet
                })
              }
            >
              <MenuItem key={0} value="">
                <em>Any Part</em>
              </MenuItem>
              {props.allParts.toArray({ sortOn: x => x }).map(n => (
                <MenuItem key={n} value={n}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <PartIdenticon part={stripAfterDash(n)} size={30} />
                    <span style={{ marginRight: "1em" }}>{n}</span>
                  </div>
                </MenuItem>
              ))}
            </Select>
            <Select
              name="Station-Cycles-cycle-chart-station-pallet"
              autoWidth
              displayEmpty
              value={props.selectedPallet || ""}
              style={{ marginLeft: "1em" }}
              onChange={e =>
                props.setSelected({
                  pallet: e.target.value === "" ? undefined : e.target.value,
                  part: props.selectedPart
                })
              }
            >
              <MenuItem key={0} value="">
                <em>Any Pallet</em>
              </MenuItem>
              {props.palletNames.toArray({ sortOn: x => x }).map(n => (
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
            points={props.points.data}
            series_label={props.points.seriesLabel}
            default_date_range={props.default_date_range}
            extra_tooltip={extraStationCycleTooltip}
            current_date_zoom={chartZoom.zoom}
            set_date_zoom_range={setChartZoom}
          />
        ) : (
          <StationDataTable
            points={props.points.data}
            default_date_range={props.default_date_range}
            current_date_zoom={undefined}
            set_date_zoom_range={undefined}
            last30_days={true}
            openDetails={props.openMaterial}
            showWorkorderAndInspect={true}
            showMedian={false}
          />
        )}
      </CardContent>
    </Card>
  );
}

const stationCyclePointsSelector = createSelector(
  [
    (st: Store, _: boolean) => st.Events.last30.cycles.part_cycles,
    (st: Store, _: boolean) => st.Gui.station_cycle_selected_part,
    (st: Store, _: boolean) => st.Gui.station_cycle_selected_pallet,
    (_: Store, showLabor: boolean) => showLabor
  ],
  (cycles: Vector<PartCycleData>, part: string | undefined, pallet: string | undefined, showLabor: boolean) => {
    return filterStationCycles(
      cycles,
      { start: addDays(startOfToday(), -2), end: addDays(startOfToday(), 1) },
      part,
      pallet,
      showLabor ? FilterAnyLoadKey : FilterAnyMachineKey
    );
  }
);

const ConnectedLaborCycleChart = connect(
  st => ({
    showLabor: true,
    allParts: st.Events.last30.cycles.part_and_proc_names,
    points: stationCyclePointsSelector(st, true),
    selectedPart: st.Gui.station_cycle_selected_part,
    selectedPallet: st.Gui.station_cycle_selected_pallet,
    zoomDateRange: st.Gui.station_cycle_date_zoom,
    palletNames: st.Events.last30.cycles.pallet_names,
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)]
  }),
  {
    setSelected: mkAC(guiState.ActionType.SetSelectedStationCycle),
    setZoomRange: mkAC(guiState.ActionType.SetStationCycleDateZoom),
    openMaterial: matDetails.openMaterialById
  }
)(PartStationCycleChart);

const ConnectedMachineCycleChart = connect(
  st => ({
    showLabor: false,
    allParts: st.Events.last30.cycles.part_and_proc_names,
    points: stationCyclePointsSelector(st, false),
    selectedPart: st.Gui.station_cycle_selected_part,
    selectedPallet: st.Gui.station_cycle_selected_pallet,
    zoomDateRange: st.Gui.station_cycle_date_zoom,
    palletNames: st.Events.last30.cycles.pallet_names,
    default_date_range: [addDays(startOfToday(), -2), addDays(startOfToday(), 1)]
  }),
  {
    setSelected: mkAC(guiState.ActionType.SetSelectedStationCycle),
    setZoomRange: mkAC(guiState.ActionType.SetStationCycleDateZoom),
    openMaterial: matDetails.openMaterialById
  }
)(PartStationCycleChart);

// -----------------------------------------------------------------------------------
// OEE
// -----------------------------------------------------------------------------------

interface OEEBarPoint {
  readonly x: string;
  readonly y: number;
  readonly planned: number;
}

interface OEEBarSeries {
  readonly station: string;
  readonly points: ReadonlyArray<OEEBarPoint>;
}

interface OEEProps {
  readonly showLabor: boolean;
  readonly start: Date;
  readonly end: Date;
  readonly points: ReadonlyArray<OEEBarSeries>;
}

function format_oee_hint(p: OEEBarPoint): ReadonlyArray<{ title: string; value: string }> {
  return [
    { title: "Day", value: p.x },
    { title: "Actual Hours", value: p.y.toFixed(1) },
    { title: "Planned Hours", value: p.planned.toFixed(1) }
  ];
}

const actualOeeColor = "#6200EE";
const plannedOeeColor = "#03DAC5";

function StationOee(props: OEEProps) {
  const [hoveredSeries, setHoveredSeries] = React.useState<{ station: string; day: string } | undefined>(undefined);
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <HourglassIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>OEE</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}>
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
      />
      <CardContent>
        <Grid container>
          {props.points.map((series, idx) => (
            <Grid item xs={12} md={6} key={idx}>
              <div>
                <FlexibleWidthXYPlot
                  xType="ordinal"
                  height={window.innerHeight / 2 - 200}
                  animation
                  yDomain={[0, 24]}
                  onMouseLeave={() => setHoveredSeries(undefined)}
                >
                  <XAxis />
                  <YAxis />
                  <VerticalBarSeries
                    data={series.points}
                    onValueMouseOver={(p: OEEBarPoint) => setHoveredSeries({ station: series.station, day: p.x })}
                    onValueMouseOut={() => setHoveredSeries(undefined)}
                    color={actualOeeColor}
                  />
                  <VerticalBarSeries
                    data={series.points}
                    getY={(p: OEEBarPoint) => p.planned}
                    color={plannedOeeColor}
                    onValueMouseOver={(p: OEEBarPoint) => setHoveredSeries({ station: series.station, day: p.x })}
                    onValueMouseOut={() => setHoveredSeries(undefined)}
                  />
                  {hoveredSeries === undefined || hoveredSeries.station !== series.station ? (
                    undefined
                  ) : (
                    <Hint
                      value={series.points.find((p: OEEBarPoint) => p.x === hoveredSeries.day)}
                      format={format_oee_hint}
                    />
                  )}
                </FlexibleWidthXYPlot>
                <div style={{ textAlign: "center" }}>
                  {props.points.length > 1 ? (
                    <DiscreteColorLegend
                      orientation="horizontal"
                      items={[
                        { title: series.station + " Actual", color: actualOeeColor },
                        { title: series.station + " Planned", color: plannedOeeColor }
                      ]}
                    />
                  ) : (
                    undefined
                  )}
                </div>
              </div>
            </Grid>
          ))}
        </Grid>
      </CardContent>
    </Card>
  );
}

const oeePointsSelector = createSelector(
  (last30: events.Last30Days, _: boolean) => last30.cycles.part_cycles,
  (last30: events.Last30Days, _: boolean) => last30.sim_use.station_use,
  (_: events.Last30Days, showLabor: boolean) => showLabor,
  (cycles, statUse, showLabor): ReadonlyArray<OEEBarSeries> => {
    const start = addDays(startOfToday(), -6);
    const end = addDays(startOfToday(), 1);
    const filteredCycles = LazySeq.ofIterable(cycles).filter(
      e => showLabor === e.isLabor && e.x >= start && e.x <= end
    );
    const actualBins = events.binCyclesByDayAndStat(filteredCycles, c => c.active);
    const filteredStatUse = LazySeq.ofIterable(statUse).filter(
      e => showLabor === e.station.startsWith("L/U") && e.end >= start && e.start <= end
    );
    const plannedBins = events.binSimStationUseByDayAndStat(
      filteredStatUse,
      c => c.utilizationTime - c.plannedDownTime
    );

    const series: Array<OEEBarSeries> = [];
    const statNames = actualBins
      .keySet()
      .addAll(plannedBins.keySet())
      .map(e => e.station)
      .toArray({ sortOn: x => x });

    for (let stat of statNames) {
      const points: Array<OEEBarPoint> = [];
      for (let d = start; d <= end; d = addDays(d, 1)) {
        const dAndStat = new DayAndStation(d, stat);
        const actual = actualBins.get(dAndStat);
        const planned = plannedBins.get(dAndStat);
        points.push({
          x: d.toLocaleDateString(),
          y: actual.getOrElse(0) / 60,
          planned: planned.getOrElse(0) / 60
        });
      }
      series.push({
        station: stat,
        points: points
      });
    }
    return series;
  }
);

const ConnectedLoadOEE = connect((st: Store) => {
  return {
    showLabor: true,
    start: addDays(startOfToday(), -6),
    end: addDays(startOfToday(), 1),
    points: oeePointsSelector(st.Events.last30, true)
  };
})(StationOee);

const ConnectedMachineOEE = connect((st: Store) => {
  return {
    showLabor: false,
    start: addDays(startOfToday(), -6),
    end: addDays(startOfToday(), 1),
    points: oeePointsSelector(st.Events.last30, false)
  };
})(StationOee);

// -----------------------------------------------------------------------------------
// Main
// -----------------------------------------------------------------------------------

export function OperationLoadUnload() {
  return (
    <DocumentTitle title="Load/Unload Management - FMS Insight">
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
    </DocumentTitle>
  );
}

export function OperationMachines() {
  return (
    <DocumentTitle title="Machine Management - FMS Insight">
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
    </DocumentTitle>
  );
}
