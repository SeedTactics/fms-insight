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
import { addMonths, addDays, startOfToday } from "date-fns";
import { Extension as ExtensionIcon, HourglassFull as HourglassIcon } from "@mui/icons-material";

import AnalysisSelectToolbar from "./AnalysisSelectToolbar.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { SelectableHeatChart } from "./HeatChart.js";
import { InspectionSankey } from "./InspectionSankey.js";
import {
  binSimStationUseByDayAndStat,
  copyOeeHeatmapToClipboard,
  binActiveCyclesByDayAndStat,
  DayAndStation,
  binOccupiedCyclesByDayAndStat,
} from "../../data/results.oee.js";
import {
  binCyclesByDayAndPart,
  binSimProductionByDayAndPart,
  copyCompletedPartsHeatmapToClipboard,
} from "../../data/results.completed-parts.js";
import { DataTableActionZoomType } from "./DataTable.js";
import { BufferOccupancyChart } from "./BufferChart.js";
import { useRecoilValue } from "recoil";
import { last30SimStationUse, specificMonthSimStationUse } from "../../cell-status/sim-station-use.js";
import {
  last30SimProduction,
  SimPartCompleted,
  specificMonthSimProduction,
} from "../../cell-status/sim-production.js";
import { last30Inspections, specificMonthInspections } from "../../cell-status/inspections.js";
import {
  last30MaterialSummary,
  specificMonthMaterialSummary,
  MaterialSummaryAndCompletedData,
} from "../../cell-status/material-summary.js";
import {
  last30StationCycles,
  PartCycleData,
  specificMonthStationCycles,
} from "../../cell-status/station-cycles.js";
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";
import { PartLoadStationCycleChart, PartMachineCycleChart } from "./PartCycleCards.js";
import { PalletCycleChart } from "./PalletCycleCards.js";

// --------------------------------------------------------------------------------
// Oee Heatmap
// --------------------------------------------------------------------------------

type StationOeeHeatmapTypes = "Standard OEE" | "Planned OEE" | "Occupied";

function dayAndStatToHeatmapPoints(pts: HashMap<DayAndStation, number>) {
  return LazySeq.of(pts)
    .map(([dayAndStat, val]) => {
      const pct = val / (24 * 60);
      return {
        x: dayAndStat.day,
        y: dayAndStat.station,
        color: Math.min(pct, 1),
        label: (pct * 100).toFixed(1) + "%",
      };
    })
    .toSortedArray((p) => p.x.getTime(), { desc: (p) => p.y });
}

function StationOeeHeatmap() {
  const [selected, setSelected] = React.useState<StationOeeHeatmapTypes>("Standard OEE");

  const period = useRecoilValue(selectedAnalysisPeriod);
  const dateRange: [Date, Date] =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  const cycles = useRecoilValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const statUse = useRecoilValue(period.type === "Last30" ? last30SimStationUse : specificMonthSimStationUse);
  const points = React.useMemo(() => {
    if (selected === "Standard OEE") {
      return dayAndStatToHeatmapPoints(binActiveCyclesByDayAndStat(cycles.valuesToLazySeq()));
    } else if (selected === "Occupied") {
      return dayAndStatToHeatmapPoints(binOccupiedCyclesByDayAndStat(cycles.valuesToLazySeq()));
    } else {
      return dayAndStatToHeatmapPoints(binSimStationUseByDayAndStat(statUse));
    }
  }, [selected, cycles, statUse]);

  return (
    <SelectableHeatChart<StationOeeHeatmapTypes>
      card_label="Station Use"
      y_title="Station"
      label_title={selected === "Occupied" ? "Occupied" : "OEE"}
      dateRange={dateRange}
      icon={<HourglassIcon style={{ color: "#6D4C41" }} />}
      cur_selected={selected}
      options={["Standard OEE", "Occupied", "Planned OEE"]}
      setSelected={setSelected}
      points={points}
      onExport={() => copyOeeHeatmapToClipboard("Station", points)}
    />
  );
}

// --------------------------------------------------------------------------------
// Completed Heatmap
// --------------------------------------------------------------------------------

type CompletedPartsHeatmapTypes = "Planned" | "Completed";

function partsCompletedPoints(
  partCycles: Iterable<PartCycleData>,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  start: Date,
  end: Date
) {
  const pts = binCyclesByDayAndPart(partCycles, matsById, start, end);
  return LazySeq.of(pts)
    .map(([dayAndPart, val]) => {
      return {
        x: dayAndPart.day,
        y: dayAndPart.part,
        color: val.activeMachineMins,
        label: val.count.toFixed(0) + " (" + (val.activeMachineMins / 60).toFixed(1) + " hours)",
        count: val.count,
        activeMachineMins: val.activeMachineMins,
      };
    })
    .toSortedArray((p) => p.x.getTime(), { desc: (p) => p.y });
}

function partsPlannedPoints(prod: Iterable<SimPartCompleted>) {
  const pts = binSimProductionByDayAndPart(prod);
  return LazySeq.of(pts)
    .map(([dayAndPart, val]) => {
      return {
        x: dayAndPart.day,
        y: dayAndPart.part,
        color: val.activeMachineMins,
        label: val.count.toFixed(0) + " (" + (val.activeMachineMins / 60).toFixed(1) + " hours)",
        count: val.count,
        activeMachineMins: val.activeMachineMins,
      };
    })
    .toSortedArray((p) => p.x.getTime(), { desc: (p) => p.y });
}

function CompletedCountHeatmap() {
  const [selected, setSelected] = React.useState<CompletedPartsHeatmapTypes>("Completed");

  const period = useRecoilValue(selectedAnalysisPeriod);
  const dateRange: [Date, Date] =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  const cycles = useRecoilValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const productionCounts = useRecoilValue(
    period.type === "Last30" ? last30SimProduction : specificMonthSimProduction
  );
  const matSummary = useRecoilValue(
    period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary
  );
  const points = React.useMemo(() => {
    if (selected === "Completed") {
      const today = startOfToday();
      const start = period.type === "Last30" ? addDays(today, -30) : period.month;
      const endD = period.type === "Last30" ? addDays(today, 1) : addMonths(period.month, 1);
      return partsCompletedPoints(cycles.valuesToLazySeq(), matSummary.matsById, start, endD);
    } else {
      return partsPlannedPoints(productionCounts);
    }
  }, [selected, cycles, matSummary, productionCounts]);
  return (
    <SelectableHeatChart
      card_label="Part Production"
      y_title="Part"
      label_title={selected}
      dateRange={dateRange}
      icon={<ExtensionIcon style={{ color: "#6D4C41" }} />}
      cur_selected={selected}
      options={["Completed", "Planned"]}
      setSelected={setSelected}
      points={points}
      onExport={() => copyCompletedPartsHeatmapToClipboard(points)}
    />
  );
}

// --------------------------------------------------------------------------------
// Inspection
// --------------------------------------------------------------------------------

function ConnectedInspection() {
  const period = useRecoilValue(selectedAnalysisPeriod);

  const inspectionlogs = useRecoilValue(
    period.type === "Last30" ? last30Inspections : specificMonthInspections
  );
  const zoomType =
    period.type === "Last30" ? DataTableActionZoomType.Last30Days : DataTableActionZoomType.ZoomIntoRange;
  const default_date_range =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  return (
    <InspectionSankey
      inspectionlogs={inspectionlogs}
      zoomType={zoomType}
      default_date_range={default_date_range}
      defaultToTable={false}
    />
  );
}

// --------------------------------------------------------------------------------
// Efficiency
// --------------------------------------------------------------------------------

export function EfficiencyCards(): JSX.Element {
  return (
    <>
      <div data-testid="part-cycle-chart">
        <PartMachineCycleChart />
      </div>
      <div data-testid="part-load-cycle-chart" style={{ marginTop: "3em" }}>
        <PartLoadStationCycleChart />
      </div>
      <div data-testid="pallet-cycle-chart" style={{ marginTop: "3em" }}>
        <PalletCycleChart />
      </div>
      <div data-testid="buffer-chart" style={{ marginTop: "3em" }}>
        <BufferOccupancyChart />
      </div>
      <div data-testid="station-oee-heatmap" style={{ marginTop: "3em" }}>
        <StationOeeHeatmap />
      </div>
      <div data-testid="completed-heatmap" style={{ marginTop: "3em" }}>
        <CompletedCountHeatmap />
      </div>
      <div data-testid="inspection-sankey" style={{ marginTop: "3em" }}>
        <ConnectedInspection />
      </div>
    </>
  );
}

export default function Efficiency(): JSX.Element {
  React.useEffect(() => {
    document.title = "Efficiency - FMS Insight";
  }, []);
  return (
    <>
      <AnalysisSelectToolbar />
      <main style={{ padding: "24px" }}>
        <EfficiencyCards />
      </main>
    </>
  );
}
