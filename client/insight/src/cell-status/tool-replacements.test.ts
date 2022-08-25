/* Copyright (c) 2021, John Lenz

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

import { snapshot_UNSTABLE } from "recoil";
import { toRawJs } from "../../test/to-raw-js.js";
import { applyConduitToSnapshot } from "../util/recoil-util.js";
import { onLoadLast30Log, onServerEvent } from "./loading.js";
import { last30ToolReplacements } from "./tool-replacements.js";
import { it, expect } from "vitest";
import {
  fakeMachineCycle,
  fakeNormalToolUsage,
  fakeToolChangeBeforeCycle,
  fakeToolChangeDuringCycle,
} from "../../test/events.fake.js";
import { addMinutes, addMonths } from "date-fns";
import { LogEntry } from "../network/api.js";

const machCycle = {
  part: "abc",
  proc: 1,
  program: "prog",
  elapsedMin: 1,
  activeMin: 1,
};

it("calculates last 30 tool replacements", () => {
  const start = new Date(Date.UTC(2022, 8, 25, 3, 4, 5));
  const evts = [
    // start with normal usages on machines 4 and 6 with the same tools
    ...fakeMachineCycle({
      ...machCycle,
      counter: 1,
      time: start,
      mcNum: 4,
      tooluse: [
        fakeNormalToolUsage({ tool: "aaa", pocket: 1, minsAtEnd: 40 }),
        fakeNormalToolUsage({ tool: "bbb", pocket: 2, minsAtEnd: 50 }),
        fakeNormalToolUsage({ tool: "ccc", pocket: 3, minsAtEnd: 60 }),
        fakeNormalToolUsage({ tool: "aaa", pocket: 4, minsAtEnd: 70 }),
      ],
    }),
    ...fakeMachineCycle({
      ...machCycle,
      counter: 2,
      time: addMinutes(start, 10),
      mcNum: 6,
      tooluse: [
        fakeNormalToolUsage({ tool: "aaa", pocket: 1, minsAtEnd: 140 }),
        fakeNormalToolUsage({ tool: "bbb", pocket: 2, minsAtEnd: 150 }),
        fakeNormalToolUsage({ tool: "ccc", pocket: 3, minsAtEnd: 160 }),
        fakeNormalToolUsage({ tool: "aaa", pocket: 4, minsAtEnd: 170 }),
      ],
    }),

    // now a couple changes on machine 4
    ...fakeMachineCycle({
      ...machCycle,
      counter: 3,
      time: addMinutes(start, 20),
      mcNum: 4,
      tooluse: [
        fakeToolChangeBeforeCycle({ tool: "aaa", pocket: 1, minsAtEnd: 22 }),
        fakeToolChangeDuringCycle({ tool: "bbb", pocket: 2, use: 16, life: 50, minsAtEnd: 5 }),
        fakeNormalToolUsage({ tool: "ccc", pocket: 3, minsAtEnd: 66 }),
        fakeNormalToolUsage({ tool: "aaa", pocket: 4, minsAtEnd: 77 }),
      ],
    }),
  ];

  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  let tools = snapshot.getLoadable(last30ToolReplacements).valueOrThrow();
  expect(toRawJs(tools)).toMatchSnapshot("tools");

  // now changes on machine 6
  const machEndMc6 = fakeMachineCycle({
    ...machCycle,
    counter: 4,
    time: addMinutes(start, 30),
    mcNum: 6,
    tooluse: [
      fakeToolChangeBeforeCycle({ tool: "aaa", pocket: 1, minsAtEnd: 32 }),
      fakeToolChangeBeforeCycle({ tool: "bbb", pocket: 2, minsAtEnd: 52 }),
      fakeToolChangeDuringCycle({ tool: "ccc", pocket: 3, use: 10, life: 50, minsAtEnd: 7 }),
      fakeNormalToolUsage({ tool: "aaa", pocket: 4, minsAtEnd: 70 }),
    ],
  })[1];

  const afterAdd = applyConduitToSnapshot(snapshot, onServerEvent, {
    now: addMinutes(start, 30),
    evt: { logEntry: new LogEntry(machEndMc6) },
    expire: true,
  });

  tools = afterAdd.getLoadable(last30ToolReplacements).valueOrThrow();
  expect(toRawJs(tools)).toMatchSnapshot("after machine 6 changes");

  // now another change on machine 4, far in the future so it should filter
  const machEndMc4 = fakeMachineCycle({
    ...machCycle,
    counter: 5,
    time: addMonths(start, 2),
    mcNum: 4,
    tooluse: [
      fakeNormalToolUsage({ tool: "aaa", pocket: 1, minsAtEnd: 44 }),
      fakeToolChangeBeforeCycle({ tool: "bbb", pocket: 2, minsAtEnd: 15 }),
      fakeNormalToolUsage({ tool: "ccc", pocket: 3, minsAtEnd: 66 }),
      fakeToolChangeBeforeCycle({ tool: "aaa", pocket: 4, minsAtEnd: 16 }),
    ],
  })[1];

  const afterAdd2 = applyConduitToSnapshot(afterAdd, onServerEvent, {
    now: addMonths(start, 2),
    evt: { logEntry: new LogEntry(machEndMc4) },
    expire: true,
  });
  tools = afterAdd2.getLoadable(last30ToolReplacements).valueOrThrow();
  expect(toRawJs(tools)).toMatchSnapshot("after machine 4 changes");
});
