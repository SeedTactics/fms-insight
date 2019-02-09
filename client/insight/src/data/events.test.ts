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

import { addDays, addHours, differenceInMinutes, addMinutes } from "date-fns";
import { duration } from "moment";

import { PledgeStatus } from "../store/middleware";
import * as events from "./events";
import * as stationCycles from "./events.cycles";
import * as simuse from "./events.simuse";
import * as inspection from "./events.inspection";
import { fakeCycle } from "./events.fake";
import { ILogEntry } from "./api";
import { LazySeq } from "./lazyseq";
import { buildPointsTable, buildCycleTable } from "./clipboard-table";

it("creates initial state", () => {
  // tslint:disable no-any
  let s = events.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(events.initial);
});

it("responds to loading", () => {
  let st = events.reducer(
    { ...events.initial, loading_error: new Error("hello") },
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_log_entries).toBe(true);
  expect(st.loading_job_history).toBe(false);
  expect(st.loading_error).toBeUndefined();
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("responds to loading for jobs", () => {
  let st = events.reducer(
    { ...events.initial, loading_error: new Error("hello") },
    {
      type: events.ActionType.LoadRecentJobHistory,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_log_entries).toBe(false);
  expect(st.loading_job_history).toBe(true);
  expect(st.loading_error).toBeUndefined();
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("responds to error", () => {
  let st = events.reducer(
    { ...events.initial, loading_log_entries: true },
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error("hello")
      }
    }
  );
  expect(st.loading_log_entries).toBe(false);
  expect(st.loading_job_history).toBe(false);
  expect(st.loading_error).toEqual(new Error("hello"));
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("responds to error for jobs", () => {
  let st = events.reducer(
    { ...events.initial, loading_job_history: true },
    {
      type: events.ActionType.LoadRecentJobHistory,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error("hello")
      }
    }
  );
  expect(st.loading_log_entries).toBe(false);
  expect(st.loading_job_history).toBe(false);
  expect(st.loading_error).toEqual(new Error("hello"));
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

function procNewEvents(evtsToAction: (now: Date, newEvts: ReadonlyArray<ILogEntry>) => events.Action) {
  var now = new Date(Date.UTC(2018, 1, 2, 9, 4, 5));

  // start with cycles from 27 days ago, 2 days ago, and today
  const todayCycle = fakeCycle(now, 30, "part111", 1, "palbb");
  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24, "part222", 1, "palbb");
  var twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18, "part222", 2, "palaa");
  let st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: now,
    pledge: {
      status: PledgeStatus.Completed,
      result: twentySevenCycle.concat(twoDaysAgoCycle, todayCycle)
    }
  });
  expect(st.last30).toMatchSnapshot("last30 with 27 days ago, 2 days ago, and today");

  // Now add again 6 days from now, so that the twoDaysAgo cycle is removed from hours (which processes only 7 days)
  // and twenty seven is removed from processing 30 days
  var sixDays = addDays(now, 6);
  const sixDaysCycle = fakeCycle(sixDays, 12, "part111", 1, "palcc");
  st = events.reducer(st, evtsToAction(sixDays, sixDaysCycle));

  // twentySevenDaysAgo should have been filtered out

  expect(st.last30).toMatchSnapshot("last30 with 2 days ago, today, and 6 days from now");

  // empty list should keep lists unchanged and the same object
  let newSt = events.reducer(st, {
    type: events.ActionType.LoadRecentLogEntries,
    now: sixDays,
    pledge: {
      status: PledgeStatus.Completed,
      result: []
    }
  });
  expect(newSt.last30).toBe(st.last30);
}

it("processes new log events into last30", () => {
  procNewEvents((now, evts) => ({
    type: events.ActionType.LoadRecentLogEntries,
    now,
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  }));
});

it("refreshes new events into last30", () => {
  procNewEvents((now, evts) => ({
    type: events.ActionType.ReceiveNewLogEntries,
    now,
    events: evts
  }));
});

it("starts loading a specific month for analysis", () => {
  let st = events.reducer(
    { ...events.initial },
    {
      type: events.ActionType.LoadSpecificMonthLogEntries,
      month: new Date(2018, 2, 1),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.SpecificMonth);
  expect(st.analysis_period_month).toEqual(new Date(2018, 2, 1));
  expect(st.loading_analysis_month_log).toBe(true);
  expect(st.loading_analysis_month_jobs).toBe(false);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("starts loading a specific month jobs for analysis", () => {
  let st = events.reducer(
    { ...events.initial },
    {
      type: events.ActionType.LoadSpecificMonthJobHistory,
      month: new Date(2018, 2, 1),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_analysis_month_log).toBe(false);
  expect(st.loading_analysis_month_jobs).toBe(true);
});

it("loads 30 days for analysis", () => {
  const cycles = stationCycles.process_events(
    { type: stationCycles.ExpireOldDataType.NoExpire },
    fakeCycle(new Date(), 3),
    true,
    stationCycles.initial
  );

  let st = events.reducer(
    {
      ...events.initial,
      analysis_period: events.AnalysisPeriod.SpecificMonth,
      selected_month: {
        cycles,
        sim_use: simuse.initial,
        inspection: inspection.initial
      }
    },
    {
      type: events.ActionType.SetAnalysisLast30Days
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.Last30Days);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("loads a specific month for analysis", () => {
  var now = new Date(Date.UTC(2018, 1, 2, 9, 4, 5));

  // start with cycles from 27 days ago, 2 days ago, and today
  const todayCycle = fakeCycle(now, 30, "partAAA", 1, "palss");
  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24, "partBBB", 2, "paltt");
  var twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18, "partCCC", 3, "paltt");

  let st = events.reducer(
    {
      ...events.initial,
      loading_analysis_month_log: true,
      analysis_period_month: new Date(2018, 1, 1),
      analysis_period: events.AnalysisPeriod.SpecificMonth
    },
    {
      type: events.ActionType.LoadSpecificMonthLogEntries,
      month: new Date(2018, 1, 1),
      pledge: {
        status: PledgeStatus.Completed,
        result: twentySevenCycle.concat(twoDaysAgoCycle, todayCycle)
      }
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.SpecificMonth);
  expect(st.loading_analysis_month_log).toBe(false);
  expect(st.analysis_period_month).toEqual(new Date(2018, 1, 1));
  expect(st.selected_month).toMatchSnapshot("selected month with 27 days ago, 2 days ago, and today");
});

it("bins actual cycles by day", () => {
  const now = new Date(2018, 2, 5); // midnight in local time
  const nowChicago = new Date(Date.UTC(2018, 2, 5, 6, 0, 0)); // America/Chicago time
  const minOffset = differenceInMinutes(nowChicago, now);

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addHours(now, -3), 20),
    fakeCycle(addHours(now, -15), 15)
  );
  const st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: addDays(now, 1),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  });

  let byDayAndStat = events.binCyclesByDayAndStat(st.last30.cycles.part_cycles, c => duration(c.active).asMinutes());

  // update day to be in Chicago timezone
  // This is because the snapshot formats the day as a UTC time in Chicago timezone
  // Note this is after cycles are binned, which is correct since cycles are generated using
  // now in local time and then binned in local time.  Just need to update the date before
  // comparing with the snapshot
  byDayAndStat = byDayAndStat.map((dayAndStat, val) => [dayAndStat.adjustDay(d => addMinutes(d, minOffset)), val]);

  expect(byDayAndStat).toMatchSnapshot("cycles binned by day and station");

  let byDayAndPart = events.binCyclesByDayAndPart(st.last30.cycles.part_cycles, c => (c.completed ? 1 : 0));

  byDayAndPart = byDayAndPart.map((dayAndPart, val) => [dayAndPart.adjustDay(d => addMinutes(d, minOffset)), val]);

  expect(byDayAndPart).toMatchSnapshot("cycles binned by day and part");
});

it("computes station oee", () => {
  const now = new Date(2018, 2, 5);

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addDays(now, -3), 20),
    fakeCycle(addDays(now, -15), 15)
  );
  const st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: addDays(now, 1),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  });

  let statMins = events.stationMinutes(st.last30.cycles.part_cycles, addDays(now, -7));

  expect(statMins).toMatchSnapshot("station minutes for last week");
});

it("creates points clipboard table", () => {
  // table formats columns in local time, so no need to convert to a specific timezone
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addHours(now, -3), 20),
    fakeCycle(addHours(now, -15), 15)
  );
  const st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: addDays(now, 1),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  });

  let byDayAndStat = events.binCyclesByDayAndStat(st.last30.cycles.part_cycles, c => duration(c.active).asMinutes());

  const points = LazySeq.ofIterable(byDayAndStat)
    .map(([dayAndStat, val]) => ({
      x: dayAndStat.day,
      y: dayAndStat.station,
      label: val.toString()
    }))
    .toArray();

  const table = document.createElement("div");
  table.innerHTML = buildPointsTable("Station", points);
  expect(table).toMatchSnapshot("clipboard table");
});

it("creates cycles clipboard table", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addHours(now, -3), 20),
    fakeCycle(addHours(now, -15), 15)
  );
  const st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: addDays(now, 1),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  });
  const data = stationCycles.filterStationCycles(st.last30.cycles.part_cycles, undefined, undefined, undefined);

  const table = document.createElement("div");
  table.innerHTML = buildCycleTable(data);
  expect(table).toMatchSnapshot("cycle clipboard table");
});

/*
These huge snapshots currently cause jest to hang in appveyor CI

it("loads events from mock data", async () => {
  const now = new Date(2018, 7, 5);
  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(addDays(now, -28), jan18);
  const data = loadMockData(offsetSeconds);
  const evts = await data.events;
  const st = events.reducer(
    events.initial,
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Completed,
        result: evts
      }
    });
  expect(st.last30).toMatchSnapshot("all mock data");
});

it("estimates the cycle times using MAD", async () => {
  const now = new Date(2018, 7, 5);
  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(addDays(now, -28), jan18);
  const data = loadMockData(offsetSeconds);
  const evts = await data.events;
  const st = events.reducer(
    events.initial,
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Completed,
        result: evts.map(e => ({...e, active: ""}))
      }
    });
  expect(st.last30).toMatchSnapshot("all mock data with estimated active time");
});
*/
