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

import { addMonths, getDaysInMonth, addDays } from "date-fns";
import { MaterialSummaryAndCompletedData } from "../cell-status/material-summary.js";
import copy from "copy-to-clipboard";
import { PartCycleData } from "../cell-status/station-cycles.js";
import { HashMap, LazySeq, OrderedMap } from "@seedtactics/immutable-collections";

export interface PartCost {
  readonly part: string;
  readonly parts_completed: number;
  readonly machine: OrderedMap<string, number>;
  readonly labor: number;
  readonly automation: number;
}

export type MachineCostPerYear = { readonly [stationGroup: string]: number };

export interface CostData {
  readonly machineQuantities: OrderedMap<string, number>;
  readonly type: { month: Date } | { thirtyDaysAgo: Date };
  readonly parts: ReadonlyArray<PartCost>;
}

function isUnloadCycle(c: PartCycleData): boolean {
  return c.isLabor && c.operation === "UNLOAD";
}

function isMonthType(type: { month: Date } | { thirtyDaysAgo: Date }): type is { month: Date } {
  return Object.prototype.hasOwnProperty.call(type, "month");
}

export function compute_monthly_cost_percentages(
  cycles: Iterable<PartCycleData>,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  type: { month: Date } | { thirtyDaysAgo: Date }
): CostData {
  const start = isMonthType(type) ? type.month : type.thirtyDaysAgo;
  const end = isMonthType(type) ? addMonths(type.month, 1) : addDays(type.thirtyDaysAgo, 31);

  let totalPalletCycles = 0;
  const totalStatUseMinutes = new Map<string, number>();
  let totalLaborUseMinutes = 0;
  const stationCount = new Map<string, Set<number>>();

  for (const c of cycles) {
    if (c.x < start || c.x > end) continue;
    if (isUnloadCycle(c)) {
      totalPalletCycles += 1;
    }
    if (c.isLabor) {
      totalLaborUseMinutes += c.activeMinutes;
    } else {
      totalStatUseMinutes.set(
        c.stationGroup,
        c.activeMinutes + (totalStatUseMinutes.get(c.stationGroup) ?? 0)
      );
      const s = stationCount.get(c.stationGroup);
      if (s) {
        s.add(c.stationNumber);
      } else {
        stationCount.set(c.stationGroup, new Set<number>([c.stationNumber]));
      }
    }
  }

  const completed = LazySeq.of(matsById)
    .filter(([, details]) => {
      if (details.numProcesses === undefined) return false;
      const unload = details.unloaded_processes?.[details.numProcesses];
      return !!unload && unload >= start && unload <= end;
    })
    .toRMap(
      ([, details]) => [details.partName, 1],
      (v1, v2) => v1 + v2
    );

  const parts = LazySeq.of(cycles)
    .filter((c) => c.x >= start && c.x <= end)
    .groupBy((c) => c.part)
    .map(([partName, forPart]) => ({
      part: partName,
      parts_completed: completed.get(partName) ?? 0,
      machine: LazySeq.of(forPart)
        .filter((c) => !c.isLabor)
        .buildOrderedMap<string, number>(
          (c) => c.stationGroup,
          (old, c) => (old ?? 0) + c.activeMinutes
        )
        .mapValues((minutes, statGroup) => {
          const totalUse = totalStatUseMinutes.get(statGroup) ?? 1;
          return minutes / totalUse;
        }),
      labor: LazySeq.of(forPart)
        .filter((c) => c.isLabor)
        .sumBy((c) => c.activeMinutes / totalLaborUseMinutes),
      automation: forPart.reduce((acc, v) => (isUnloadCycle(v) ? acc + 1 : acc), 0) / totalPalletCycles,
    }))
    .toRArray();

  const machineQuantities = LazySeq.of(stationCount).toOrderedMap(([statGroup, nums]) => [
    statGroup,
    nums.size,
  ]);

  return {
    parts,
    machineQuantities,
    type,
  };
}

export function convert_cost_percent_to_cost_per_piece(
  costs: CostData,
  machineCostPerYear: MachineCostPerYear,
  automationCostPerYear: number | null,
  totalLaborCostForPeriod: number
): CostData {
  const days = isMonthType(costs.type) ? getDaysInMonth(costs.type.month) : 30;
  const automationCostForPeriod = automationCostPerYear ? (automationCostPerYear * days) / 365 : 0;
  const stationCostForPeriod = costs.machineQuantities.mapValues(
    (cnt, statGroup) => ((machineCostPerYear[statGroup] ?? 0) * cnt * days) / 365
  );

  return {
    machineQuantities: costs.machineQuantities,
    type: costs.type,
    parts: costs.parts.map((p) => ({
      part: p.part,
      parts_completed: p.parts_completed,
      machine: p.machine.mapValues((pct, statGroup) =>
        p.parts_completed > 0 ? (pct * (stationCostForPeriod.get(statGroup) ?? 0)) / p.parts_completed : 0
      ),
      labor: p.parts_completed > 0 ? (p.labor * totalLaborCostForPeriod) / p.parts_completed : 0,
      automation: p.parts_completed > 0 ? (p.automation * automationCostForPeriod) / p.parts_completed : 0,
    })),
  };
}

export function buildCostPerPieceTable(costs: CostData): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Part</th>";
  table += "<th>Completed Quantity</th>";
  for (const m of costs.machineQuantities.keys()) {
    table += "<th>" + m + " Cost %</th>";
  }
  table += "<th>Labor Cost</th>";
  table += "<th>Automation Cost</th>";
  table += "</tr></thead>\n<tbody>\n";

  const rows = LazySeq.of(costs.parts).sortBy((c) => c.part);
  const format = Intl.NumberFormat(undefined, {
    maximumFractionDigits: 1,
  });

  for (const c of rows) {
    table += "<tr><td>" + c.part + "</td>";
    table += "<td>" + c.parts_completed.toString() + "</td>";
    for (const m of costs.machineQuantities.keys()) {
      table += "<td>" + format.format(c.machine.get(m) ?? 0) + "</td>";
    }
    table += "<td>" + (c.parts_completed > 0 ? format.format(c.labor) : "0") + "</td>";
    table += "<td>" + (c.parts_completed > 0 ? format.format(c.automation) : "0") + "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyCostPerPieceToClipboard(costs: CostData): void {
  copy(buildCostPerPieceTable(costs));
}

export function buildCostBreakdownTable(costs: CostData): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Part</th>";
  table += "<th>Completed Quantity</th>";
  for (const m of costs.machineQuantities.keys()) {
    table += "<th>" + m + " Cost %</th>";
  }
  table += "<th>Labor Cost %</th>";
  table += "<th>Automation Cost %</th>";
  table += "</tr></thead>\n<tbody>\n";

  const rows = LazySeq.of(costs.parts).sortBy((c) => c.part);
  const pctFormat = new Intl.NumberFormat(undefined, {
    style: "percent",
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  });

  for (const c of rows) {
    table += "<tr><td>" + c.part + "</td>";
    table += "<td>" + c.parts_completed.toString() + "</td>";
    for (const m of costs.machineQuantities.keys()) {
      table += "<td>" + pctFormat.format(c.machine.get(m) ?? 0) + "</td>";
    }
    table += "<td>" + pctFormat.format(c.labor) + "</td>";
    table += "<td>" + pctFormat.format(c.automation) + "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyCostBreakdownToClipboard(costs: CostData): void {
  copy(buildCostBreakdownTable(costs));
}
