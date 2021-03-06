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

/* eslint-disable @typescript-eslint/no-unsafe-assignment */
import * as React from "react";
import { initStore } from "../src/store/store";
import { differenceInSeconds } from "date-fns";
import { registerMockBackend } from "../src/data/backend-mock";
import * as events from "../src/data/events";

import evtsJson from "./log-events.json";
import newJobs from "./newjobs.json";
import tools from "./tools.json";
import programs from "./programs.json";
import statusJson from "./status-mock.json";
import toolUse from "./tool-use.json";

export function mockComponent(name: string): (props: { [key: string]: object }) => JSX.Element {
  return function MockedComponent(props) {
    return (
      <div data-testid={"mock-component-" + name}>
        {Object.getOwnPropertyNames(props)
          .sort()
          .map((p, idx) => (
            <span key={idx} data-prop={p}>
              {JSON.stringify(props[p], null, 2)}
            </span>
          ))}
      </div>
    );
  };
}

export async function createTestStore() {
  // offset is such that all events fall within July no matter the timezone, so
  // selecting July 2018 as the month loads the same set of data
  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(new Date(Date.UTC(2018, 6, 2, 4, 10, 0)), jan18);

  const store = initStore();
  registerMockBackend(
    offsetSeconds,
    Promise.resolve({
      curSt: statusJson,
      jobs: newJobs,
      tools: tools,
      programs: programs,
      toolUse: toolUse,
    }),
    Promise.resolve(evtsJson as ReadonlyArray<object>)
  );
  store.dispatch(events.loadLast30Days());

  return store;
}
