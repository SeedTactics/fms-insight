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

import { Action } from 'redux-first-router';

export enum RouteLocation {
  Dashboard = 'ROUTE_Dashboard',
  StationMonitor = 'ROUTE_StationMonitor',
  CostPerPiece = 'ROUTE_CostPerPiece',
  Efficiency = 'ROUTE_Efficiency'
}

export const routeMap = {
  [RouteLocation.Dashboard]: '/',
  [RouteLocation.StationMonitor]: '/station',
  [RouteLocation.CostPerPiece]: '/cost',
  [RouteLocation.Efficiency]: '/efficiency',
};

export interface State {
  readonly current: RouteLocation;
}

export const initial: State = {
  current: RouteLocation.Dashboard
};

export function reducer(s: State, a: Action): State {
  if ( s === undefined) { return initial; }
  switch (a.type) {
    case RouteLocation.StationMonitor:
      return { current: RouteLocation.StationMonitor };
    case RouteLocation.CostPerPiece:
      return { current: RouteLocation.CostPerPiece };
    case RouteLocation.Efficiency:
      return { current: RouteLocation.Efficiency };
    case RouteLocation.Dashboard:
      return { current: RouteLocation.Dashboard };
    default:
      return s;
  }
}