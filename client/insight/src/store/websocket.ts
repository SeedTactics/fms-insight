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
import * as events from "../data/events";
import { LogEntry, NewJobs, CurrentStatus, EditMaterialInLogEvents } from "../data/api";
import { BackendHost, JobsBackend } from "../data/backend";
import { User } from "oidc-client";
import { fmsInformation } from "../data/server-settings";
import { atom, RecoilValue, useRecoilCallback, useRecoilValueLoadable } from "recoil";
import { useEffect, useRef } from "react";
import { currentStatus, processEventsIntoCurrentStatus } from "../data/current-status";

const websocketReconnectingAtom = atom<boolean>({
  key: "websocket-reconnecting",
  default: false,
});

export const websocketReconnecting: RecoilValue<boolean> = websocketReconnectingAtom;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
let storeDispatch: ((a: any) => void) | undefined;
let getEvtState: (() => events.State) | undefined;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function configureWebsocket(d: (a: any) => void, ges: () => events.State) {
  storeDispatch = d;
  getEvtState = ges;
}

export function WebsocketConnection(): null {
  const fmsInfoLoadable = useRecoilValueLoadable(fmsInformation);
  const websocketRef = useRef<ReconnectingWebSocket | null>(null);

  const open = useRecoilCallback(
    ({ set }) => (user: User | null) => {
      if (!storeDispatch || !getEvtState) {
        return;
      }

      set(websocketReconnectingAtom, true);
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
      websocket.onopen = () => {
        if (!storeDispatch || !getEvtState) {
          return;
        }

        const st = getEvtState();
        if (st.last30.latest_log_counter !== undefined) {
          storeDispatch(events.refreshLogEntries(st.last30.latest_log_counter));
        } else {
          storeDispatch(events.loadLast30Days());
        }

        JobsBackend.currentStatus().then((st) => {
          set(currentStatus, st);
          set(websocketReconnectingAtom, false);
        });
      };
      websocket.onclose = () => {
        if (!storeDispatch) {
          return;
        }
        set(websocketReconnectingAtom, true);
      };
      websocket.onmessage = (evt) => {
        if (!storeDispatch) {
          return;
        }
        const json = JSON.parse(evt.data);
        if (json.LogEntry) {
          const entry = LogEntry.fromJS(json.LogEntry);
          storeDispatch(events.receiveNewEvents([entry]));
          set(currentStatus, processEventsIntoCurrentStatus(entry));
        } else if (json.NewJobs) {
          const newJobs = NewJobs.fromJS(json.NewJobs);
          storeDispatch(events.receiveNewJobs(newJobs));
        } else if (json.NewCurrentStatus) {
          const status = CurrentStatus.fromJS(json.NewCurrentStatus);
          set(currentStatus, status);
        } else if (json.EditMaterialInLog) {
          const swap = EditMaterialInLogEvents.fromJS(json.EditMaterialInLog);
          storeDispatch(events.onEditMaterialOnPallet(swap));
        }
      };

      websocketRef.current = websocket;
    },
    []
  );

  useEffect(() => {
    if (fmsInfoLoadable.state !== "hasValue") return;
    if (websocketRef.current) return;

    open(fmsInfoLoadable.valueOrThrow().user ?? null);

    return () => {
      if (websocketRef.current) {
        websocketRef.current.close();
        websocketRef.current = null;
      }
    };
  }, [fmsInfoLoadable]);

  return null;
}
