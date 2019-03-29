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

import * as api from "./api";
import { Pledge, PledgeStatus, PledgeToPromise } from "../store/middleware";
import { LogBackend, OtherLogBackends } from "./backend";
import { HashMap } from "prelude-ts";
import { InspectionLogEntry } from "./events.inspection";
import * as insp from "./events.inspection";
import { addDays } from "date-fns";

export enum ActionType {
  SearchDateRange = "PathLookup_Search",
  LoadLogFromOtherServer = "PathLookup_LoadFromOtherServer",
  Clear = "PathLookup_Clear"
}

export type Action =
  | {
      type: ActionType.Clear;
    }
  | {
      type: ActionType.SearchDateRange;
      part: string;
      initialLoad: boolean;
      curStart: Date;
      curEnd: Date;
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>;
    }
  | {
      type: ActionType.LoadLogFromOtherServer;
      part: string;
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>;
    };

export function searchForPaths(part: string, start: Date, end: Date): ReadonlyArray<PledgeToPromise<Action>> {
  const mainLoad = {
    type: ActionType.SearchDateRange,
    part,
    initialLoad: true,
    curStart: start,
    curEnd: end,
    pledge: LogBackend.get(start, end)
  } as PledgeToPromise<Action>;

  const extra = OtherLogBackends.map(
    b =>
      ({
        type: ActionType.LoadLogFromOtherServer,
        part,
        pledge: b.get(start, end)
      } as PledgeToPromise<Action>)
  );
  return [mainLoad].concat(extra);
}

export function extendRange(
  part: string,
  oldStart: Date,
  oldEnd: Date,
  numDays: number
): ReadonlyArray<PledgeToPromise<Action>> {
  let start: Date, end: Date;
  let newStart: Date | undefined, newEnd: Date | undefined;
  if (numDays < 0) {
    start = addDays(oldStart, numDays);
    end = oldStart;
    newStart = start;
  } else {
    start = oldEnd;
    end = addDays(oldEnd, numDays);
    newEnd = end;
  }
  const mainLoad = {
    type: ActionType.SearchDateRange,
    part,
    initialLoad: false,
    curStart: newStart,
    curEnd: newEnd,
    pledge: LogBackend.get(start, end)
  } as PledgeToPromise<Action>;

  const extra = OtherLogBackends.map(
    b =>
      ({
        type: ActionType.LoadLogFromOtherServer,
        part,
        pledge: b.get(start, end)
      } as PledgeToPromise<Action>)
  );
  return [mainLoad].concat(extra);
}

export interface State {
  readonly entries: HashMap<insp.PartAndInspType, ReadonlyArray<InspectionLogEntry>> | undefined;
  readonly loading: boolean;
  readonly curStart?: Date;
  readonly curEnd?: Date;
  readonly load_error?: Error;
}

export const initial: State = {
  entries: undefined,
  loading: false
};

function processEvents(newEvts: ReadonlyArray<Readonly<api.ILogEntry>>, partToSearch: string, old: State): State {
  return {
    ...old,
    entries: insp.process_events({ type: insp.ExpireOldDataType.NoExpire }, newEvts, partToSearch, {
      by_part: old.entries || HashMap.empty()
    }).by_part
  };
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) {
    return initial;
  }
  switch (a.type) {
    case ActionType.SearchDateRange:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {
            entries: a.initialLoad ? HashMap.empty() : s.entries,
            loading: true,
            curStart: a.curStart || s.curStart,
            curEnd: a.curEnd || s.curEnd
          };
        case PledgeStatus.Error:
          return { entries: HashMap.empty(), loading: false, load_error: a.pledge.error };
        case PledgeStatus.Completed:
          return {
            ...processEvents(a.pledge.result, a.part, s),
            loading: false
          };
        default:
          return s;
      }
    case ActionType.LoadLogFromOtherServer:
      if (a.pledge.status === PledgeStatus.Completed) {
        if (s.entries) {
          return processEvents(a.pledge.result, a.part, s);
        } else {
          return s; // happens if the data is cleared before the response arrives
        }
      } else {
        return s; // ignore starting and errors for other servers
      }

    case ActionType.Clear:
      return initial;

    default:
      return s;
  }
}
