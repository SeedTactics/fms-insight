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

import ReconnectingWebSocket from "reconnecting-websocket";
import { ServerEvent } from "./api";
import { BackendHost, JobsBackend, LogBackend } from "./backend";
import { fmsInformation } from "./server-settings";
import { atom, RecoilState, RecoilValueReadOnly, useRecoilCallback, useRecoilValueLoadable } from "recoil";
import { useEffect, useRef } from "react";
import {
  lastEventCounter,
  onLoadCurrentSt,
  onLoadLast30Jobs,
  onLoadLast30Log,
  onServerEvent,
} from "../cell-status/loading";
import { addDays } from "date-fns";
import { RecoilConduit } from "../util/recoil-util";

const websocketReconnectingAtom = atom<boolean>({
  key: "websocket-reconnecting",
  default: false,
});
export const websocketReconnecting: RecoilValueReadOnly<boolean> = websocketReconnectingAtom;

const errorLoadingLast30RW = atom<string | null>({ key: "error-last30-data", default: null });
export const errorLoadingLast30: RecoilValueReadOnly<string | null> = errorLoadingLast30RW;

function loadInitial(
  set: <T>(s: RecoilState<T>, t: T) => void,
  push: <T>(c: RecoilConduit<T>) => (t: T) => void
): void {
  const now = new Date();
  const thirtyDaysAgo = addDays(now, -30);

  const curStProm = JobsBackend.currentStatus().then(push(onLoadCurrentSt));
  const jobsProm = JobsBackend.history(thirtyDaysAgo, now).then(push(onLoadLast30Jobs));
  const logProm = LogBackend.get(thirtyDaysAgo, now).then(push(onLoadLast30Log));

  Promise.all([curStProm, jobsProm, logProm])
    .catch((e: Record<string, string | undefined>) => set(errorLoadingLast30RW, e.message ?? e.toString()))
    .finally(() => set(websocketReconnectingAtom, false));
}

function loadMissed(
  lastCntr: number,
  set: <T>(s: RecoilState<T>, t: T) => void,
  push: <T>(c: RecoilConduit<T>) => (t: T) => void
): void {
  const curStProm = JobsBackend.currentStatus().then(push(onLoadCurrentSt));
  const logProm = LogBackend.recent(lastCntr).then(push(onLoadLast30Log));

  Promise.all([curStProm, logProm])
    .catch((e: Record<string, string | undefined>) => set(errorLoadingLast30RW, e.message ?? e.toString()))
    .finally(() => set(websocketReconnectingAtom, false));
}

export function WebsocketConnection(): null {
  const onOpen = useRecoilCallback(
    ({ set, snapshot, transact_UNSTABLE }) =>
      () => {
        function push<T>(c: RecoilConduit<T>): (t: T) => void {
          return (t) => transact_UNSTABLE((trans) => c.transform(trans, t));
        }
        const lastSeenCntr = snapshot.getLoadable(lastEventCounter).valueMaybe();
        set(websocketReconnectingAtom, true);
        set(errorLoadingLast30RW, null);
        if (lastSeenCntr !== null && lastSeenCntr !== undefined) {
          loadMissed(lastSeenCntr, set, push);
        } else {
          loadInitial(set, push);
        }
      },
    []
  );

  const onClose = useRecoilCallback(
    ({ set }) =>
      () => {
        set(websocketReconnectingAtom, true);
      },
    []
  );

  const onMessage = useRecoilCallback(
    ({ transact_UNSTABLE }) =>
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (evt: MessageEvent<any>) => {
        const serverEvt = ServerEvent.fromJS(JSON.parse(evt.data));
        transact_UNSTABLE((trans) => onServerEvent.transform(trans, { evt: serverEvt, now: new Date(), expire: true }));
      },
    []
  );

  const fmsInfoLoadable = useRecoilValueLoadable(fmsInformation);
  const websocketRef = useRef<ReconnectingWebSocket | null>(null);

  useEffect(() => {
    if (fmsInfoLoadable.state !== "hasValue") return;
    if (websocketRef.current) return;

    const user = fmsInfoLoadable.valueOrThrow().user ?? null;

    const loc = window.location;
    let uri: string;
    if (loc.protocol === "https:") {
      uri = "wss:";
    } else {
      uri = "ws:";
    }
    uri += "//" + (BackendHost || loc.host) + "/api/v1/events";

    if (user) {
      uri += "?token=" + encodeURIComponent(user.access_token || user.id_token);
    }

    const websocket = new ReconnectingWebSocket(uri);
    websocket.onopen = onOpen;
    websocket.onclose = onClose;
    websocket.onmessage = onMessage;
    websocketRef.current = websocket;

    return () => {
      if (websocketRef.current) {
        websocketRef.current.close();
        websocketRef.current = null;
      }
    };
  }, [fmsInfoLoadable]);

  return null;
}
