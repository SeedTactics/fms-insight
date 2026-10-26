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

import { atom, type Getter } from "jotai";
import type { ICurrentStatus } from "./api.js";
import { currentStatus, setCurrentStatus } from "../cell-status/current-status.js";
import { replaceCustomState } from "../cell-status/custom-state.js";
import { setNamesFromCurrentStatus } from "../cell-status/names.js";

// One open socket owns a session. Complete snapshots share capture revisions across HTTP and
// websocket delivery; keep the original reference to distinguish later log and optimistic edits.
type CurrentStatusSession = {
  readonly id: number;
  readonly open: boolean;
  readonly snapshot: {
    readonly status: Readonly<ICurrentStatus>;
    readonly source: "http" | "socket";
  } | null;
};
export const currentStatusSessionAtom = atom<CurrentStatusSession>({
  id: 0,
  open: false,
  snapshot: null,
});

export const currentStatusIsLiveRW = atom(false);
export const errorLoadingStatusRW = atom<string | null>(null);

// A rejected action response needs an actual replacement read. A queued socket frame alone
// cannot establish that read. Identity keeps an older in-flight read from satisfying a later one.
export const requiredStatusReadRW = atom<Readonly<{ pending: true }> | null>(null);

export const currentStatusRequest = atom((get) => ({
  session: get(currentStatusSessionAtom).id,
  before: get(currentStatus),
}));

export type CurrentStatusRequest = Readonly<{
  session: number;
  before: Readonly<ICurrentStatus>;
}>;

// Successful HTTP responses share this rule, including snapshots returned by custom actions.
export const installCurrentStatusResponse = atom(
  null,
  (get, set, request: CurrentStatusRequest, loaded: Readonly<ICurrentStatus>): boolean => {
    const current = get(currentStatusSessionAtom);
    if (current.id !== request.session) return false;
    const installed = current.snapshot?.status;
    if (installed && loaded.revision <= installed.revision) return true;
    if (get(currentStatus) !== request.before && get(currentStatus) !== installed) return false;
    return set(acceptCurrentStatusSnapshot, request.session, loaded, "http");
  },
);

export function isCurrentSession(get: Getter, session: number): boolean {
  return get(currentStatusSessionAtom).id === session;
}

// Raw replacement also serves offline fixtures and local rollbacks. Live server snapshots must
// use acceptCurrentStatusSnapshot so the display and its comparison revision move together.
export const onLoadCurrentSt = atom(null, (_, set, status: Readonly<ICurrentStatus>) => {
  set(setCurrentStatus, status);
  set(replaceCustomState, status.customState ?? null);
  set(setNamesFromCurrentStatus, status);
});

export const acceptCurrentStatusSnapshot = atom(
  null,
  (
    get,
    set,
    session: number,
    status: Readonly<ICurrentStatus>,
    source: "http" | "socket",
  ): boolean => {
    const current = get(currentStatusSessionAtom);
    const installed = current.snapshot?.status;
    if (current.id !== session || (installed && status.revision <= installed.revision))
      return false;
    set(onLoadCurrentSt, status);
    set(currentStatusSessionAtom, { ...current, snapshot: { status, source } });
    if (!get(requiredStatusReadRW)) {
      if (current.open) set(currentStatusIsLiveRW, true);
      set(errorLoadingStatusRW, null);
    }
    return true;
  },
);
