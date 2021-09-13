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

import { addDays } from "date-fns";

import { PledgeStatus } from "../store/middleware";
import * as events from "./events";
import { fakeCycle } from "./events.fake";
import { ILogEntry } from "./api";

it("creates initial state", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const s = events.reducer(undefined as any, undefined as any);
  expect(s).toBe(events.initial);
});

it("responds to loading", () => {
  const st = events.reducer(
    { ...events.initial, loading_error: new Error("hello") },
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting,
      },
    }
  );
  expect(st.loading_log_entries).toBe(true);
  expect(st.loading_error).toBeUndefined();
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("responds to error", () => {
  const st = events.reducer(
    { ...events.initial, loading_log_entries: true },
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error("hello"),
      },
    }
  );
  expect(st.loading_log_entries).toBe(false);
  expect(st.loading_error).toEqual(new Error("hello"));
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

function procNewEvents(evtsToAction: (now: Date, newEvts: ReadonlyArray<ILogEntry>) => events.Action) {
  const now = new Date(Date.UTC(2018, 1, 2, 9, 4, 5));

  // start with cycles from 27 days ago, 2 days ago, and today
  const todayCycle = fakeCycle(now, 30, "part111", 1, "palbb");
  const twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24, "part222", 1, "palbb");
  const twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18, "part222", 2, "palaa");
  let st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: now,
    pledge: {
      status: PledgeStatus.Completed,
      result: twentySevenCycle.concat(twoDaysAgoCycle, todayCycle),
    },
  });
  expect(st.last30).toMatchSnapshot("last30 with 27 days ago, 2 days ago, and today");

  // Now add again 6 days from now, so that the twoDaysAgo cycle is removed from hours (which processes only 7 days)
  // and twenty seven is removed from processing 30 days
  const sixDays = addDays(now, 6);
  const sixDaysCycle = fakeCycle(sixDays, 12, "part111", 1, "palcc");
  st = events.reducer(st, evtsToAction(sixDays, sixDaysCycle));

  // twentySevenDaysAgo should have been filtered out

  expect(st.last30).toMatchSnapshot("last30 with 2 days ago, today, and 6 days from now");

  // empty list should keep lists unchanged and the same object
  const newSt = events.reducer(st, {
    type: events.ActionType.LoadRecentLogEntries,
    now: now,
    pledge: {
      status: PledgeStatus.Completed,
      result: [],
    },
  });
  expect(newSt.last30).toBe(st.last30);
}

it("processes new log events into last30", () => {
  procNewEvents((now, evts) => ({
    type: events.ActionType.LoadRecentLogEntries,
    now,
    pledge: {
      status: PledgeStatus.Completed,
      result: evts,
    },
  }));
});

it("refreshes new events into last30", () => {
  procNewEvents((now, evts) => ({
    type: events.ActionType.ReceiveNewLogEntries,
    now,
    events: evts,
  }));
});
