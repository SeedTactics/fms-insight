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

import { filterRemoveAddQueue } from "./LogEntry";
import { fakeCycle, fakeAddToQueue, fakeRemoveFromQueue } from "../../test/events.fake";

it("doesn't filter just a single add", () => {
  const cycles = [...fakeCycle({ time: new Date(), machineTime: 100 }), fakeAddToQueue()];
  expect(Array.from(filterRemoveAddQueue(cycles))).toEqual(cycles);
});

it("doesn't filter a single add and remove", () => {
  const cycles = [
    ...fakeCycle({ time: new Date(), machineTime: 100 }),
    fakeAddToQueue("q1"),
    fakeRemoveFromQueue("q1"),
  ];
  expect(Array.from(filterRemoveAddQueue(cycles))).toEqual(cycles);
});

it("filters out a single add and remove", () => {
  const regCycle = fakeCycle({ time: new Date(), machineTime: 100 });
  const a1 = fakeAddToQueue("q1");
  const r1 = fakeRemoveFromQueue("q1");
  const a2 = fakeAddToQueue("q1");
  const r2 = fakeRemoveFromQueue("q1");

  expect(Array.from(filterRemoveAddQueue([...regCycle, a1, r1, a2, r2]))).toEqual([...regCycle, a1, r2]);
});

it("doesn't filters when they are different queues", () => {
  const regCycle = fakeCycle({ time: new Date(), machineTime: 100 });
  const a1 = fakeAddToQueue("q1");
  const r1 = fakeRemoveFromQueue("q1");
  const a2 = fakeAddToQueue("q2");
  const r2 = fakeRemoveFromQueue("q2");

  expect(Array.from(filterRemoveAddQueue([...regCycle, a1, r1, a2, r2]))).toEqual([...regCycle, a1, r1, a2, r2]);
});
