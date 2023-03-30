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
import * as React from "react";
import { Paper } from "@mui/material";
import { List } from "@mui/material";
import { ListItem } from "@mui/material";
import { ListItemIcon } from "@mui/material";
import { ListSubheader } from "@mui/material";
import { ListItemText } from "@mui/material";
import { Typography } from "@mui/material";

import {
  Build as BuildIcon,
  ShoppingBasket,
  Directions as DirectionsIcon,
  StarRate as StarIcon,
  InsertChart as ChartIcon,
  Extension as ExtensionIcon,
  Info as InfoIcon,
  Opacity as OpacityIcon,
  Memory as MemoryIcon,
  CalendarViewMonth,
} from "@mui/icons-material";

import { RouteState, RouteLocation } from "./routes.js";

export type ChooseModeItem =
  | { readonly type: "Subheader"; readonly caption: string }
  | { readonly type: "Link"; route: RouteState; readonly icon: JSX.Element; readonly label: string };

export const defaultChooseModes: ReadonlyArray<ChooseModeItem> = [
  { type: "Subheader", caption: "Shop Floor" },
  {
    type: "Link",
    route: {
      route: RouteLocation.Station_LoadMonitor,
      loadNum: 1,
      queues: [],
      completed: false,
    },
    icon: <DirectionsIcon />,
    label: "Load Station",
  },
  {
    type: "Link",
    route: {
      route: RouteLocation.Station_Queues,
      queues: [],
    },
    icon: <ExtensionIcon />,
    label: "Queue Management",
  },
  {
    type: "Link",
    route: {
      route: RouteLocation.Station_InspectionMonitor,
    },
    icon: <InfoIcon />,
    label: "Inspection Stand",
  },
  {
    type: "Link",
    route: {
      route: RouteLocation.Station_WashMonitor,
    },
    icon: <OpacityIcon />,
    label: "Wash",
  },
  {
    type: "Link",
    route: {
      route: RouteLocation.Tools_Dashboard,
    },
    icon: <BuildIcon />,
    label: "Tool Management",
  },
  {
    type: "Link",
    route: {
      route: RouteLocation.Station_Overview,
    },
    icon: <CalendarViewMonth />,
    label: "System Overview",
  },
  {
    type: "Subheader",
    caption: "Daily Monitoring",
  },
  {
    type: "Link",
    route: { route: RouteLocation.Operations_Dashboard },
    icon: <ShoppingBasket />,
    label: "Operation Management",
  },
  {
    type: "Link",
    route: { route: RouteLocation.Engineering },
    icon: <MemoryIcon />,
    label: "Engineering",
  },
  {
    type: "Link",
    route: { route: RouteLocation.Quality_Dashboard },
    icon: <StarIcon />,
    label: "Quality Analysis",
  },
  { type: "Subheader", caption: "Monthly Review" },
  {
    type: "Link",
    route: { route: RouteLocation.Analysis_Efficiency },
    icon: <ChartIcon />,
    label: "Flexibility Analysis",
  },
];

export interface ChooseModeProps {
  readonly setRoute: (r: RouteState) => void;
  readonly modes?: ReadonlyArray<ChooseModeItem> | null;
}

export function ChooseMode(p: ChooseModeProps): JSX.Element {
  const navList = (
    <Paper>
      <List component="nav">
        {(p.modes ?? defaultChooseModes).map((mode, idx) =>
          mode.type === "Subheader" ? (
            <ListSubheader key={idx}>{mode.caption}</ListSubheader>
          ) : (
            <ListItem key={idx} button onClick={() => p.setRoute(mode.route)}>
              <ListItemIcon>{mode.icon}</ListItemIcon>
              <ListItemText>{mode.label}</ListItemText>
            </ListItem>
          )
        )}
      </List>
    </Paper>
  );

  return (
    <main style={{ display: "flex", justifyContent: "center" }}>
      <div>
        <div style={{ textAlign: "center" }}>
          <Typography variant="h4" style={{ marginTop: "2em" }}>
            Select user and computer location
          </Typography>
          <Typography
            variant="caption"
            style={{ marginBottom: "2em", maxWidth: "30em", marginLeft: "auto", marginRight: "auto" }}
          >
            We recommend that after selecting one of the following modes, you bookmark the page and visit it
            directly.
            <a href="https://fms-insight.seedtactics.com/docs/client-dashboard.html">Learn More</a>
          </Typography>
        </div>
        {navList}
      </div>
    </main>
  );
}

export default ChooseMode;
