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

import { User, UserManager } from "oidc-client";
import * as api from "./api";
import { FmsServerBackend, setOtherLogBackends, setUserToken } from "./backend";
import { Pledge, PledgeStatus, ActionBeforeMiddleware } from "../store/middleware";
import { openWebsocket } from "../store/websocket";

export enum ActionType {
  Load = "ServerSettings_Load",
  Login = "ServerSettings_Login",
  Logout = "ServerSettings_Logout"
}

export interface LoadReturn {
  readonly fmsInfo: Readonly<api.IFMSInfo>;
  readonly user?: User;
}

export type Action =
  | { type: ActionType.Load; pledge: Pledge<LoadReturn> }
  | { type: ActionType.Login }
  | { type: ActionType.Logout };

export interface State {
  readonly fmsInfo?: Readonly<api.IFMSInfo>;
  readonly loadError?: Error;
  readonly user?: User;
}

export const initial: State = {};

let userManager: UserManager | undefined;

export function requireLogin(fmsInfo: Readonly<api.IFMSInfo>): boolean {
  if (!fmsInfo.openIDConnectClientId) return false;
  if (location.hostname === "localhost") {
    return !!fmsInfo.localhostOpenIDConnectAuthority;
  } else {
    return !!fmsInfo.openIDConnectAuthority;
  }
}

async function loadInfo(): Promise<LoadReturn> {
  const fmsInfo = await FmsServerBackend.fMSInformation();

  if (fmsInfo.additionalLogServers && fmsInfo.additionalLogServers.length > 0) {
    setOtherLogBackends(fmsInfo.additionalLogServers);
  }

  let user: User | null = null;
  if (requireLogin(fmsInfo)) {
    userManager = new UserManager({
      authority:
        location.hostname === "localhost" ? fmsInfo.localhostOpenIDConnectAuthority : fmsInfo.openIDConnectAuthority,
      client_id: fmsInfo.openIDConnectClientId,
      redirect_uri: window.location.protocol + "//" + window.location.host + "/",
      post_logout_redirect_uri: window.location.protocol + "//" + window.location.host + "/",
      automaticSilentRenew: true,
      scope: "openid profile"
    });
    user = await userManager.getUser();
    if (!user) {
      try {
        user = await userManager.signinRedirectCallback();
      } catch {
        user = null;
      }
      window.history.replaceState({}, "", "/");
    }
    if (user) {
      setUserToken(user);
      localStorage.setItem("current-operator", user.profile.name || user.profile.sub);
      openWebsocket(user);
    }
  } else {
    openWebsocket(user);
  }

  return { fmsInfo, user: user === null ? undefined : user };
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
            user: a.pledge.result.user
          };
        case PledgeStatus.Error:
          return { ...s, loadError: a.pledge.error };
        default:
          return s;
      }

    case ActionType.Login:
      if (userManager && !s.user) {
        userManager.signinRedirect();
      }
      return s;

    case ActionType.Logout:
      if (userManager && s.user) {
        userManager.signoutRedirect();
      }
      return s;

    default:
      return s;
  }
}
