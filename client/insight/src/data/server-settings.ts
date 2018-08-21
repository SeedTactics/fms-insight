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

import * as api from "./api";
import { ServerBackend, setOtherLogBackends } from "./backend";
import {
  Pledge,
  PledgeStatus,
  ActionBeforeMiddleware
} from "../store/middleware";

export enum ActionType {
  Load = "ServerSettings_Load"
}

export interface LatestInstaller {
  readonly version: string;
  readonly date: Date;
}

interface LoadReturn {
  readonly fmsInfo: Readonly<api.IFMSInfo>;
  readonly latestVersion?: LatestInstaller;
}

export type Action = { type: ActionType.Load; pledge: Pledge<LoadReturn> };

export interface State {
  readonly fmsInfo?: Readonly<api.IFMSInfo>;
  readonly latestInstaller?: LatestInstaller;
  readonly loadError?: Error;
}

export const initial: State = {};

async function loadInfo(): Promise<LoadReturn> {
  const fmsInfo = await ServerBackend.fMSInformation();

  if (fmsInfo.additionalLogServers && fmsInfo.additionalLogServers.length > 0) {
    setOtherLogBackends(fmsInfo.additionalLogServers);
  }

  let url: string | undefined;
  let latestVersion: LatestInstaller | undefined;
  switch (fmsInfo.name) {
    case "Mazak":
      url = "https://fms-insight.seedtactics.com/installers/Mazak-latest.json";
      break;
    case "Makino":
      url = "https://fms-insight.seedtactics.com/installers/Makino-latest.json";
      break;
    case "mock":
      url = "https://fms-insight.seedtactics.com/installers/Mazak-latest.json";
      break;
  }
  if (url) {
    const res = await fetch(url);
    if (res && res.ok) {
      const data = await res.json();
      latestVersion = {
        version: data.version,
        date: new Date(Date.parse(data.date))
      };
    }
  }

  return { fmsInfo, latestVersion };
}

export function loadServerSettings(): ActionBeforeMiddleware<Action> {
  return {
    type: ActionType.Load,
    pledge: loadInfo()
  };
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) {
    return initial;
  }
  switch (a.type) {
    case ActionType.Load:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return s; // do nothing
        case PledgeStatus.Completed:
          return {
            fmsInfo: a.pledge.result.fmsInfo,
            latestInstaller: a.pledge.result.latestVersion
          };
        case PledgeStatus.Error:
          return { ...s, loadError: a.pledge.error };
        default:
          return s;
      }

    default:
      return s;
  }
}
