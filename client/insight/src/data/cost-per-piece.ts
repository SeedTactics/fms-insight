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
import { getDaysInMonth } from "date-fns";
import { HashMap, Vector, HasEquals } from "prelude-ts";
import { LazySeq } from "./lazyseq";
// eslint-disable-next-line @typescript-eslint/no-var-requires
const copy = require("copy-to-clipboard");

export interface PartCost {
  readonly part: string;
  readonly parts_completed: number;

  // sum of machine costs over all cycles.  Must be divided by parts_completed to get cost/piece
  readonly machine_cost: number;

  // sum of labor costs over all cycles.  Must be divided by parts_completed to get cost/piece
  readonly labor_cost: number;

  // split of yearly automation cost over all use
  readonly automation_cost: number;

  readonly material_cost: number;
}

export type MachineCostPerYear = { readonly [stationGroup: string]: number };
export type PartMaterialCost = { readonly [partName: string]: number };

export function compute_monthly_cost(
  machineCostPerYear: MachineCostPerYear,
  partMaterialCost: PartMaterialCost,
  automationCostPerYear: number | null,
  totalLaborCostForPeriod: number,
  cycles: Vector<PartCycleData>,
  month: Date | null
): ReadonlyArray<PartCost> {
  const days = month ? getDaysInMonth(month) : 30;

  const totalStatUseMinutes: HashMap<string, number> = LazySeq.ofIterable(cycles).toMap(
    (c) => [c.stationGroup, c.activeMinutes],
    (a1, a2) => a1 + a2
  );

  const totalPalletCycles = LazySeq.ofIterable(cycles).sumOn((v) => (v.completed ? 1 : 0));

  const autoCostForPeriod = automationCostPerYear ? (automationCostPerYear * days) / 365 : 0;

  return Array.from(
    LazySeq.ofIterable(cycles)
      .groupBy((c) => c.part)
      .map((partName, forPart) => [
        partName as string & HasEquals,
        {
          part: partName,
          parts_completed: LazySeq.ofIterable(forPart)
            .filter((c) => c.completed)
            .sumOn((c) => c.material.length),
          machine_cost: LazySeq.ofIterable(forPart)
            .filter((c) => !c.isLabor)
            .toMap(
              (c) => [c.stationGroup, c.activeMinutes],
              (a1, a2) => a1 + a2
            )
            .foldLeft(0, (x: number, [statGroup, minutes]: [string, number]) => {
              const totalUse = totalStatUseMinutes.get(statGroup).getOrElse(1);
              const totalMachineCost = ((machineCostPerYear[statGroup] || 0) * days) / 365;
              return x + (minutes / totalUse) * totalMachineCost;
            }),
          labor_cost: LazySeq.ofIterable(forPart)
            .filter((c) => c.isLabor)
            .toMap(
              (c) => [c.stationGroup, c.activeMinutes],
              (a1, a2) => a1 + a2
            )
            .foldLeft(0, (x: number, [statGroup, minutes]: [string, number]) => {
              const total = totalStatUseMinutes.get(statGroup).getOrElse(1);
              return x + (minutes / total) * totalLaborCostForPeriod;
            }),
          automation_cost: automationCostPerYear
            ? (cycles.sumOn((v) => (v.completed ? 1 : 0)) / totalPalletCycles) * autoCostForPeriod
            : 0,
          material_cost: partMaterialCost[partName] || 0,
        },
      ])
      .valueIterable()
  );
}

export function buildCostPerPieceTable(costs: ReadonlyArray<PartCost>, partMatCost: PartMaterialCost) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Part</th>";
  table += "<th>Material Cost</th>";
  table += "<th>Machine Cost</th>";
  table += "<th>Labor Cost</th>";
  table += "<th>Automation Cost</th>";
  table += "<th>Total</th>";
  table += "</tr></thead>\n<tbody>\n";

  const rows = Vector.ofIterable(costs).sortOn((c) => c.part);
  const format = Intl.NumberFormat(undefined, {
    maximumFractionDigits: 1,
  });

  for (const c of rows) {
    table += "<tr><td>" + c.part + "</td>";
    table += "<td>" + (partMatCost[c.part] ?? "") + "</td>";
    table += "<td>" + (c.parts_completed > 0 ? format.format(c.machine_cost / c.parts_completed) : 0) + "</td>";
    table += "<td>" + (c.parts_completed > 0 ? format.format(c.labor_cost / c.parts_completed) : 0) + "</td>";
    table += "<td>" + (c.parts_completed > 0 ? format.format(c.automation_cost / c.parts_completed) : 0) + "</td>";
    table +=
      "<td>" +
      (c.parts_completed > 0
        ? format.format(
            (partMatCost[c.part] || 0) +
              c.machine_cost / c.parts_completed +
              c.labor_cost / c.parts_completed +
              c.automation_cost / c.parts_completed
          )
        : format.format(partMatCost[c.part] || 0)) +
      "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyCostPerPieceToClipboard(costs: ReadonlyArray<PartCost>, partMatCost: PartMaterialCost): void {
  copy(buildCostPerPieceTable(costs, partMatCost));
}
