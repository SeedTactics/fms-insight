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

import { reducer, initial, ActionType } from "./path-lookup";
import { PledgeStatus } from "../store/middleware";
import { HashMap } from "prelude-ts";
import { PartAndInspType } from "./events.inspection";
import { loadMockData } from "../mock-data/load";
import { differenceInSeconds, addDays } from "date-fns";

it("has the initial state", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const s = reducer(undefined as any, undefined as any);
  expect(s).toBe(initial);
});

it("initially searches", () => {
  const s = reducer(initial, {
    type: ActionType.SearchDateRange,
    part: "aaa",
    initialLoad: true,
    curStart: new Date(Date.UTC(2018, 7, 6, 15, 33, 0)),
    curEnd: new Date(Date.UTC(2018, 7, 6, 16, 33, 0)),
    pledge: {
      status: PledgeStatus.Starting,
    },
  });

  expect(s.loading).toBe(true);
  expect(s.load_error).toBeUndefined();
  expect(s.curStart).toEqual(new Date(Date.UTC(2018, 7, 6, 15, 33, 0)));
  expect(s.curEnd).toEqual(new Date(Date.UTC(2018, 7, 6, 16, 33, 0)));
});

it("responds to errors", () => {
  const s = reducer(
    { ...initial, loading: true },
    {
      type: ActionType.SearchDateRange,
      part: "aaa",
      initialLoad: true,
      curStart: new Date(Date.UTC(2018, 7, 6, 15, 33, 0)),
      curEnd: new Date(Date.UTC(2018, 7, 6, 15, 33, 0)),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error("the error"),
      },
    }
  );

  expect(s.loading).toBe(false);
  expect(s.load_error).toEqual(new Error("the error"));
});

it("clears the data", () => {
  const s = reducer(
    { ...initial, entries: HashMap.of([new PartAndInspType("a", "b"), []]) },
    { type: ActionType.Clear }
  );
  expect(s.entries).toBeUndefined();
});

it("loads the data", async () => {
  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(addDays(new Date(Date.UTC(2018, 7, 6, 15, 39, 0)), -28), jan18);
  const data = loadMockData(offsetSeconds);
  const evts = await data.events;

  const s = reducer(initial, {
    type: ActionType.SearchDateRange,
    part: "aaa",
    initialLoad: true,
    curStart: new Date(Date.UTC(2018, 7, 6, 15, 33, 0)),
    curEnd: new Date(Date.UTC(2018, 7, 6, 15, 33, 0)),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts,
    },
  });

  expect(s.entries).toMatchSnapshot("computed events");
});

it("loads the from other log", async () => {
  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(addDays(new Date(Date.UTC(2018, 7, 6, 15, 39, 0)), -28), jan18);
  const data = loadMockData(offsetSeconds);
  const evts = await data.events;

  const s = reducer(initial, {
    type: ActionType.LoadLogFromOtherServer,
    part: "aaa",
    pledge: {
      status: PledgeStatus.Completed,
      result: evts,
    },
  });

  expect(s.entries).toMatchSnapshot("computed events");
});
