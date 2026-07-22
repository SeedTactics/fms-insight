/* Copyright (c) 2026, John Lenz

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

import { createStore } from "jotai";
import { describe, expect, test } from "vitest";
import { customState } from "./custom-state.js";
import { onServerEvent } from "./loading.js";

const eventTime = {
  now: new Date("2026-07-22T12:00:00Z"),
  expire: true,
} as const;

describe("opaque custom state", () => {
  test("replaces each complete snapshot, including explicit null", () => {
    const store = createStore();
    const first = { schema: "custom", version: 1 };
    const second = ["replacement"];

    store.set(onServerEvent, { ...eventTime, evt: { customState: first } });
    expect(store.get(customState)).toBe(first);

    store.set(onServerEvent, { ...eventTime, evt: { customState: second } });
    expect(store.get(customState)).toBe(second);

    store.set(onServerEvent, { ...eventTime, evt: { customState: null } });
    expect(store.get(customState)).toBeNull();
  });

  test("an event without custom state leaves the snapshot unchanged", () => {
    const store = createStore();
    const snapshot = { schema: "custom", version: 1 };

    store.set(onServerEvent, { ...eventTime, evt: { customState: snapshot } });
    store.set(onServerEvent, { ...eventTime, evt: {} });

    expect(store.get(customState)).toBe(snapshot);
  });
});
