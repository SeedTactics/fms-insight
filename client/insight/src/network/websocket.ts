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

import { LoadHistoricDataSimDayUsage, ServerEvent } from "./api.js";
import { JobsBackend, LogBackend } from "./backend.js";
import { fmsInformation } from "./server-settings.js";
import { useCallback, useEffect, useRef } from "react";
import {
  lastEventCounter,
  onLoadCurrentSt,
  onLoadLast30Jobs,
  onLoadLast30Log,
  onServerEvent,
} from "../cell-status/loading.js";
import { addDays } from "date-fns";
import { last30SchIds } from "../cell-status/scheduled-jobs.js";
import { HashSet } from "@seedtactics/immutable-collections";
import { Atom, Setter, atom, useAtomValue, useSetAtom } from "jotai";

const websocketReconnectingAtom = atom<boolean>(false);
export const websocketReconnecting: Atom<boolean> = websocketReconnectingAtom;

const errorLoadingLast30RW = atom<string | null>(null);
export const errorLoadingLast30: Atom<string | null> = errorLoadingLast30RW;

function loadInitial(set: Setter): void {
  const now = new Date();
  const thirtyDaysAgo = addDays(now, -30);

  const curStProm = JobsBackend.currentStatus().then((st) => set(onLoadCurrentSt, st));
  const jobsProm = JobsBackend.history(
    thirtyDaysAgo,
    now,
    LoadHistoricDataSimDayUsage.LoadOnlyMostRecent
  ).then((j) => set(onLoadLast30Jobs, j));
  const logProm = LogBackend.get(thirtyDaysAgo, now).then((log) => set(onLoadLast30Log, log));

  Promise.all([curStProm, jobsProm, logProm])
    .catch((e: Record<string, string | undefined>) => set(errorLoadingLast30RW, e.message ?? e.toString()))
    .finally(() => set(websocketReconnectingAtom, false));
}

function loadMissed(lastCntr: number, schIds: HashSet<string> | undefined, set: Setter): void {
  const now = new Date();
  const curStProm = JobsBackend.currentStatus().then((st) => set(onLoadCurrentSt, st));
  const jobsProm = JobsBackend.filteredHistory(
    now,
    addDays(now, -30),
    LoadHistoricDataSimDayUsage.LoadOnlyMostRecent,
    schIds ? Array.from(schIds) : []
  ).then((j) => set(onLoadLast30Jobs, j));
  const logProm = LogBackend.recent(lastCntr, undefined).then((log) => set(onLoadLast30Log, log));

  Promise.all([curStProm, jobsProm, logProm])
    .catch((e: Record<string, string | undefined>) => set(errorLoadingLast30RW, e.message ?? e.toString()))
    .finally(() => set(websocketReconnectingAtom, false));
}

class ReconnectingWebsocket {
  public onopen?: () => void;
  public onmessage?: (evt: MessageEvent<string>) => void;
  public onreconnecting?: () => void;

  private readonly url: string;
  private ws?: WebSocket;
  private userCalledClose = false;
  private reconnectAttempts = 0;

  public constructor(url: string) {
    this.url = url;
    this.connect();
  }

  public close() {
    this.userCalledClose = true;
    this.ws?.close();
  }

  private connect() {
    if (this.userCalledClose) return;

    this.ws = new WebSocket(this.url);
    const localWs = this.ws;

    const connectTimeout = setTimeout(() => {
      localWs.close();
    }, 2000);

    localWs.onopen = () => {
      clearTimeout(connectTimeout);
      this.reconnectAttempts = 0;
      this.onopen?.();
    };

    localWs.onclose = () => {
      clearTimeout(connectTimeout);
      this.ws = undefined;
      if (this.userCalledClose) {
        return;
      }

      this.onreconnecting?.();
      const delay = Math.min(1000 * Math.pow(1.5, this.reconnectAttempts), 30000);
      setTimeout(() => {
        this.reconnectAttempts++;
        this.connect();
      }, delay);
    };

    localWs.onmessage = (evt: MessageEvent<string>) => {
      this.onmessage?.(evt);
    };

    localWs.onerror = (evt) => {
      console.error(evt);
    };
  }
}

const onOpenAtom = atom(null, (get, set) => {
  const lastSeenCntr = get(lastEventCounter);
  const schIds = get(last30SchIds);
  set(websocketReconnectingAtom, true);
  set(errorLoadingLast30RW, null);
  if (lastSeenCntr !== null && lastSeenCntr !== undefined) {
    loadMissed(lastSeenCntr, schIds, set);
  } else {
    loadInitial(set);
  }
});

const onMessageAtom = atom(null, (_, set, evt: MessageEvent<string>) => {
  const serverEvt = ServerEvent.fromJS(JSON.parse(evt.data));
  set(onServerEvent, { evt: serverEvt, now: new Date(), expire: true });
});

export function WebsocketConnection(): null {
  const onOpen = useSetAtom(onOpenAtom);
  const setReconnecting = useSetAtom(websocketReconnectingAtom);
  const onReconnecting = useCallback(() => setReconnecting(true), [setReconnecting]);
  const onMessage = useSetAtom(onMessageAtom);
  const fmsInfoLoadable = useAtomValue(fmsInformation);
  const websocketRef = useRef<ReconnectingWebsocket | null>(null);

  useEffect(() => {
    if (websocketRef.current) return;

    const user = fmsInfoLoadable.user ?? null;

    const loc = window.location;
    let uri: string;
    if (loc.protocol === "backup:") {
      // viewing page in backup viewer, no websocket connection
      return;
    } else if (loc.protocol === "https:") {
      uri = "wss:";
    } else {
      uri = "ws:";
    }
    uri += "//" + loc.host + "/api/v1/events";

    if (user) {
      uri += "?token=" + encodeURIComponent(user.access_token);
    }

    const websocket = new ReconnectingWebsocket(uri);
    websocket.onopen = onOpen;
    websocket.onreconnecting = onReconnecting;
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
