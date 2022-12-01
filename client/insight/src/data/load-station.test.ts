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

import curSt from "../../test/status-mock.json";
import { CurrentStatus } from "../network/api.js";
import { selectLoadStationAndQueueProps } from "./load-station.js";
import { describe, it, expect } from "vitest";
import { toRawJs } from "../../test/to-raw-js.js";

describe("load station status", () => {
  it("load 1 with no queues", () => {
    const status = CurrentStatus.fromJS(curSt);
    expect(toRawJs(selectLoadStationAndQueueProps(1, [], status))).toMatchSnapshot("load 1 with no queues");
  });

  it("load 2 with queue", () => {
    const status = CurrentStatus.fromJS(curSt);
    expect(toRawJs(selectLoadStationAndQueueProps(2, ["Queue1"], status))).toMatchSnapshot(
      "load 2 with queue"
    );
  });

  it("load 3 with empty pallet", () => {
    const status = CurrentStatus.fromJS(curSt);
    expect(toRawJs(selectLoadStationAndQueueProps(3, ["Queue2"], status))).toMatchSnapshot(
      "load 3 with empty pallet"
    );
  });
});
