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
import * as currentStatus from "../data/current-status";
import * as events from "../data/events";
import * as gui from "../data/gui-state";
import * as routes from "../data/routes";
import * as mat from "../data/material-details";
import * as operators from "../data/operators";
import * as serverSettings from "../data/server-settings";
import { registerMockBackend } from "../data/backend";
import * as ccp from "../data/cost-per-piece";
import * as websocket from "./websocket";
import { initBarcodeListener } from "./barcode";

import { createStore, StoreState, StoreActions, ACPayload, mkACF } from "./typed-redux";
import { applyMiddleware, compose } from "redux";
import * as reactRedux from "react-redux";
import { middleware } from "./middleware";

import * as im from "immutable";
import { connectRoutes, LocationState } from "redux-first-router";
import createHistory from "history/createBrowserHistory";
import * as queryString from "query-string";

type InitToStore<I> = I extends (demo: boolean) => infer S ? S : never;
export type Store = StoreState<InitToStore<typeof initStore>>;
export type AppActionBeforeMiddleware = StoreActions<InitToStore<typeof initStore>>;
export type Payload<T> = ACPayload<InitToStore<typeof initStore>, T>;
export type DispatchAction<T> = (payload: Payload<T>) => void;
export const connect: InitToStore<typeof initStore>["connect"] = reactRedux.connect;
export const mkAC: InitToStore<typeof initStore>["mkAC"] = mkACF();

export function initStore(demo: boolean) {
  const history = createHistory();
  const router = demo
    ? undefined
    : connectRoutes(history, routes.routeMap, {
        querySerializer: queryString
      });

  /* tslint:disable */
  const composeEnhancers =
    typeof window === "object" && (window as any)["__REDUX_DEVTOOLS_EXTENSION_COMPOSE__"]
      ? (window as any)["__REDUX_DEVTOOLS_EXTENSION_COMPOSE__"]({
          serialize: {
            immutable: im
          }
        })
      : compose;
  /* tslint:enable */

  const store = createStore(
    {
      Current: currentStatus.reducer,
      Events: events.reducer,
      Gui: gui.reducer,
      MaterialDetails: mat.reducer,
      Route: routes.reducer,
      Websocket: websocket.reducer,
      Operators: operators.reducer,
      ServerSettings: serverSettings.reducer,
      CostPerPiece: ccp.reducer,
      location: router ? router.reducer : (s: LocationState<string>, a: object) => s || {}
    },
    middleware,
    router
      ? m => composeEnhancers(router.enhancer, applyMiddleware(m, router.middleware))
      : m => composeEnhancers(applyMiddleware(m))
  );

  if (demo) {
    registerMockBackend();
    store.dispatch(events.loadLast30Days());
    store.dispatch(currentStatus.loadCurrentStatus());
  } else {
    websocket.openWebsocket(a => store.dispatch(a), () => store.getState().Events);
  }
  initBarcodeListener(store.dispatch);

  const operatorOnStateChange = operators.createOnStateChange();
  store.subscribe(() => operatorOnStateChange(store.getState().Operators));
  store.dispatch(serverSettings.loadServerSettings());

  return store;
}
