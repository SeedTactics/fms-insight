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

import { atom, DefaultValue, selector } from "recoil";
import { LazySeq } from "./lazyseq";
import { fmsInformation } from "./server-settings";

const selectedOperator = atom<string | null>({
  key: "selected-operator",
  default: typeof localStorage !== "undefined" ? localStorage.getItem("current-operator") || null : null,
  effects_UNSTABLE: [
    ({ onSet }) => {
      onSet((newVal) => {
        if (newVal instanceof DefaultValue || newVal === null) {
          localStorage.removeItem("current-operator");
        } else {
          localStorage.setItem("current-operator", newVal);
        }
      });
    },
  ],
});

export const allOperators = atom<ReadonlySet<string>>({
  key: "all-operators",
  default: LazySeq.ofIterable<string>(
    JSON.parse(typeof localStorage !== "undefined" ? localStorage.getItem("operators") || "[]" : "[]")
  ).toRSet((x) => x),
  effects_UNSTABLE: [
    ({ onSet }) => {
      onSet((newVal) => {
        if (newVal instanceof DefaultValue) {
          localStorage.removeItem("operators");
        } else {
          localStorage.setItem("operators", JSON.stringify(Array.from(newVal)));
        }
      });
    },
  ],
});

export const currentOperator = selector<string | null>({
  key: "current-operator",
  get: ({ get }) => {
    const selected = get(selectedOperator);
    const fmsInfo = get(fmsInformation);
    return fmsInfo.user ? fmsInfo.user.profile.name || fmsInfo.user.profile.sub || null : selected;
  },
  set: ({ set }, newVal) => {
    set(selectedOperator, newVal);
  },
});
