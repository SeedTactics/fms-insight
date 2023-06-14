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
import { useEffect } from "react";
import "urlpattern-polyfill";
import { materialDialogOpen } from "../cell-status/material-details.js";
import { atom, useAtomValue, useSetAtom } from "jotai";

export enum RouteLocation {
  ChooseMode = "/",

  Station_LoadMonitor = "/station/loadunload/:num",
  Station_InspectionMonitor = "/station/inspection",
  Station_InspectionMonitorWithType = "/station/inspection/:type",
  Station_Closeout = "/station/closeout",
  Station_Queues = "/station/queues",
  Station_Overview = "/station/overview",

  Operations_Dashboard = "/operations",
  Operations_LoadOutliers = "/operations/load-outliers",
  Operations_LoadHours = "/operations/load-hours",
  Operations_LoadCycles = "/operations/load-cycles",
  Operations_MachineOutliers = "/operations/machine-outliers",
  Operations_MachineHours = "/operations/machine-hours",
  Operations_MachineCycles = "/operations/machine-cycles",
  Operations_SystemOverview = "/operations/system-overview",
  Operations_AllMaterial = "/operations/material",
  Operations_RecentSchedules = "/operations/recent-schedules",
  Operations_CurrentWorkorders = "/operations/current-workorders",
  Operations_Production = "/operations/recent-production",
  Operations_Tools = "/operations/tools",
  Operations_Programs = "/operations/programs",
  Operations_Quality = "/operations/quality",
  Operations_Inspections = "/operations/inspections",

  Engineering_Cycles = "/engineering",
  Engineering_Hours = "/engineering/hours",
  Engineering_Outliers = "/engineering/outliers",

  Quality_Dashboard = "/quality",
  Quality_Paths = "/quality/paths",
  Quality_Quarantine = "/quality/quarantine",

  Tools_Dashboard = "/tools",
  Tools_Programs = "/tools/programs",

  Analysis_Buffers = "/analysis/buffers",
  Analysis_StationOEE = "/analysis/station-oee",
  Analysis_PartsCompleted = "/analysis/parts-completed",
  Analysis_MachineCycles = "/analysis/machine-cycles",
  Analysis_LoadCycles = "/analysis/load-cycles",
  Analysis_PalletCycles = "/analysis/pallet-cycles",
  Analysis_Schedules = "/analysis/schedules",
  Analysis_Quality = "/analysis/quality",
  Analysis_ToolReplacements = "/analysis/tool-replacements",
  Analysis_CostPercents = "/analysis/cost-percents",

  Analysis_CostPerPiece = "/analysis/cost",

  VerboseLogging = "/logging",

  Client_Custom = "/client/:custom+",
}

const patterns = LazySeq.ofObject(RouteLocation)
  .map(([, loc]) => [loc, new URLPattern(loc, location.origin)] as const)
  // redirects from old URLs
  .append([
    RouteLocation.Operations_RecentSchedules,
    new URLPattern("/operations/completed", location.origin),
  ])
  .append([RouteLocation.Station_Closeout, new URLPattern("/station/wash", location.origin)])
  .toRArray();

export type RouteState =
  | { route: RouteLocation.ChooseMode }
  | {
      route: RouteLocation.Station_LoadMonitor;
      loadNum: number;
      queues: ReadonlyArray<string>;
      completed: boolean;
    }
  | { route: RouteLocation.Station_InspectionMonitor }
  | { route: RouteLocation.Station_InspectionMonitorWithType; inspType: string }
  | { route: RouteLocation.Station_Closeout }
  | { route: RouteLocation.Station_Queues; queues: ReadonlyArray<string> }
  | { route: RouteLocation.Station_Overview }
  | { route: RouteLocation.Operations_Dashboard }
  | { route: RouteLocation.Operations_LoadOutliers }
  | { route: RouteLocation.Operations_LoadHours }
  | { route: RouteLocation.Operations_LoadCycles }
  | { route: RouteLocation.Operations_MachineOutliers }
  | { route: RouteLocation.Operations_MachineHours }
  | { route: RouteLocation.Operations_MachineCycles }
  | { route: RouteLocation.Operations_SystemOverview }
  | { route: RouteLocation.Operations_AllMaterial }
  | { route: RouteLocation.Operations_RecentSchedules }
  | { route: RouteLocation.Operations_CurrentWorkorders }
  | { route: RouteLocation.Operations_Production }
  | { route: RouteLocation.Operations_Tools }
  | { route: RouteLocation.Operations_Programs }
  | { route: RouteLocation.Operations_Quality }
  | { route: RouteLocation.Operations_Inspections }
  | { route: RouteLocation.Engineering_Cycles }
  | { route: RouteLocation.Engineering_Outliers }
  | { route: RouteLocation.Engineering_Hours }
  | { route: RouteLocation.Quality_Dashboard }
  | { route: RouteLocation.Quality_Paths }
  | { route: RouteLocation.Quality_Quarantine }
  | { route: RouteLocation.Tools_Dashboard }
  | { route: RouteLocation.Tools_Programs }
  | { route: RouteLocation.Analysis_Buffers }
  | { route: RouteLocation.Analysis_StationOEE }
  | { route: RouteLocation.Analysis_PartsCompleted }
  | { route: RouteLocation.Analysis_Quality }
  | { route: RouteLocation.Analysis_ToolReplacements }
  | { route: RouteLocation.Analysis_MachineCycles }
  | { route: RouteLocation.Analysis_LoadCycles }
  | { route: RouteLocation.Analysis_PalletCycles }
  | { route: RouteLocation.Analysis_CostPerPiece }
  | { route: RouteLocation.Analysis_Schedules }
  | { route: RouteLocation.Analysis_CostPercents }
  | { route: RouteLocation.VerboseLogging }
  | { route: RouteLocation.Client_Custom; custom: ReadonlyArray<string> };

function routeToUrl(route: RouteState): string {
  switch (route.route) {
    case RouteLocation.Station_LoadMonitor:
      if (route.queues.length > 0 || route.completed) {
        const params = new URLSearchParams();
        for (const q of route.queues) {
          params.append("queue", q);
        }
        if (route.completed) {
          params.append("completed", "t");
        }
        return `/station/loadunload/${route.loadNum}?${params.toString()}`;
      } else {
        return `/station/loadunload/${route.loadNum}`;
      }

    case RouteLocation.Station_InspectionMonitorWithType:
      return `/station/inspection/${encodeURIComponent(route.inspType)}`;

    case RouteLocation.Station_Queues:
      if (route.queues.length > 0) {
        const params = new URLSearchParams();
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
            queues: url.searchParams.getAll("queue"),
            completed: url.searchParams.has("completed"),
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

const currentRouteLocation = atom<RouteState>(urlToRoute(new URL(location.href)));
export const isDemoAtom = atom<boolean>(false);

export const currentRoute = atom(
  (get) => get(currentRouteLocation),
  (get, set, to: RouteState) => {
    const demo = get(isDemoAtom);
    set(currentRouteLocation, to);
    set(materialDialogOpen, null);
    if (!demo) {
      history.pushState(null, "", routeToUrl(to));
    }
  }
);

export function useWatchHistory(): void {
  const isDemo = useAtomValue(isDemoAtom);
  const setCurRoute = useSetAtom(currentRouteLocation);

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

export function useIsDemo(): boolean {
  return useAtomValue(isDemoAtom);
}

export function useSetTitle(title: string): void {
  const demo = useIsDemo();
  useEffect(() => {
    document.title = title + " - FMS Insight";
  }, [demo, title]);
}

export function helpUrl(r: RouteState): string {
  switch (r.route) {
    case RouteLocation.Station_LoadMonitor:
    case RouteLocation.Station_InspectionMonitor:
    case RouteLocation.Station_InspectionMonitorWithType:
    case RouteLocation.Station_Closeout:
    case RouteLocation.Station_Queues:
    case RouteLocation.Station_Overview:
      return "https://www.seedtactics.com/docs/fms-insight/client-station-monitor";

    case RouteLocation.Operations_Dashboard:
    case RouteLocation.Operations_LoadOutliers:
    case RouteLocation.Operations_LoadHours:
    case RouteLocation.Operations_LoadCycles:
    case RouteLocation.Operations_MachineOutliers:
    case RouteLocation.Operations_MachineHours:
    case RouteLocation.Operations_MachineCycles:
    case RouteLocation.Operations_SystemOverview:
    case RouteLocation.Operations_AllMaterial:
    case RouteLocation.Operations_RecentSchedules:
    case RouteLocation.Operations_CurrentWorkorders:
    case RouteLocation.Operations_Production:
    case RouteLocation.Operations_Quality:
    case RouteLocation.Operations_Inspections:
      return "https://www.seedtactics.com/docs/fms-insight/client-operations";

    case RouteLocation.Operations_Tools:
    case RouteLocation.Operations_Programs:
      return "https://www.seedtactics.com/docs/fms-insight/client-tools-programs";

    case RouteLocation.Engineering_Cycles:
    case RouteLocation.Engineering_Outliers:
    case RouteLocation.Engineering_Hours:
      return "https://www.seedtactics.com/docs/fms-insight/client-engineering";

    case RouteLocation.Quality_Dashboard:
    case RouteLocation.Quality_Paths:
    case RouteLocation.Quality_Quarantine:
      return "https://www.seedtactics.com/docs/fms-insight/client-tools-programs";

    case RouteLocation.Tools_Dashboard:
    case RouteLocation.Tools_Programs:
      return "https://www.seedtactics.com/docs/fms-insight/client-operations";

    case RouteLocation.Analysis_Buffers:
    case RouteLocation.Analysis_StationOEE:
    case RouteLocation.Analysis_PartsCompleted:
    case RouteLocation.Analysis_MachineCycles:
    case RouteLocation.Analysis_LoadCycles:
    case RouteLocation.Analysis_PalletCycles:
    case RouteLocation.Analysis_Quality:
    case RouteLocation.Analysis_CostPercents:
    case RouteLocation.Analysis_ToolReplacements:
      return "https://www.seedtactics.com/docs/fms-insight/client-flexibility-analysis";

    case RouteLocation.Analysis_CostPerPiece:
      return "https://www.seedtactics.com/docs/fms-insight/client-cost-per-piece";

    case RouteLocation.ChooseMode:
    case RouteLocation.VerboseLogging:
    case RouteLocation.Client_Custom:
    default:
      return "https://www.seedtactics.com/docs/fms-insight/client-launch";
  }
}
