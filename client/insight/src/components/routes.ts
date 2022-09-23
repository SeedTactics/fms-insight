/* Copyright (c) 2022, John Lenz

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

import { LazySeq } from "@seedtactics/immutable-collections";
import { useCallback, useEffect } from "react";
import { atom, useRecoilState, useRecoilValue, useSetRecoilState } from "recoil";
import "urlpattern-polyfill";

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
  Operations_RecentSchedules = "/operations/recent-schedules",
  Operations_Tools = "/operations/tools",
  Operations_Programs = "/operations/programs",

  Engineering = "/engineering",

  Quality_Dashboard = "/quality",
  Quality_Serials = "/quality/serials",
  Quality_Paths = "/quality/paths",
  Quality_Quarantine = "/quality/quarantine",

  Tools_Dashboard = "/tools",
  Tools_Programs = "/tools/programs",

  Analysis_Cycles = "/analysis/cycles",
  Analysis_Efficiency = "/analysis/efficiency",
  Analysis_Quality = "/analysis/quality",
  Analysis_CostPerPiece = "/analysis/cost",
  Analysis_Schedules = "/analysis/schedules",
  Analysis_DataExport = "/analysis/data-export",

  Backup_InitialOpen = "/backup/open",
  Backup_Efficiency = "/backup/efficiency",
  Backup_PartLookup = "/backup/lookup",
  Backup_Schedules = "/backup/schedules",

  Client_Custom = "/client/:custom+",
}

const patterns = LazySeq.ofObject(RouteLocation)
  .map(([, loc]) => [loc, new URLPattern(loc, location.origin)] as const)
  // redirects from old URLs
  .append([
    RouteLocation.Operations_RecentSchedules,
    new URLPattern("/operations/completed", location.origin),
  ])
  .toRArray();

export type RouteState =
  | { route: RouteLocation.ChooseMode }
  | {
      route: RouteLocation.Station_LoadMonitor;
      loadNum: number;
      free: boolean;
      queues: ReadonlyArray<string>;
    }
  | { route: RouteLocation.Station_InspectionMonitor }
  | { route: RouteLocation.Station_InspectionMonitorWithType; inspType: string }
  | { route: RouteLocation.Station_WashMonitor }
  | { route: RouteLocation.Station_Queues; queues: ReadonlyArray<string>; free: boolean }
  | { route: RouteLocation.Operations_Dashboard }
  | { route: RouteLocation.Operations_LoadStation }
  | { route: RouteLocation.Operations_Machines }
  | { route: RouteLocation.Operations_AllMaterial }
  | { route: RouteLocation.Operations_RecentSchedules }
  | { route: RouteLocation.Operations_Tools }
  | { route: RouteLocation.Operations_Programs }
  | { route: RouteLocation.Engineering }
  | { route: RouteLocation.Quality_Dashboard }
  | { route: RouteLocation.Quality_Serials }
  | { route: RouteLocation.Quality_Paths }
  | { route: RouteLocation.Quality_Quarantine }
  | { route: RouteLocation.Tools_Dashboard }
  | { route: RouteLocation.Tools_Programs }
  | { route: RouteLocation.Analysis_Cycles }
  | { route: RouteLocation.Analysis_Efficiency }
  | { route: RouteLocation.Analysis_Quality }
  | { route: RouteLocation.Analysis_CostPerPiece }
  | { route: RouteLocation.Analysis_Schedules }
  | { route: RouteLocation.Analysis_DataExport }
  | { route: RouteLocation.Backup_InitialOpen }
  | { route: RouteLocation.Backup_Efficiency }
  | { route: RouteLocation.Backup_Schedules }
  | { route: RouteLocation.Backup_PartLookup }
  | { route: RouteLocation.Client_Custom; custom: ReadonlyArray<string> };

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

    case RouteLocation.Client_Custom:
      return "/client/" + route.custom.map((r) => encodeURIComponent(r)).join("/");

    default:
      return route.route;
  }
}

function urlToRoute(url: URL): RouteState {
  for (const [route, pattern] of patterns) {
    const groups = pattern.exec(url.origin + url.pathname)?.pathname?.groups;
    if (groups) {
      switch (route) {
        case RouteLocation.Station_LoadMonitor:
          return {
            route,
            loadNum: parseInt(groups["num"] ?? "1"),
            free: url.searchParams.has("free"),
            queues: url.searchParams.getAll("queue"),
          };
        case RouteLocation.Station_InspectionMonitorWithType: {
          const inspType = groups["type"];
          if (inspType) {
            return {
              route: RouteLocation.Station_InspectionMonitorWithType,
              inspType,
            };
          } else {
            return { route: RouteLocation.Station_InspectionMonitor };
          }
        }

        case RouteLocation.Station_Queues: {
          return {
            route: RouteLocation.Station_Queues,
            free: url.searchParams.has("free"),
            queues: url.searchParams.getAll("queue"),
          };
        }

        case RouteLocation.Client_Custom:
          return {
            route: RouteLocation.Client_Custom,
            custom: groups?.["custom"]?.split("/")?.map((s: string) => decodeURIComponent(s)) ?? [],
          };
        default:
          return { route } as RouteState;
      }
    }
  }
  return { route: RouteLocation.ChooseMode };
}

const currentRouteLocation = atom<RouteState>({
  key: "fms-insight-location",
  default: urlToRoute(new URL(location.href)),
});
export const isDemoAtom = atom<boolean>({ key: "fms-insight-demo", default: false });

export function useWatchHistory(): void {
  const isDemo = useRecoilValue(isDemoAtom);
  const setCurRoute = useSetRecoilState(currentRouteLocation);

  // check for changes in history (back, forwards)
  useEffect(() => {
    if (isDemo) return;
    function updateRoute() {
      setCurRoute(urlToRoute(new URL(location.href)));
    }

    addEventListener("popstate", updateRoute);

    return () => removeEventListener("popstate", updateRoute);
  }, [isDemo]);
}

export function useSetCurrentRoute(): (r: RouteState) => void {
  const isDemo = useRecoilValue(isDemoAtom);
  const setCurRoute = useSetRecoilState(currentRouteLocation);

  const setRouteAndUpdateHistory = useCallback((to: RouteState) => {
    setCurRoute(to);
    history.pushState(null, "", routeToUrl(to));
  }, []);

  if (isDemo) {
    return setCurRoute;
  } else {
    return setRouteAndUpdateHistory;
  }
}

export function useCurrentRoute(): [RouteState, (r: RouteState) => void] {
  const isDemo = useRecoilValue(isDemoAtom);
  const [curRoute, setCurRoute] = useRecoilState(currentRouteLocation);

  const setRouteAndUpdateHistory = useCallback((to: RouteState) => {
    setCurRoute(to);
    history.pushState(null, "", routeToUrl(to));
  }, []);

  if (isDemo) {
    return [curRoute, setCurRoute];
  } else {
    return [curRoute, setRouteAndUpdateHistory];
  }
}

export function useIsDemo(): boolean {
  return useRecoilValue(isDemoAtom);
}
