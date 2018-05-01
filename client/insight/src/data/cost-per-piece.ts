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

import * as im from 'immutable';
import { PartCycleData } from './events.cycles';
import { duration, Duration } from 'moment';

export interface CostInput {
  // key is machine group name
  readonly machineCostPerYear: {readonly [key: string]: number};
  readonly partMaterialCost: {readonly [key: string]: number};
  readonly numOperators: number;
  readonly operatorCostPerHour: number;
}

export enum ActionType {
  SetMachineCostPerYear = 'CostPerPiece_SetMachineCost',
  SetPartMaterialCost = 'CostPerPiece_SetPartCost',
  SetNumOperators = 'CostPerPiece_SetNumOpers',
  SetOperatorCostPerHour = 'CostPerPiece_SetOperatorCost',
}

export type Action =
  | {type: ActionType.SetMachineCostPerYear, group: string, cost: number}
  | {type: ActionType.SetPartMaterialCost, part: string, cost: number}
  | {type: ActionType.SetNumOperators, numOpers: number}
  | {type: ActionType.SetOperatorCostPerHour, cost: number}
  ;

export interface State {
  readonly input: CostInput;
}

export const initial: State = (function() {
  const json = localStorage.getItem("cost-per-piece");
  if (json) {
    return {
      input: JSON.parse(json),
    };
  } else {
    return {
      input: {
        machineCostPerYear: {},
        partMaterialCost: {},
        numOperators: 0,
        operatorCostPerHour: 0,
      }
    };
  }
})();

export function reducer(s: State, a: Action): State {
  if (s === undefined) { return initial; }
  let newSt = s;
  switch (a.type) {
    case ActionType.SetMachineCostPerYear:
      newSt = {...s, input: {...s.input, machineCostPerYear: {...s.input.machineCostPerYear,
        [a.group]: a.cost,
      }}};
      break;
    case ActionType.SetPartMaterialCost:
      newSt = {...s, input: {...s.input, partMaterialCost: {...s.input.partMaterialCost,
        [a.part]: a.cost,
      }}};
      break;
    case ActionType.SetNumOperators:
      newSt = {...s, input: {...s.input,
        numOperators: a.numOpers,
      }};
      break;
    case ActionType.SetOperatorCostPerHour:
      newSt = {...s, input: {...s.input,
        operatorCostPerHour: a.cost,
      }};
      break;
  }
  if (s !== newSt) {
    localStorage.setItem("cost-per-piece", JSON.stringify(newSt.input));
  }
  return newSt;
}

export interface PartCost {
  readonly part: string;
  readonly parts_completed: number;

  // sum of machine costs over all cycles.  Must be divided by parts_completed to get cost/piece
  readonly machine_cost: number;

  // sum of labor costs over all cycles.  Must be divided by parts_completed to get cost/piece
  readonly labor_cost: number;

  readonly material_cost: number;
}

function machine_cost(
  cycles: im.Seq<number, PartCycleData>,
  totalStatUseMinutes: im.Map<string, number>,
  machineCostPerYear: {readonly [key: string]: number},
  days: number,
): number {
  return cycles
    .groupBy(c => c.stationGroup)
    .map(forPartStat => forPartStat
      .reduce((x: Duration, c) => x.add(c.active, "minutes"), duration(0))
      .minutes()
    )
    .reduce(
      (x: number, minutes: number, statGroup: string) => {
        const totalUse = totalStatUseMinutes.get(statGroup) || 1;
        const totalMachineCost = (machineCostPerYear[statGroup] || 0) * days / 365;
        return x + minutes / totalUse * totalMachineCost;
      },
      0
    )
    ;
}

function labor_cost(
  cycles: im.Seq<number, PartCycleData>, totalStatUseMinutes: im.Map<string, number>, totalLaborCost: number
): number {
  const pctUse = cycles
    .groupBy(c => c.stationGroup)
    .map(forPartStat => forPartStat
      .reduce((x: Duration, c) => x.add(c.active, "minutes"), duration(0))
      .minutes()
    )
    .reduce(
      (x: number, minutes: number, statGroup: string) => {
        const total = totalStatUseMinutes.get(statGroup, 1);
        return x + minutes / total;
      },
      0
    )
    ;
  return pctUse * totalLaborCost;
}

export function compute_monthly_cost(
  i: CostInput, cycles: im.Seq.Indexed<PartCycleData>, numDays?: number
): ReadonlyArray<PartCost> {

  const days = numDays || 30;
  const totalLaborCost = days * 24 * i.operatorCostPerHour * i.numOperators;

  const totalStatUseMinutes: im.Map<string, number> =
    cycles
    .groupBy(c => c.stationGroup)
    .map(cs => cs
      .reduce((x: Duration, c) => x.add(c.active, "minutes"), duration(0))
      .minutes()
    )
    .toMap();

  return cycles
    .groupBy(c => c.part)
    .map((forPart, partName) => ({
      part: partName,
      parts_completed: forPart.toSeq().filter(c => c.completed).count(),
      machine_cost:
        machine_cost(forPart.toSeq().filter(c => !c.isLabor), totalStatUseMinutes, i.machineCostPerYear, days),
      labor_cost: labor_cost(forPart.toSeq().filter(c => c.isLabor), totalStatUseMinutes, totalLaborCost),
      material_cost: i.partMaterialCost[partName] || 0,
    }))
    .valueSeq()
    .toArray()
    ;
}