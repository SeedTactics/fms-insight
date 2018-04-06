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

import * as currentStatus from './current-status';
import * as events from './events';
import * as gui from './gui-state';
import * as routes from './routes';
import * as mat from './material-details';
import * as websocket from './websocket';
import { pledgeMiddleware } from './pledge';
import * as tstore from './typed-store';

import { connectRoutes, LocationState } from 'redux-first-router';
import createHistory from 'history/createBrowserHistory';
import * as queryString from 'query-string';
import * as reactRedux from 'react-redux';

export interface Store {
  readonly Current: currentStatus.State;
  readonly Events: events.State;
  readonly Gui: gui.State;
  readonly MaterialDetails: mat.State;
  readonly Route: routes.State;
  readonly Websocket: websocket.State;
  readonly location: LocationState;
}

export type AppAction =
  | currentStatus.Action
  | events.Action
  | gui.Action
  | mat.Action
  | routes.Action
  ;

export const connect: tstore.Connect<AppAction, Store> = reactRedux.connect;
export const mkAC: tstore.ActionCreatorFactory<AppAction> = tstore.actionCreatorFactory<AppAction>();
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

  const arrayMiddleware =
    // tslint:disable-next-line:no-any
    ({dispatch}: {dispatch: reactRedux.Dispatch<any>}) => (next: reactRedux.Dispatch<any>) => (action: any) => {
      if (action instanceof Array) {
        action.map(dispatch);
      } else {
        return next(action);
      }
  };

  const store = createStore<Store>(
    combineReducers<Store>(
      {
        Current: currentStatus.reducer,
        Events: events.reducer,
        Gui: gui.reducer,
        MaterialDetails: mat.reducer,
        Route: routes.reducer,
        Websocket: websocket.reducer,
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

  return store;
}