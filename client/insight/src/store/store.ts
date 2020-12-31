/* Copyright (c) 2019, John Lenz

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
import * as paths from "../data/path-lookup";
import * as websocket from "./websocket";
import { initBarcodeListener } from "./barcode";

import { createStore, StoreState, StoreActions, ACPayload, mkACF } from "./typed-redux";
import * as redux from "redux";
import * as reactRedux from "react-redux";
import { middleware } from "./middleware";

import { connectRoutes, LocationState } from "redux-first-router";
import * as queryString from "query-string";

/* eslint-disable @typescript-eslint/no-use-before-define */

type InitToStore<I> = I extends (a: { useRouter: boolean }) => infer S ? S : never;
export type Store = StoreState<InitToStore<typeof initStore>>;
export type AppActionBeforeMiddleware = StoreActions<InitToStore<typeof initStore>>;
export type Payload<T> = ACPayload<InitToStore<typeof initStore>, T>;
export type DispatchAction<T> = (payload: Payload<T>) => void;
export const connect: InitToStore<typeof initStore>["connect"] = reactRedux.connect;
export const mkAC = mkACF<AppActionBeforeMiddleware>();
export const useSelector: reactRedux.TypedUseSelectorHook<Store> = reactRedux.useSelector;

export let reduxStore: redux.Store<Store> | null = null;

export function initStore({ useRouter }: { useRouter: boolean }) {
  const router = useRouter
    ? connectRoutes(routes.routeMap, {
        querySerializer: queryString,
      })
    : undefined;

  /* eslint-disable @typescript-eslint/no-explicit-any */
  const composeEnhancers =
    typeof window === "object" && (window as any)["__REDUX_DEVTOOLS_EXTENSION_COMPOSE__"]
      ? (window as any)["__REDUX_DEVTOOLS_EXTENSION_COMPOSE__"]
      : redux.compose;
  /* eslint-enable @typescript-eslint/no-explicit-any */

  const store = createStore(
    {
      Current: currentStatus.reducer,
      Events: events.reducer,
      Gui: gui.reducer,
      MaterialDetails: mat.reducer,
      PathLookup: paths.reducer,
      Route: routes.reducer,
      Websocket: websocket.reducer,
      location: router ? router.reducer : (s: LocationState<string>, _: object) => s || {},
    },
    middleware,
    router
      ? (m) => composeEnhancers(router.enhancer, redux.applyMiddleware(m, router.middleware))
      : (m) => composeEnhancers(redux.applyMiddleware(m))
  );

  initBarcodeListener(store.dispatch.bind(store));

  reduxStore = store as any;
  return store;
}
