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
import * as im from "immutable";
import WorkIcon from "@material-ui/icons/Work";
import BasketIcon from "@material-ui/icons/ShoppingBasket";
import { addMonths, addDays } from "date-fns";
import { createSelector } from "reselect";
import ExtensionIcon from "@material-ui/icons/Extension";
import HourglassIcon from "@material-ui/icons/HourglassFull";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

import AnalysisSelectToolbar from "../AnalysisSelectToolbar";
import { SelectableCycleChart, CycleChartPoint, ExtraTooltip } from "./CycleChart";
import { SelectableHeatChart, HeatChartPoint } from "./HeatChart";
import * as events from "../../data/events";
import { Store, connect, mkAC } from "../../store/store";
import * as guiState from "../../data/gui-state";
import * as matDetails from "../../data/material-details";
import InspectionSankey from "./InspectionSankey";
import { PartCycleData } from "../../data/events.cycles";
import { MaterialDialog } from "../station-monitor/Material";
import { LazySeq } from "../../data/iterators";

// --------------------------------------------------------------------------------
// Station Cycles
// --------------------------------------------------------------------------------

const ConnectedMaterialDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog)
  }
)(MaterialDialog);

interface PartStationCycleChartProps {
  readonly points: im.Map<string, im.Map<string, ReadonlyArray<events.CycleData>>>;
  readonly default_date_range?: Date[];
  readonly selected?: string;
  readonly setSelected: (s: string) => void;
  readonly openMaterial: (matId: number) => void;
}

function PartStationCycleChart(props: PartStationCycleChartProps) {
  function extraStationCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
    const partC = point as PartCycleData;
    return [
      {
        title: "Material",
        value: "Open Card",
        link: () => props.openMaterial(partC.matId)
      }
    ];
  }

  return (
    <SelectableCycleChart
      select_label="Part"
      series_label="Station"
      card_label="Station Cycles"
      icon={<WorkIcon style={{ color: "#6D4C41" }} />}
      extra_tooltip={extraStationCycleTooltip}
      useIdenticon
      {...props}
    />
  );
}

function stationCycleSelector(st: Store) {
  if (st.Events.analysis_period === events.AnalysisPeriod.Last30Days) {
    const now = new Date();
    const oneMonthAgo = addDays(now, -30);
    return {
      points: st.Events.last30.cycles.by_part_then_stat,
      selected: st.Gui.station_cycle_selected_part,
      default_date_range: [now, oneMonthAgo]
    };
  } else {
    return {
      points: st.Events.selected_month.cycles.by_part_then_stat,
      selected: st.Gui.station_cycle_selected_part,
      default_date_range: [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)]
    };
  }
}

const ConnectedPartStationCycleChart = connect(
  stationCycleSelector,
  {
    setSelected: (s: string) => ({
      type: guiState.ActionType.SetSelectedStationCyclePart,
      part: s
    }),
    openMaterial: matDetails.openMaterialById
  }
)(PartStationCycleChart);

// --------------------------------------------------------------------------------
// Pallet Cycles
// --------------------------------------------------------------------------------

interface PalletCycleChartProps {
  readonly points: im.Map<string, ReadonlyArray<events.CycleData>>;
  readonly default_date_range?: Date[];
  readonly selected?: string;
  readonly setSelected: (s: string) => void;
}

function PalletCycleChart(props: PalletCycleChartProps) {
  const points = props.points.map((cs, pal) => im.Map({ [pal]: cs }));
  return (
    <SelectableCycleChart
      select_label="Pallet"
      series_label="Pallet"
      card_label="Pallet Cycles"
      icon={<BasketIcon style={{ color: "#6D4C41" }} />}
      selected={props.selected}
      setSelected={props.setSelected}
      points={points}
    />
  );
}

function palletCycleSelector(st: Store) {
  if (st.Events.analysis_period === events.AnalysisPeriod.Last30Days) {
    const now = new Date();
    const oneMonthAgo = addDays(now, -30);
    return {
      points: st.Events.last30.cycles.by_pallet,
      selected: st.Gui.pallet_cycle_selected,
      default_date_range: [now, oneMonthAgo]
    };
  } else {
    return {
      points: st.Events.selected_month.cycles.by_pallet,
      selected: st.Gui.pallet_cycle_selected,
      default_date_range: [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)]
    };
  }
}

const ConnectedPalletCycleChart = connect(
  palletCycleSelector,
  {
    setSelected: (p: string) => ({
      type: guiState.ActionType.SetSelectedPalletCycle,
      pallet: p
    })
  }
)(PalletCycleChart);

// --------------------------------------------------------------------------------
// Oee Heatmap
// --------------------------------------------------------------------------------

interface HeatmapProps {
  readonly planned_or_actual: guiState.PlannedOrActual;
  readonly setType: (p: guiState.PlannedOrActual) => void;
  readonly points: ReadonlyArray<HeatChartPoint>;
}

function StationOeeHeatmap(props: HeatmapProps) {
  return (
    <SelectableHeatChart
      card_label="Station OEE"
      label_title="OEE"
      icon={<HourglassIcon style={{ color: "#6D4C41" }} />}
      {...props}
    />
  );
}

const stationOeeActualPointsSelector = createSelector(
  (cycles: events.CycleState) => cycles.by_part_then_stat,
  byPartThenStat => {
    let pts = events.binCyclesByDayAndStat(byPartThenStat, c => c.active);
    return pts
      .toSeq()
      .map((val, dayAndStat) => {
        const pct = val / (24 * 60);
        return {
          x: dayAndStat.get("day", null),
          y: dayAndStat.get("station", null),
          color: pct,
          label: (pct * 100).toFixed(1) + "%"
        };
      })
      .valueSeq()
      .sortBy(p => p.x)
      .sortBy(p => p.y, (a, b) => (a === b ? 0 : a < b ? 1 : -1)) // descending
      .toArray();
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
        const cmp = p2.y.localeCompare(p1.y); // descending, compare p2 to p1
        if (cmp === 0) {
          return p1.x.getTime() - p2.x.getTime();
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
      label_title={props.planned_or_actual === guiState.PlannedOrActual.Actual ? "Completed" : "Planned"}
      icon={<ExtensionIcon style={{ color: "#6D4C41" }} />}
      {...props}
    />
  );
}

const completedActualPointsSelector = createSelector(
  (cycles: events.CycleState) => cycles.by_part_then_stat,
  byPartThenStat => {
    let pts = events.binCyclesByDayAndPart(byPartThenStat, c => (c.completed ? 1 : 0));
    return pts
      .toSeq()
      .map((val, dayAndStat) => {
        return {
          x: dayAndStat.get("day", null),
          y: dayAndStat.get("part", null),
          color: val,
          label: val.toFixed(1)
        };
      })
      .valueSeq()
      .sortBy(p => p.x)
      .sortBy(p => p.y, (a, b) => (a === b ? 0 : a < b ? 1 : -1)) // descending
      .toArray();
  }
);

const completedPlannedPointsSelector = createSelector(
  (simUse: events.SimUseState) => simUse.production,
  production => {
    let pts = events.binSimProductionByDayAndPart(production);
    return LazySeq.ofIterable(pts)
      .map(([dayAndStat, val]) => {
        return {
          x: dayAndStat.day,
          y: dayAndStat.part,
          color: val,
          label: val.toFixed(1)
        };
      })
      .toArray()
      .sort((p1, p2) => {
        const cmp = p2.y.localeCompare(p1.y); // descending, compare p2 to p1
        if (cmp === 0) {
          return p1.x.getTime() - p2.x.getTime();
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

export default function Efficiency() {
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
            <ConnectedStationOeeHeatmap />
          </div>
          <div data-testid="completed-heatmap" style={{ marginTop: "3em" }}>
            <ConnectedCompletedCountHeatmap />
          </div>
          <div data-testid="inspection-sankey" style={{ marginTop: "3em" }}>
            <InspectionSankey />
          </div>
          <ConnectedMaterialDialog />
        </main>
      </>
    </DocumentTitle>
  );
}
