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

import { User, UserManager } from "oidc-client-ts";
import * as api from "./api.js";
import { FmsServerBackend, setOtherLogBackends, setUserToken } from "./backend.js";
import { atom } from "jotai";

export interface FMSInfoAndUser extends Readonly<api.IFMSInfo> {
  readonly user?: User;
}

let userManager: UserManager | undefined;

export function requireLogin(fmsInfo: Readonly<api.IFMSInfo>): boolean {
  if (!fmsInfo.openIDConnectClientId) return false;
  if (location.hostname === "localhost") {
    return !!fmsInfo.localhostOpenIDConnectAuthority;
  } else {
    return !!fmsInfo.openIDConnectAuthority;
  }
}

export async function loadInfo(): Promise<FMSInfoAndUser> {
  const fmsInfo = await FmsServerBackend.fMSInformation();

  if (fmsInfo.additionalLogServers && fmsInfo.additionalLogServers.length > 0) {
    setOtherLogBackends(fmsInfo.additionalLogServers);
  }

  let user: User | null = null;
  if (requireLogin(fmsInfo)) {
    userManager = new UserManager({
      authority:
        (location.hostname === "localhost"
          ? fmsInfo.localhostOpenIDConnectAuthority
          : fmsInfo.openIDConnectAuthority) ?? "",
      client_id: fmsInfo.openIDConnectClientId ?? "",
      redirect_uri: window.location.protocol + "//" + window.location.host + "/",
      post_logout_redirect_uri: window.location.protocol + "//" + window.location.host + "/",
      automaticSilentRenew: true,
      loadUserInfo: true,
      scope: "openid profile",
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
    }
  }

  return { ...fmsInfo, user: user === null ? undefined : user };
}

export const fmsInformation = atom<FMSInfoAndUser>({ name: "FMS Insight", version: "" });

export function login(fmsInfo: FMSInfoAndUser) {
  if (userManager && !fmsInfo.user) {
    userManager.signinRedirect().catch((e) => console.log(e));
  }
}

export function logout() {
  if (userManager) {
    userManager.signoutRedirect().catch((e) => console.log(e));
  }
}
