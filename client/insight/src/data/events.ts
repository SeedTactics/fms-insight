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
import { addDays } from "date-fns";
import { PledgeStatus, Pledge, ActionBeforeMiddleware } from "../store/middleware";
import { Vector } from "prelude-ts";
import { LazySeq } from "../util/lazyseq";

import * as api from "../network/api";
import * as cycles from "./events.cycles";
import * as matsummary from "./events.matsummary";
import * as inspection from "./events.inspection";
import * as buffering from "./events.buffering";
import { LogBackend } from "../network/backend";

export enum AnalysisPeriod {
  Last30Days = "Last_30_Days",
  SpecificMonth = "Specific_Month",
}

export interface Last30Days {
  readonly thirty_days_ago: Date;
  readonly latest_log_counter: number | undefined;
  readonly most_recent_10_events: Vector<Readonly<api.ILogEntry>>;

  readonly cycles: cycles.CycleState;
  readonly inspection: inspection.InspectionState;
  readonly buffering: buffering.BufferingState;
  readonly mat_summary: matsummary.MatSummaryState; // matSummary should be global, not 30 days or specific month
}

export interface AnalysisMonth {
  readonly cycles: cycles.CycleState;
  readonly inspection: inspection.InspectionState;
  readonly buffering: buffering.BufferingState;
  readonly mat_summary: matsummary.MatSummaryState;
}

const emptyAnalysisMonth: AnalysisMonth = {
  cycles: cycles.initial,
  inspection: inspection.initial,
  buffering: buffering.initial,
  mat_summary: matsummary.initial,
};

export interface State {
  readonly loading_log_entries: boolean;
  readonly loading_error?: Error;

  readonly loading_analysis_month_log: boolean;

  readonly last30: Last30Days;
  readonly selected_month: AnalysisMonth;
}

export const initial: State = {
  loading_log_entries: false,
  //analysis_period_month: startOfMonth(new Date()),
  loading_analysis_month_log: false,

  last30: {
    thirty_days_ago: addDays(new Date(), -30),
    latest_log_counter: undefined,
    most_recent_10_events: Vector.empty(),
    cycles: cycles.initial,
    mat_summary: matsummary.initial,
    inspection: inspection.initial,
    buffering: buffering.initial,
  },

  selected_month: emptyAnalysisMonth,
};

export enum ActionType {
  LoadRecentLogEntries = "Events_LoadRecentLogEntries",
  LoadSpecificMonthLogEntries = "Events_LoadSpecificMonthLogEntries",
  ReceiveNewLogEntries = "Events_NewLogEntries",
  SwapMaterialOnPal = "Events_SwapMatOnPal",
}

export type Action =
  | {
      type: ActionType.LoadRecentLogEntries;
      now: Date;
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>;
    }
  | {
      type: ActionType.LoadSpecificMonthLogEntries;
      month: Date;
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>;
    }
  | {
      type: ActionType.ReceiveNewLogEntries;
      now: Date;
      events: ReadonlyArray<Readonly<api.ILogEntry>>;
    }
  | { type: ActionType.SwapMaterialOnPal; swap: Readonly<api.IEditMaterialInLogEvents> };

type ABF = ActionBeforeMiddleware<Action>;

export function refreshLogEntries(lastCounter: number): ABF {
  const now = new Date();
  return {
    type: ActionType.LoadRecentLogEntries,
    now: now,
    pledge: LogBackend.recent(lastCounter),
  };
}

export function receiveNewEvents(events: ReadonlyArray<Readonly<api.ILogEntry>>): ABF {
  return {
    type: ActionType.ReceiveNewLogEntries,
    now: new Date(),
    events,
  };
}

export function onEditMaterialOnPallet(swap: Readonly<api.IEditMaterialInLogEvents>): ABF {
  return { type: ActionType.SwapMaterialOnPal, swap };
}

function safeAssign<T extends R, R>(o: T, n: R): T {
  let allMatch = true;
  for (const k of Object.keys(n)) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-member-access
    if ((o as any)[k] !== (n as any)[k]) {
      allMatch = false;
      break;
    }
  }
  if (allMatch) {
    return o;
  } else {
    return Object.assign({}, o, n);
  }
}

function processRecentLogEntries(now: Date, evts: ReadonlyArray<Readonly<api.ILogEntry>>, s: Last30Days): Last30Days {
  const thirtyDaysAgo = addDays(now, -30);
  const initialLoad = s.latest_log_counter === undefined;
  let lastCounter = s.latest_log_counter;
  const lastNewEvent = LazySeq.ofIterable(evts).maxOn((e) => e.counter);
  let last10Evts = s.most_recent_10_events;
  if (lastNewEvent.isSome()) {
    if (lastCounter === undefined || lastCounter < lastNewEvent.get().counter) {
      lastCounter = lastNewEvent.get().counter;
    }
    const lastNew10 = evts.slice(-10);
    last10Evts = last10Evts.appendAll(lastNew10).reverse().take(10).reverse();
  }
  return safeAssign(s, {
    thirty_days_ago: evts.length === 0 ? s.thirty_days_ago : thirtyDaysAgo,
    latest_log_counter: lastCounter,
    cycles: cycles.process_events(
      { type: cycles.ExpireOldDataType.ExpireEarlierThan, d: thirtyDaysAgo },
      evts,
      initialLoad,
      s.cycles
    ),
    most_recent_10_events: last10Evts,
    mat_summary: matsummary.process_events(
      { type: cycles.ExpireOldDataType.ExpireEarlierThan, d: thirtyDaysAgo },
      evts,
      s.mat_summary
    ),
    inspection: inspection.process_events(
      { type: cycles.ExpireOldDataType.ExpireEarlierThan, d: thirtyDaysAgo },
      evts,
      undefined,
      s.inspection
    ),
    buffering: buffering.process_events(
      { type: cycles.ExpireOldDataType.ExpireEarlierThan, d: thirtyDaysAgo },
      evts,
      s.buffering
    ),
  });
}

function processSpecificMonthLogEntries(evts: ReadonlyArray<Readonly<api.ILogEntry>>, s: AnalysisMonth): AnalysisMonth {
  return safeAssign(s, {
    cycles: cycles.process_events(
      { type: cycles.ExpireOldDataType.NoExpire },
      evts,
      true, // initial load is true
      s.cycles
    ),
    inspection: inspection.process_events({ type: cycles.ExpireOldDataType.NoExpire }, evts, undefined, s.inspection),
    buffering: buffering.process_events({ type: cycles.ExpireOldDataType.NoExpire }, evts, s.buffering),
    mat_summary: matsummary.process_events({ type: cycles.ExpireOldDataType.NoExpire }, evts, s.mat_summary),
  });
}

export function reducer(s: State | undefined, a: Action): State {
  if (s === undefined) {
    return initial;
  }
  switch (a.type) {
    case ActionType.LoadRecentLogEntries:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return { ...s, loading_log_entries: true, loading_error: undefined };
        case PledgeStatus.Completed:
          return {
            ...s,
            last30: processRecentLogEntries(a.now, a.pledge.result, s.last30),
            loading_log_entries: false,
          };
        case PledgeStatus.Error:
          return {
            ...s,
            loading_log_entries: false,
            loading_error: a.pledge.error,
          };
        default:
          return s;
      }

    case ActionType.ReceiveNewLogEntries:
      return {
        ...s,
        last30: processRecentLogEntries(a.now, a.events, s.last30),
      };

    case ActionType.LoadSpecificMonthLogEntries:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {
            ...s,
            loading_error: undefined,
            loading_analysis_month_log: true,
            selected_month: emptyAnalysisMonth,
          };

        case PledgeStatus.Completed:
          return {
            ...s,
            loading_analysis_month_log: false,
            selected_month: processSpecificMonthLogEntries(a.pledge.result, s.selected_month),
          };

        case PledgeStatus.Error:
          return {
            ...s,
            loading_analysis_month_log: false,
            loading_error: a.pledge.error,
          };

        default:
          return s;
      }

    case ActionType.SwapMaterialOnPal:
      return {
        ...s,
        last30: {
          ...s.last30,
          inspection: inspection.process_swap(a.swap, s.last30.inspection),
          mat_summary: matsummary.process_swap(a.swap, s.last30.mat_summary),
          cycles: cycles.process_swap(a.swap, s.last30.cycles),
        },
      };

    default:
      return s;
  }
}
