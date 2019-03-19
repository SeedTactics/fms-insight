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
import WorkIcon from "@material-ui/icons/Work";
import BasketIcon from "@material-ui/icons/ShoppingBasket";
import { addMonths, addDays, startOfToday } from "date-fns";
import { createSelector } from "reselect";
import ExtensionIcon from "@material-ui/icons/Extension";
import HourglassIcon from "@material-ui/icons/HourglassFull";
import { HashMap, Vector, HashSet } from "prelude-ts";
import Card from "@material-ui/core/Card";
import CardHeader from "@material-ui/core/CardHeader";
import Select from "@material-ui/core/Select";
import MenuItem from "@material-ui/core/MenuItem";
import CardContent from "@material-ui/core/CardContent";
import Tooltip from "@material-ui/core/Tooltip";
import IconButton from "@material-ui/core/IconButton";
import ImportExport from "@material-ui/icons/ImportExport";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

import AnalysisSelectToolbar from "./AnalysisSelectToolbar";
import { CycleChart, CycleChartPoint, ExtraTooltip } from "./CycleChart";
import { SelectableHeatChart, HeatChartPoint } from "./HeatChart";
import * as events from "../../data/events";
import { Store, connect, mkAC, DispatchAction } from "../../store/store";
import * as guiState from "../../data/gui-state";
import * as matDetails from "../../data/material-details";
import InspectionSankey from "./InspectionSankey";
import {
  PartCycleData,
  filterStationCycles,
  FilteredStationCycles,
  FilterAnyMachineKey,
  FilterAnyLoadKey
} from "../../data/events.cycles";
import { PartIdenticon } from "../station-monitor/Material";
import { LazySeq } from "../../data/lazyseq";
import { copyCyclesToClipboard } from "../../data/clipboard-table";
import StationDataTable from "./StationDataTable";

// --------------------------------------------------------------------------------
// Station Cycles
// --------------------------------------------------------------------------------

interface PartStationCycleChartProps {
  readonly allParts: HashSet<string>;
  readonly stationNames: HashSet<string>;
  readonly palletNames: HashSet<string>;
  readonly points: FilteredStationCycles;
  readonly default_date_range: Date[];
  readonly analysisPeriod: events.AnalysisPeriod;
  readonly selectedPart?: string;
  readonly selectedPallet?: string;
  readonly selectedStation?: string;
  readonly zoomDateRange?: { start: Date; end: Date };
  readonly setSelected: DispatchAction<guiState.ActionType.SetSelectedStationCycle>;
  readonly setZoomRange: DispatchAction<guiState.ActionType.SetStationCycleDateZoom>;
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

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <WorkIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Station Cycles</div>
            <div style={{ flexGrow: 1 }} />
            {props.points.data.length() > 0 ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  onClick={() => copyCyclesToClipboard(props.points, props.zoomDateRange)}
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
                  pallet: props.selectedPallet,
                  station: props.selectedStation
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
              name="Station-Cycles-cycle-chart-station-select"
              autoWidth
              displayEmpty
              value={props.selectedStation || ""}
              style={{ marginLeft: "1em" }}
              onChange={e =>
                props.setSelected({
                  station: e.target.value === "" ? undefined : e.target.value,
                  pallet: props.selectedPallet,
                  part: props.selectedPart
                })
              }
            >
              <MenuItem key={0} value="">
                <em>Any Station</em>
              </MenuItem>
              <MenuItem key={1} value={FilterAnyMachineKey}>
                <em>Any Machine</em>
              </MenuItem>
              <MenuItem key={2} value={FilterAnyLoadKey}>
                <em>Any Load Station</em>
              </MenuItem>
              {props.stationNames.toArray({ sortOn: x => x }).map(n => (
                <MenuItem key={n} value={n}>
                  <div style={{ display: "flex", alignItems: "center" }}>
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
                  station: props.selectedStation,
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
            current_date_zoom={props.zoomDateRange}
            set_date_zoom_range={props.setZoomRange}
          />
        ) : (
          <StationDataTable
            points={props.points.data}
            default_date_range={props.default_date_range}
            current_date_zoom={props.zoomDateRange}
            set_date_zoom_range={props.setZoomRange}
            last30_days={props.analysisPeriod === events.AnalysisPeriod.Last30Days}
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
    (st: Store) =>
      st.Events.analysis_period === events.AnalysisPeriod.Last30Days
        ? st.Events.last30.cycles.part_cycles
        : st.Events.selected_month.cycles.part_cycles,
    (st: Store) => st.Gui.station_cycle_selected_part,
    (st: Store) => st.Gui.station_cycle_selected_pallet,
    (st: Store) => st.Gui.station_cycle_selected_station
  ],
  (
    cycles: Vector<PartCycleData>,
    part: string | undefined,
    pallet: string | undefined,
    station: string | undefined
  ) => {
    if (part || pallet || station) {
      return filterStationCycles(cycles, undefined, part, pallet, station);
    } else {
      return { seriesLabel: "Station", data: HashMap.empty<string, ReadonlyArray<PartCycleData>>() };
    }
  }
);

const ConnectedPartStationCycleChart = connect(
  st => ({
    allParts:
      st.Events.analysis_period === events.AnalysisPeriod.Last30Days
        ? st.Events.last30.cycles.part_and_proc_names
        : st.Events.selected_month.cycles.part_and_proc_names,
    points: stationCyclePointsSelector(st),
    selectedPart: st.Gui.station_cycle_selected_part,
    selectedPallet: st.Gui.station_cycle_selected_pallet,
    selectedStation: st.Gui.station_cycle_selected_station,
    zoomDateRange: st.Gui.station_cycle_date_zoom,
    analysisPeriod: st.Events.analysis_period,
    stationNames:
      st.Events.analysis_period === events.AnalysisPeriod.Last30Days
        ? st.Events.last30.cycles.station_names
        : st.Events.selected_month.cycles.station_names,
    palletNames:
      st.Events.analysis_period === events.AnalysisPeriod.Last30Days
        ? st.Events.last30.cycles.pallet_names
        : st.Events.selected_month.cycles.pallet_names,
    default_date_range:
      st.Events.analysis_period === events.AnalysisPeriod.Last30Days
        ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
        : [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)]
  }),
  {
    setSelected: mkAC(guiState.ActionType.SetSelectedStationCycle),
    setZoomRange: mkAC(guiState.ActionType.SetStationCycleDateZoom),
    openMaterial: matDetails.openMaterialById
  }
)(PartStationCycleChart);

// --------------------------------------------------------------------------------
// Pallet Cycles
// --------------------------------------------------------------------------------

interface PalletCycleChartProps {
  readonly points: HashMap<string, ReadonlyArray<events.CycleData>>;
  readonly default_date_range: Date[];
  readonly selected?: string;
  readonly setSelected: (s: string) => void;
  readonly zoomDateRange?: { start: Date; end: Date };
  readonly setZoomRange: DispatchAction<guiState.ActionType.SetStationCycleDateZoom>;
}

function PalletCycleChart(props: PalletCycleChartProps) {
  let points = HashMap.empty<string, ReadonlyArray<events.CycleData>>();
  if (props.selected) {
    const palData = props.points.get(props.selected);
    if (palData.isSome()) {
      points = HashMap.of([props.selected, palData.get()]);
    }
  }
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <BasketIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Pallet Cycles</div>
            <div style={{ flexGrow: 1 }} />
            <Select
              name={"Pallet-Cycles-cycle-chart-select"}
              autoWidth
              displayEmpty
              value={props.selected || ""}
              onChange={e => props.setSelected(e.target.value)}
            >
              {props.selected ? (
                undefined
              ) : (
                <MenuItem key={0} value="">
                  <em>Select Pallet</em>
                </MenuItem>
              )}
              {props.points
                .keySet()
                .toArray({ sortOn: x => x })
                .map(n => (
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
        <CycleChart
          points={points}
          series_label="Pallet"
          default_date_range={props.default_date_range}
          current_date_zoom={props.zoomDateRange}
          set_date_zoom_range={props.setZoomRange}
        />
      </CardContent>
    </Card>
  );
}

function palletCycleSelector(st: Store) {
  if (st.Events.analysis_period === events.AnalysisPeriod.Last30Days) {
    const now = addDays(startOfToday(), 1);
    const oneMonthAgo = addDays(now, -30);
    return {
      points: st.Events.last30.cycles.by_pallet,
      selected: st.Gui.pallet_cycle_selected,
      default_date_range: [oneMonthAgo, now],
      zoomDateRange: st.Gui.pallet_cycle_date_zoom
    };
  } else {
    return {
      points: st.Events.selected_month.cycles.by_pallet,
      selected: st.Gui.pallet_cycle_selected,
      default_date_range: [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)],
      zoomDateRange: st.Gui.pallet_cycle_date_zoom
    };
  }
}

const ConnectedPalletCycleChart = connect(
  palletCycleSelector,
  {
    setSelected: (p: string) => ({
      type: guiState.ActionType.SetSelectedPalletCycle,
      pallet: p
    }),
    setZoomRange: mkAC(guiState.ActionType.SetStationCycleDateZoom)
  }
)(PalletCycleChart);

// --------------------------------------------------------------------------------
// Oee Heatmap
// --------------------------------------------------------------------------------

interface HeatmapProps {
  readonly planned_or_actual: guiState.PlannedOrActual;
  readonly setType: (p: guiState.PlannedOrActual) => void;
  readonly points: ReadonlyArray<HeatChartPoint>;
  readonly allowSetType: boolean;
}

function StationOeeHeatmap(props: HeatmapProps) {
  return (
    <SelectableHeatChart
      card_label="Station OEE"
      y_title="Station"
      label_title="OEE"
      icon={<HourglassIcon style={{ color: "#6D4C41" }} />}
      planned_or_actual={props.planned_or_actual}
      points={props.points}
      setType={props.allowSetType ? props.setType : undefined}
    />
  );
}

const stationOeeActualPointsSelector = createSelector(
  (cycles: events.CycleState) => cycles.part_cycles,
  cycles => {
    let pts = events.binCyclesByDayAndStat(cycles, c => c.active);
    return LazySeq.ofIterable(pts)
      .map(([dayAndStat, val]) => {
        const pct = val / (24 * 60);
        return {
          x: dayAndStat.day,
          y: dayAndStat.station,
          color: pct,
          label: (pct * 100).toFixed(1) + "%"
        };
      })
      .toArray()
      .sort((p1, p2) => {
        const cmp = p1.x.getTime() - p2.x.getTime();
        if (cmp === 0) {
          return p2.y.localeCompare(p1.y); // descending, compare p2 to p1
        } else {
          return cmp;
        }
      });
  }
);

const stationOeePlannedPointsSelector = createSelector(
  (sim: events.SimUseState) => sim.station_use,
  statUse => {
    let pts = events.binSimStationUseByDayAndStat(statUse, c => c.utilizationTime - c.plannedDownTime);
    return LazySeq.ofIterable(pts)
      .map(([dayAndStat, val]) => {
        const pct = val / (24 * 60);
        return {
          x: dayAndStat.day,
          y: dayAndStat.station,
          color: pct,
          label: (pct * 100).toFixed(1) + "%"
        };
      })
      .toArray()
      .sort((p1, p2) => {
        const cmp = p1.x.getTime() - p2.x.getTime();
        if (cmp === 0) {
          return p2.y.localeCompare(p1.y); // descending, compare p2 to p1
        } else {
          return cmp;
        }
      });
  }
);

function stationOeePoints(st: Store) {
  let cycles: events.CycleState;
  let sim: events.SimUseState;
  if (st.Events.analysis_period === events.AnalysisPeriod.Last30Days) {
    cycles = st.Events.last30.cycles;
    sim = st.Events.last30.sim_use;
  } else {
    cycles = st.Events.selected_month.cycles;
    sim = st.Events.selected_month.sim_use;
  }

  switch (st.Gui.station_oee_heatmap_type) {
    case guiState.PlannedOrActual.Actual:
      return stationOeeActualPointsSelector(cycles);
    case guiState.PlannedOrActual.Planned:
      return stationOeePlannedPointsSelector(sim);
    case guiState.PlannedOrActual.PlannedMinusActual:
      return [];
  }
}

const ConnectedStationOeeHeatmap = connect(
  (st: Store) => {
    return {
      planned_or_actual: st.Gui.station_oee_heatmap_type,
      points: stationOeePoints(st)
    };
  },
  {
    setType: (p: guiState.PlannedOrActual) => ({
      type: guiState.ActionType.SetStationOeeHeatmapType,
      ty: p
    })
  }
)(StationOeeHeatmap);

// --------------------------------------------------------------------------------
// Completed Heatmap
// --------------------------------------------------------------------------------

function CompletedCountHeatmap(props: HeatmapProps) {
  return (
    <SelectableHeatChart
      card_label="Part Production"
      y_title="Part"
      label_title={props.planned_or_actual === guiState.PlannedOrActual.Actual ? "Completed" : "Planned"}
      icon={<ExtensionIcon style={{ color: "#6D4C41" }} />}
      planned_or_actual={props.planned_or_actual}
      points={props.points}
      setType={props.allowSetType ? props.setType : undefined}
    />
  );
}

const completedActualPointsSelector = createSelector(
  (cycles: events.CycleState) => cycles.part_cycles,
  cycles => {
    let pts = events.binCyclesByDayAndPart(cycles, c => (c.completed ? 1 : 0));
    return LazySeq.ofIterable(pts)
      .map(([dayAndPart, val]) => {
        return {
          x: dayAndPart.day,
          y: dayAndPart.part,
          color: val,
          label: val.toFixed(0)
        };
      })
      .toArray()
      .sort((p1, p2) => {
        const cmp = p1.x.getTime() - p2.x.getTime();
        if (cmp === 0) {
          return p2.y.localeCompare(p1.y); // descending, compare p2 to p1
        } else {
          return cmp;
        }
      });
  }
);

const completedPlannedPointsSelector = createSelector(
  (simUse: events.SimUseState) => simUse.production,
  production => {
    let pts = events.binSimProductionByDayAndPart(production);
    return LazySeq.ofIterable(pts)
      .map(([dayAndPart, val]) => {
        return {
          x: dayAndPart.day,
          y: dayAndPart.part,
          color: val,
          label: val.toFixed(0)
        };
      })
      .toArray()
      .sort((p1, p2) => {
        const cmp = p1.x.getTime() - p2.x.getTime();
        if (cmp === 0) {
          return p2.y.localeCompare(p1.y); // descending, compare p2 to p1
        } else {
          return cmp;
        }
      });
  }
);

function completedPoints(st: Store) {
  let cycles: events.CycleState;
  let sim: events.SimUseState;
  if (st.Events.analysis_period === events.AnalysisPeriod.Last30Days) {
    cycles = st.Events.last30.cycles;
    sim = st.Events.last30.sim_use;
  } else {
    cycles = st.Events.selected_month.cycles;
    sim = st.Events.selected_month.sim_use;
  }

  switch (st.Gui.completed_count_heatmap_type) {
    case guiState.PlannedOrActual.Actual:
      return completedActualPointsSelector(cycles);
    case guiState.PlannedOrActual.Planned:
      return completedPlannedPointsSelector(sim);
    case guiState.PlannedOrActual.PlannedMinusActual:
      return [];
  }
}

const ConnectedCompletedCountHeatmap = connect(
  (st: Store) => {
    return {
      planned_or_actual: st.Gui.completed_count_heatmap_type,
      points: completedPoints(st)
    };
  },
  {
    setType: (p: guiState.PlannedOrActual) => ({
      type: guiState.ActionType.SetCompletedCountHeatmapType,
      ty: p
    })
  }
)(CompletedCountHeatmap);

// --------------------------------------------------------------------------------
// Efficiency
// --------------------------------------------------------------------------------

export default function Efficiency({ allowSetType }: { allowSetType: boolean }) {
  return (
    <DocumentTitle title="Efficiency - FMS Insight">
      <>
        <AnalysisSelectToolbar />
        <main style={{ padding: "24px" }}>
          <div data-testid="part-cycle-chart">
            <ConnectedPartStationCycleChart />
          </div>
          <div data-testid="pallet-cycle-chart" style={{ marginTop: "3em" }}>
            <ConnectedPalletCycleChart />
          </div>
          <div data-testid="station-oee-heatmap" style={{ marginTop: "3em" }}>
            <ConnectedStationOeeHeatmap allowSetType={allowSetType} />
          </div>
          <div data-testid="completed-heatmap" style={{ marginTop: "3em" }}>
            <ConnectedCompletedCountHeatmap allowSetType={allowSetType} />
          </div>
          <div data-testid="inspection-sankey" style={{ marginTop: "3em" }}>
            <InspectionSankey />
          </div>
        </main>
      </>
    </DocumentTitle>
  );
}
