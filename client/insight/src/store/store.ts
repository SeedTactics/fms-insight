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
import { createStore, GenericStoreEnhancer, combineReducers, compose, applyMiddleware } from 'redux';

import * as currentStatus from '../data/current-status';
import * as events from '../data/events';
import * as gui from '../data/gui-state';
import * as routes from '../data/routes';
import * as mat from '../data/material-details';
import * as operators from '../data/operators';
import * as serverSettings from '../data/server-settings';
import * as ccp from '../data/cost-per-piece';
import * as websocket from './websocket';

import { pledgeMiddleware, arrayMiddleware, ActionBeforeMiddleware } from './middleware';
import * as tstore from './typed-store';

import { connectRoutes, LocationState } from 'redux-first-router';
import createHistory from 'history/createBrowserHistory';
import * as queryString from 'query-string';
import * as reactRedux from 'react-redux';
import * as redux from 'redux';
import { initBarcodeListener } from './barcode';

export interface Store {
  readonly Current: currentStatus.State;
  readonly Events: events.State;
  readonly Gui: gui.State;
  readonly MaterialDetails: mat.State;
  readonly Route: routes.State;
  readonly Websocket: websocket.State;
  readonly Operators: operators.State;
  readonly ServerSettings: serverSettings.State;
  readonly CostPerPiece: ccp.State;
  readonly location: LocationState;
}

export type AppAction =
  | currentStatus.Action
  | events.Action
  | gui.Action
  | mat.Action
  | routes.Action
  | operators.Action
  | ccp.Action
  ;

export type AppActionBeforeMiddleware = ActionBeforeMiddleware<AppAction>;

export const connect: tstore.Connect<AppActionBeforeMiddleware, Store> = reactRedux.connect;
export const mkAC: tstore.ActionCreatorFactory<AppActionBeforeMiddleware> =
  tstore.actionCreatorFactory<AppActionBeforeMiddleware>();
export type DispatchAction<T> = tstore.DispatchAction<AppAction, T>;

export function initStore() {
  const history = createHistory();
  const router = connectRoutes(
    history,
    routes.routeMap,
    {
      querySerializer: queryString
    });

  /* tslint:disable */
  const devTools: GenericStoreEnhancer =
    (window as any)['__REDUX_DEVTOOLS_EXTENSION__']
    ? (window as any)['__REDUX_DEVTOOLS_EXTENSION__']()
    : f => f;
  /* tslint:enable */

  const store = createStore<Store>(
    combineReducers<Store>(
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
        location: router.reducer,
      // tslint:disable-next-line:no-any
      } as any // bug in typescript types for combineReducers
    ),
    compose(
      router.enhancer,
      applyMiddleware(
          arrayMiddleware,
          pledgeMiddleware,
          router.middleware
      ),
      devTools
    )
  );

  websocket.openWebsocket(a => store.dispatch(a), () => store.getState().Events);
  initBarcodeListener(a => store.dispatch(a as redux.Action));

  const operatorOnStateChange = operators.createOnStateChange();
  store.subscribe(() => operatorOnStateChange(store.getState().Operators));
  store.dispatch(serverSettings.loadServerSettings() as redux.Action);

  return store;
}