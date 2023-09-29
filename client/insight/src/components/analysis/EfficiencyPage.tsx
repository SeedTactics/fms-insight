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

import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { SelectableHeatChart } from "./HeatChart.js";
import {
  binSimStationUseByDayAndStat,
  copyOeeHeatmapToClipboard,
  binActiveCyclesByDayAndStat,
  DayAndStation,
  binOccupiedCyclesByDayAndStat,
  binDowntimeToDayAndStat,
} from "../../data/results.oee.js";
import {
  binCyclesByDayAndPart,
  binSimProductionByDayAndPart,
  copyCompletedPartsHeatmapToClipboard,
} from "../../data/results.completed-parts.js";
import { last30SimStationUse, specificMonthSimStationUse } from "../../cell-status/sim-station-use.js";
import {
  last30SimProduction,
  SimPartCompleted,
  specificMonthSimProduction,
} from "../../cell-status/sim-production.js";
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
import { useSetTitle } from "../routes.js";
import { atom, useAtom, useAtomValue } from "jotai";

// --------------------------------------------------------------------------------
// Oee Heatmap
// --------------------------------------------------------------------------------

type StationOeeHeatmapTypes = "Standard OEE" | "Planned OEE" | "Occupied";

function dayAndStatToHeatmapPoints(
  pts: HashMap<DayAndStation, number>,
  downtime: HashMap<DayAndStation, number>,
) {
  return LazySeq.of(pts)
    .map(([dayAndStat, val]) => {
      const downMins = downtime.get(dayAndStat) ?? 0;
      const pct = downMins < 24 * 60 ? Math.max(val / (24 * 60 - downMins), 0) : 0;
      return {
        x: dayAndStat.day,
        y: dayAndStat.station,
        color: Math.min(pct, 1),
        label: downMins < 24 * 60 ? (pct * 100).toFixed(1) + "%" : "Planned Downtime",
      };
    })
    .toSortedArray((p) => p.x.getTime(), { desc: (p) => p.y });
}

const selectedStationOeeHeatmapType = atom<StationOeeHeatmapTypes>("Standard OEE");

export function StationOeeHeatmap() {
  useSetTitle("Station OEE");
  const [selected, setSelected] = useAtom(selectedStationOeeHeatmapType);

  const period = useAtomValue(selectedAnalysisPeriod);
  const dateRange: [Date, Date] =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  const cycles = useAtomValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const statUse = useAtomValue(period.type === "Last30" ? last30SimStationUse : specificMonthSimStationUse);
  const points = React.useMemo(() => {
    const downtime = binDowntimeToDayAndStat(statUse);
    if (selected === "Standard OEE") {
      return dayAndStatToHeatmapPoints(binActiveCyclesByDayAndStat(cycles.valuesToLazySeq()), downtime);
    } else if (selected === "Occupied") {
      return dayAndStatToHeatmapPoints(binOccupiedCyclesByDayAndStat(cycles.valuesToLazySeq()), downtime);
    } else {
      return dayAndStatToHeatmapPoints(binSimStationUseByDayAndStat(statUse, downtime), downtime);
    }
  }, [selected, cycles, statUse]);

  return (
    <SelectableHeatChart<StationOeeHeatmapTypes>
      label="Station Usage Per Day"
      y_title="Station"
      label_title={selected === "Occupied" ? "Occupied" : "OEE"}
      dateRange={dateRange}
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

const selectedCompletedPartsHeatmapType = atom<CompletedPartsHeatmapTypes>("Completed");

function partsCompletedPoints(
  partCycles: Iterable<PartCycleData>,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  start: Date,
  end: Date,
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

export function CompletedCountHeatmap() {
  useSetTitle("Part Production");
  const [selected, setSelected] = useAtom(selectedCompletedPartsHeatmapType);

  const period = useAtomValue(selectedAnalysisPeriod);
  const dateRange: [Date, Date] =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  const cycles = useAtomValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const productionCounts = useAtomValue(
    period.type === "Last30" ? last30SimProduction : specificMonthSimProduction,
  );
  const matSummary = useAtomValue(
    period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary,
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
      label="Part Production Per Day"
      y_title="Part"
      label_title={selected}
      dateRange={dateRange}
      cur_selected={selected}
      options={["Completed", "Planned"]}
      setSelected={setSelected}
      points={points}
      onExport={() => copyCompletedPartsHeatmapToClipboard(points)}
    />
  );
}
