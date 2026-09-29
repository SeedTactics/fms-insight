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

import { ServerEvent } from "./api.js";
import { JobsBackend, LogBackend } from "./backend.js";
import { fmsInformation } from "./server-settings.js";
import { useEffect, useRef } from "react";
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
import { Atom, Getter, Setter, atom, useAtomValue, useSetAtom } from "jotai";

const websocketReconnectingAtom = atom<boolean>(false);
export const websocketReconnecting: Atom<boolean> = websocketReconnectingAtom;

// True while the websocket is open and this connection has loaded or received a current status, so
// the retained current status reflects the server. Loading the historical jobs and log does not
// affect it.
const currentStatusIsLiveRW = atom<boolean>(false);
export const currentStatusIsLive: Atom<boolean> = currentStatusIsLiveRW;

// A session is one open socket; it ends when that socket closes or is disposed, so late results
// from an earlier connection are ignored. statusFromSocket records that the socket has delivered a
// current status, which is newer than the session's bootstrap current-status request.
type WebsocketSession = { readonly id: number; readonly statusFromSocket: boolean };
const websocketSessionAtom = atom<WebsocketSession>({ id: 0, statusFromSocket: false });

// The event counter from which the log history still has to be caught up. Live events advance
// lastEventCounter while a catch-up is pending, so an interrupted catch-up resumes from here
// rather than leaving a gap; it is cleared once a catch-up is applied.
const historyCatchUpFromAtom = atom<number | null>(null);

const errorLoadingLast30RW = atom<string | null>(null);
export const errorLoadingLast30: Atom<string | null> = errorLoadingLast30RW;

function isCurrentSession(get: Getter, session: number): boolean {
  return get(websocketSessionAtom).id === session;
}

function loadCurrentStatus(get: Getter, set: Setter, session: number): Promise<void> {
  return JobsBackend.currentStatus().then((st) => {
    const current = get(websocketSessionAtom);
    if (current.id !== session || current.statusFromSocket) return;
    set(onLoadCurrentSt, st);
    set(currentStatusIsLiveRW, true);
  });
}

function loadInitial(get: Getter, set: Setter, session: number): void {
  const now = new Date();
  const thirtyDaysAgo = addDays(now, -30);

  const curStProm = loadCurrentStatus(get, set, session);
  const jobsProm = JobsBackend.recent(thirtyDaysAgo, []).then((j) => {
    if (isCurrentSession(get, session)) set(onLoadLast30Jobs, j);
  });
  const logProm = LogBackend.get(thirtyDaysAgo, now).then((log) => {
    if (isCurrentSession(get, session)) set(onLoadLast30Log, log);
  });

  finishLoading([curStProm, jobsProm, logProm], get, set, session);
}

function loadMissed(
  lastCntr: number,
  schIds: HashSet<string> | undefined,
  get: Getter,
  set: Setter,
  session: number,
): void {
  const now = new Date();
  const curStProm = loadCurrentStatus(get, set, session);
  const jobsProm = JobsBackend.recent(addDays(now, -30), schIds ? Array.from(schIds) : []).then(
    (j) => {
      if (isCurrentSession(get, session)) set(onLoadLast30Jobs, j);
    },
  );
  const logProm = LogBackend.recent(lastCntr, undefined).then((log) => {
    if (!isCurrentSession(get, session)) return;
    set(onLoadLast30Log, log);
    set(historyCatchUpFromAtom, null);
  });

  finishLoading([curStProm, jobsProm, logProm], get, set, session);
}

function finishLoading(
  loads: ReadonlyArray<Promise<void>>,
  get: Getter,
  set: Setter,
  session: number,
): void {
  Promise.all(loads)
    .catch((e: Record<string, string | undefined>) => {
      if (isCurrentSession(get, session)) set(errorLoadingLast30RW, e.message ?? "Error");
    })
    .finally(() => {
      if (isCurrentSession(get, session)) set(websocketReconnectingAtom, false);
    });
}

class ReconnectingWebsocket {
  // Callbacks receive the session their socket opened, or null before it opened.
  public handleOpen?: () => number;
  public handleMessage?: (evt: MessageEvent<string>, session: number | null) => void;
  public handleClose?: (session: number | null, reconnecting: boolean) => void;

  private readonly url: string;
  private ws?: WebSocket;
  private session: number | null = null;
  private userCalledClose = false;
  private reconnectAttempts = 0;

  public constructor(url: string) {
    this.url = url;
    this.connect();
  }

  public close() {
    this.userCalledClose = true;
    this.ws?.close();
    this.endSession(false);
  }

  private endSession(reconnecting: boolean) {
    const session = this.session;
    this.session = null;
    this.handleClose?.(session, reconnecting);
  }

  private connect() {
    if (this.userCalledClose) return;

    this.ws = new WebSocket(this.url);
    const localWs = this.ws;

    const connectTimeout = setTimeout(() => {
      localWs.close();
    }, 2000);

    localWs.addEventListener("open", () => {
      clearTimeout(connectTimeout);
      this.reconnectAttempts = 0;
      this.session = this.handleOpen?.() ?? null;
    });

    localWs.addEventListener("close", () => {
      clearTimeout(connectTimeout);
      this.ws = undefined;
      if (this.userCalledClose) {
        return;
      }

      this.endSession(true);
      const delay = Math.min(1000 * Math.pow(1.5, this.reconnectAttempts), 30000);
      setTimeout(() => {
        this.reconnectAttempts++;
        this.connect();
      }, delay);
    });

    localWs.addEventListener("message", (evt: MessageEvent<string>) => {
      this.handleMessage?.(evt, this.session);
    });

    // Browsers expose websocket failures as opaque events with no useful detail.
    // Reconnect handling is driven by close/open, so avoid polluting stderr by
    // adding an `onerror` handler that does nothing and can't log useful information.
  }
}

const onOpenAtom = atom(null, (get, set): number => {
  const catchUpFrom = get(historyCatchUpFromAtom) ?? get(lastEventCounter);
  const schIds = get(last30SchIds);
  const session = get(websocketSessionAtom).id + 1;
  set(websocketSessionAtom, { id: session, statusFromSocket: false });
  set(websocketReconnectingAtom, true);
  set(currentStatusIsLiveRW, false);
  set(errorLoadingLast30RW, null);
  if (catchUpFrom !== null) {
    set(historyCatchUpFromAtom, catchUpFrom);
    loadMissed(catchUpFrom, schIds, get, set, session);
  } else {
    loadInitial(get, set, session);
  }
  return session;
});

// Every close, including disposal, ends the socket's own session; only an unexpected close
// reconnects. A connection cannot end a newer connection's session.
const onCloseAtom = atom(null, (get, set, session: number | null, reconnecting: boolean) => {
  if (reconnecting) set(websocketReconnectingAtom, true);
  if (session === null || !isCurrentSession(get, session)) return;
  set(websocketSessionAtom, { id: session + 1, statusFromSocket: false });
  set(currentStatusIsLiveRW, false);
});

const onMessageAtom = atom(null, (get, set, evt: MessageEvent<string>, session: number | null) => {
  if (session === null || !isCurrentSession(get, session)) return;
  const serverEvt = ServerEvent.fromJS(JSON.parse(evt.data));
  set(onServerEvent, { evt: serverEvt, now: new Date(), expire: true });
  if (serverEvt.newCurrentStatus) {
    set(websocketSessionAtom, { id: session, statusFromSocket: true });
    set(currentStatusIsLiveRW, true);
  }
});

export function WebsocketConnection(): null {
  const onOpen = useSetAtom(onOpenAtom);
  const onClose = useSetAtom(onCloseAtom);
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
    websocket.handleOpen = onOpen;
    websocket.handleClose = onClose;
    websocket.handleMessage = onMessage;
    websocketRef.current = websocket;

    return () => {
      if (websocketRef.current) {
        websocketRef.current.close();
        websocketRef.current = null;
      }
    };
  }, [fmsInfoLoadable, onMessage, onOpen, onClose]);

  return null;
}
