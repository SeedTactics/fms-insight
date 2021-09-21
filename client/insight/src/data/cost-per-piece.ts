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

import { PartCycleData } from "./events.cycles";
import { addMonths, getDaysInMonth, addDays } from "date-fns";
import { Vector, HasEquals, HashMap } from "prelude-ts";
import { LazySeq } from "../util/lazyseq";
import { MaterialSummaryAndCompletedData } from "./events.matsummary";
import copy from "copy-to-clipboard";

export interface PartCost {
  readonly part: string;
  readonly parts_completed: number;

  // accumulation of machine costs over all cycles.  Cost be divided by parts_completed to get cost/piece
  readonly machine: { readonly cost: number; readonly pctPerStat: ReadonlyMap<string, number> };

  // accumulation of labor costs over all cycles.  Cost must be divided by parts_completed to get cost/piece
  readonly labor: { readonly cost: number; readonly percent: number };

  // percent split of yearly automation cost over all use
  readonly automation_pct: number;
}

export type MachineCostPerYear = { readonly [stationGroup: string]: number };

export interface CostData {
  readonly totalLaborCostForPeriod: number;
  readonly automationCostForPeriod: number;
  readonly stationCostForPeriod: ReadonlyMap<string, number>;
  readonly machineCostGroups: ReadonlyArray<string>;
  readonly parts: ReadonlyArray<PartCost>;
}

function isUnloadCycle(c: PartCycleData): boolean {
  return c.isLabor && c.operation === "UNLOAD";
}

function isMonthType(type: { month: Date } | { thirtyDaysAgo: Date }): type is { month: Date } {
  return Object.prototype.hasOwnProperty.call(type, "month");
}

export function compute_monthly_cost(
  machineCostPerYear: MachineCostPerYear,
  automationCostPerYear: number | null,
  totalLaborCostForPeriod: number,
  cycles: Vector<PartCycleData>,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  type: { month: Date } | { thirtyDaysAgo: Date }
): CostData {
  const days = isMonthType(type) ? getDaysInMonth(type.month) : 30;
  const start = isMonthType(type) ? type.month : type.thirtyDaysAgo;
  const end = isMonthType(type) ? addMonths(type.month, 1) : addDays(type.thirtyDaysAgo, 31);

  let totalPalletCycles = 0;
  const totalStatUseMinutes = new Map<string, number>();
  const stationCount = new Map<string, Set<number>>();

  for (const c of cycles) {
    if (c.x < start || c.x > end) continue;
    if (isUnloadCycle(c)) {
      totalPalletCycles += 1;
    }
    totalStatUseMinutes.set(c.stationGroup, c.activeMinutes + (totalStatUseMinutes.get(c.stationGroup) ?? 0));
    const s = stationCount.get(c.stationGroup);
    if (s) {
      s.add(c.stationNumber);
    } else {
      stationCount.set(c.stationGroup, new Set<number>([c.stationNumber]));
    }
  }

  const automationCostForPeriod = automationCostPerYear ? (automationCostPerYear * days) / 365 : 0;
  const stationCostForPeriod = LazySeq.ofIterable(stationCount).toRMap(
    ([statGroup, cnt]) => [statGroup, ((machineCostPerYear[statGroup] ?? 0) * cnt.size * days) / 365],
    (x, y) => x + y
  );

  const completed = LazySeq.ofIterable(matsById)
    .filter(([, details]) => {
      if (details.numProcesses === undefined) return false;
      const unload = details.unloaded_processes?.[details.numProcesses];
      return !!unload && unload >= start && unload <= end;
    })
    .toMap(
      ([, details]) => [details.partName, 1],
      (v1, v2) => v1 + v2
    );

  const parts = Array.from(
    cycles
      .filter((c) => c.x >= start && c.x <= end)
      .groupBy((c) => c.part)
      .map((partName, forPart) => [
        partName as string & HasEquals,
        {
          part: partName,
          parts_completed: completed.get(partName).getOrElse(0),
          machine: LazySeq.ofIterable(forPart)
            .filter((c) => !c.isLabor)
            .toMap(
              (c) => [c.stationGroup, c.activeMinutes],
              (a1, a2) => a1 + a2
            )
            .foldLeft(
              { cost: 0, pctPerStat: new Map<string, number>() },
              (x, [statGroup, minutes]: [string, number]) => {
                const totalUse = totalStatUseMinutes.get(statGroup) ?? 1;
                const totalMachineCost = stationCostForPeriod.get(statGroup) ?? 0;
                x.pctPerStat.set(statGroup, minutes / totalUse + (x.pctPerStat.get(statGroup) ?? 0));
                return { cost: x.cost + (minutes / totalUse) * totalMachineCost, pctPerStat: x.pctPerStat };
              }
            ),
          labor: LazySeq.ofIterable(forPart)
            .filter((c) => c.isLabor)
            .toMap(
              (c) => [c.stationGroup, c.activeMinutes],
              (a1, a2) => a1 + a2
            )
            .foldLeft({ cost: 0, percent: 0 }, (x, [statGroup, minutes]: [string, number]) => {
              const total = totalStatUseMinutes.get(statGroup) ?? 1;
              return {
                cost: x.cost + (minutes / total) * totalLaborCostForPeriod,
                percent: x.percent + minutes / total,
              };
            }),
          automation_pct: automationCostPerYear
            ? forPart.sumOn((v) => (isUnloadCycle(v) ? 1 : 0)) / totalPalletCycles
            : 0,
        },
      ])
      .valueIterable()
  );

  const machineCostGroups = Array.from(
    LazySeq.ofIterable(parts)
      .flatMap((p) => p.machine.pctPerStat.keys())
      .toRSet((s) => s)
  );
  machineCostGroups.sort((a, b) => a.localeCompare(b));

  return {
    parts,
    totalLaborCostForPeriod,
    automationCostForPeriod,
    stationCostForPeriod: stationCostForPeriod,
    machineCostGroups,
  };
}

export function buildCostPerPieceTable(costs: CostData): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Part</th>";
  table += "<th>Completed Quantity</th>";
  table += "<th>Machine Cost</th>";
  table += "<th>Labor Cost</th>";
  table += "<th>Automation Cost</th>";
  table += "<th>Total</th>";
  table += "</tr></thead>\n<tbody>\n";

  const rows = Vector.ofIterable(costs.parts).sortOn((c) => c.part);
  const format = Intl.NumberFormat(undefined, {
    maximumFractionDigits: 1,
  });

  for (const c of rows) {
    table += "<tr><td>" + c.part + "</td>";
    table += "<td>" + c.parts_completed.toString() + "</td>";
    table += "<td>" + (c.parts_completed > 0 ? format.format(c.machine.cost / c.parts_completed) : "0") + "</td>";
    table += "<td>" + (c.parts_completed > 0 ? format.format(c.labor.cost / c.parts_completed) : "0") + "</td>";
    table +=
      "<td>" +
      (c.parts_completed > 0
        ? format.format((c.automation_pct * costs.automationCostForPeriod) / c.parts_completed)
        : "0") +
      "</td>";
    table +=
      "<td>" +
      (c.parts_completed > 0
        ? format.format(
            c.machine.cost / c.parts_completed +
              c.labor.cost / c.parts_completed +
              (c.automation_pct * costs.automationCostForPeriod) / c.parts_completed
          )
        : "") +
      "</td>";
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
  for (const m of costs.machineCostGroups) {
    table += "<th>" + m + " Cost %</th>";
  }
  table += "<th>Labor Cost %</th>";
  table += "<th>Automation Cost %</th>";
  table += "</tr></thead>\n<tbody>\n";

  const rows = Vector.ofIterable(costs.parts).sortOn((c) => c.part);
  const pctFormat = new Intl.NumberFormat(undefined, {
    style: "percent",
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  });

  for (const c of rows) {
    table += "<tr><td>" + c.part + "</td>";
    table += "<td>" + c.parts_completed.toString() + "</td>";
    for (const m of costs.machineCostGroups) {
      table += "<td>" + pctFormat.format(c.machine.pctPerStat.get(m) ?? 0) + "</td>";
    }
    table += "<td>" + pctFormat.format(c.labor.percent) + "</td>";
    table += "<td>" + pctFormat.format(c.automation_pct) + "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyCostBreakdownToClipboard(costs: CostData): void {
  copy(buildCostBreakdownTable(costs));
}
