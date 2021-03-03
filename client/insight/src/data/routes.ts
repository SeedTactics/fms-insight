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

import { useCallback, useMemo } from "react";
import { atom, useRecoilState } from "recoil";
import { useRouter } from "wouter";

export enum RouteLocation {
  ChooseMode = "/",

  Station_LoadMonitor = "/station/loadunload/:num",
  Station_InspectionMonitor = "/station/inspection",
  Station_InspectionMonitorWithType = "/station/inspection/:type",
  Station_WashMonitor = "/station/wash",
  Station_Queues = "/station/queues",

  Operations_Dashboard = "/operations",
  Operations_LoadStation = "/operations/loadunload",
  Operations_Machines = "/operations/machines",
  Operations_AllMaterial = "/operations/material",
  Operations_CompletedParts = "/operations/completed",
  Operations_Tools = "/operations/tools",
  Operations_Programs = "/operations/programs",

  Engineering = "/engineering",

  Quality_Dashboard = "/quality",
  Quality_Serials = "/quality/serials",
  Quality_Paths = "/quality/paths",
  Quality_Quarantine = "/quality/quarantine",

  Tools_Dashboard = "/tools",
  Tools_Programs = "/tools/programs",

  Analysis_Efficiency = "/analysis/efficiency",
  Analysis_CostPerPiece = "/analysis/cost",
  Analysis_DataExport = "/analysis/data-export",

  Backup_InitialOpen = "/backup/open",
  Backup_Efficiency = "/backup/efficiency",
  Backup_PartLookup = "/backup/lookup",
}

export type RouteState =
  | { route: RouteLocation.ChooseMode }
  | { route: RouteLocation.Station_LoadMonitor; loadNum: number; free: boolean; queues: ReadonlyArray<string> }
  | { route: RouteLocation.Station_InspectionMonitor }
  | { route: RouteLocation.Station_InspectionMonitorWithType; inspType: string }
  | { route: RouteLocation.Station_WashMonitor }
  | { route: RouteLocation.Station_Queues; queues: ReadonlyArray<string>; free: boolean }
  | { route: RouteLocation.Operations_Dashboard }
  | { route: RouteLocation.Operations_LoadStation }
  | { route: RouteLocation.Operations_Machines }
  | { route: RouteLocation.Operations_AllMaterial }
  | { route: RouteLocation.Operations_CompletedParts }
  | { route: RouteLocation.Operations_Tools }
  | { route: RouteLocation.Operations_Programs }
  | { route: RouteLocation.Engineering }
  | { route: RouteLocation.Quality_Dashboard }
  | { route: RouteLocation.Quality_Serials }
  | { route: RouteLocation.Quality_Paths }
  | { route: RouteLocation.Quality_Quarantine }
  | { route: RouteLocation.Tools_Dashboard }
  | { route: RouteLocation.Tools_Programs }
  | { route: RouteLocation.Analysis_Efficiency }
  | { route: RouteLocation.Analysis_CostPerPiece }
  | { route: RouteLocation.Analysis_DataExport }
  | { route: RouteLocation.Backup_InitialOpen }
  | { route: RouteLocation.Backup_Efficiency }
  | { route: RouteLocation.Backup_PartLookup };

function routeToUrl(route: RouteState): string {
  switch (route.route) {
    case RouteLocation.Station_LoadMonitor:
      if (route.free || route.queues.length > 0) {
        const params = new URLSearchParams();
        if (route.free) {
          params.append("free", "t");
        }
        for (const q of route.queues) {
          params.append("queue", q);
        }
        return `/station/loadunload/${route.loadNum}?${params.toString()}`;
      } else {
        return `/station/loadunload/${route.loadNum}`;
      }

    case RouteLocation.Station_InspectionMonitorWithType:
      return `/station/inspection/${encodeURIComponent(route.inspType)}`;

    case RouteLocation.Station_Queues:
      if (route.free || route.queues.length > 0) {
        const params = new URLSearchParams();
        if (route.free) {
          params.append("free", "t");
        }
        for (const q of route.queues) {
          params.append("queue", q);
        }
        return `/station/queues?${params.toString()}`;
      } else {
        return `/station/queues`;
      }

    default:
      return route.route;
  }
}

const demoLocation = atom<string>({ key: "demo-location", default: RouteLocation.Operations_Dashboard });

export function useDemoLocation(): [string, (s: string) => void] {
  return useRecoilState(demoLocation);
}

export function useIsDemo(): boolean {
  const router = useRouter();
  return router.hook === useDemoLocation;
}

export function useCurrentRoute(): [RouteState, (r: RouteState) => void] {
  const router = useRouter();
  let [location, setLocation] = router.hook();

  let queryString: string = "";
  if (router.hook === useDemoLocation) {
    const idx = location.indexOf("?");
    if (idx >= 0) {
      queryString = location.substring(idx + 1);
      location = location.substring(0, idx);
    }
  } else {
    queryString = window.location.search;
  }

  const setRoute = useCallback((r) => setLocation(routeToUrl(r)), []);

  const curRoute: RouteState = useMemo(() => {
    for (const route of Object.values(RouteLocation)) {
      const [matches, params] = router.matcher(route, location);
      if (matches) {
        switch (route) {
          case RouteLocation.Station_LoadMonitor:
            if (params && params.num !== undefined) {
              const search = new URLSearchParams(queryString);
              return {
                route: RouteLocation.Station_LoadMonitor,
                loadNum: parseInt(params.num, 10),
                free: search.has("free"),
                queues: search.getAll("queue"),
              };
            } else {
              return {
                route: RouteLocation.Station_LoadMonitor,
                loadNum: 1,
                free: false,
                queues: [],
              };
            }

          case RouteLocation.Station_InspectionMonitorWithType:
            if (params?.type) {
              return {
                route: RouteLocation.Station_InspectionMonitorWithType,
                inspType: params.type,
              };
            } else {
              return { route: RouteLocation.Station_InspectionMonitor };
            }

          case RouteLocation.Station_Queues:
            const search = new URLSearchParams(queryString);
            return {
              route: RouteLocation.Station_Queues,
              free: search.has("free"),
              queues: search.getAll("queue"),
            };

          default:
            return { route: route };
        }
      }
    }
    return { route: RouteLocation.ChooseMode };
  }, [location, queryString]);

  return [curRoute, setRoute];
}
