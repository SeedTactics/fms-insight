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
import * as React from "react";
import Paper from "@material-ui/core/Paper";
import List from "@material-ui/core/List";
import ListItem from "@material-ui/core/ListItem";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import ListSubheader from "@material-ui/core/ListSubheader";
import ListItemText from "@material-ui/core/ListItemText";
import Hidden from "@material-ui/core/Hidden";
import Typography from "@material-ui/core/Typography";
import BuildIcon from "@material-ui/icons/Build";
import ShoppingBasket from "@material-ui/icons/ShoppingBasket";
import DirectionsIcon from "@material-ui/icons/Directions";
import StarIcon from "@material-ui/icons/StarRate";
import ChartIcon from "@material-ui/icons/InsertChart";

import * as routes from "../data/routes";
import { connect } from "../store/store";

export interface ChooseModeProps {
  readonly setStations: () => void;
  readonly setOperations: () => void;
  readonly setQuality: () => void;
  readonly setAnalysis: () => void;
}

export function ChooseMode(p: ChooseModeProps) {
  const navList = (
    <Paper>
      <List component="nav">
        <ListSubheader>Shop Floor</ListSubheader>
        <ListItem button onClick={p.setStations}>
          <ListItemIcon>
            <DirectionsIcon />
          </ListItemIcon>
          <ListItemText>Station Monitor</ListItemText>
        </ListItem>
        <ListItem button disabled>
          <ListItemIcon>
            <BuildIcon />
          </ListItemIcon>
          <ListItemText>Tool Management</ListItemText>
        </ListItem>
        <ListSubheader>Daily Monitoring</ListSubheader>
        <ListItem button onClick={p.setOperations}>
          <ListItemIcon>
            <ShoppingBasket />
          </ListItemIcon>
          <ListItemText>Operation Management</ListItemText>
        </ListItem>
        <ListItem button onClick={p.setQuality}>
          <ListItemIcon>
            <StarIcon />
          </ListItemIcon>
          <ListItemText>Quality Review</ListItemText>
        </ListItem>
        <ListSubheader>Monthly Review</ListSubheader>
        <ListItem button onClick={p.setAnalysis}>
          <ListItemIcon>
            <ChartIcon />
          </ListItemIcon>
          <ListItemText>Efficiency Analysis</ListItemText>
        </ListItem>
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
            We recommend that after selecting one of the following modes, you bookmark the page and visit it directly.
            <a href="https://fms-insight.seedtactics.com/docs/client-dashboard.html">Learn More</a>
          </Typography>
        </div>
        {navList}
      </div>
    </main>
  );
}

export default connect(
  () => ({}),
  {
    setStations: () => ({
      type: routes.RouteLocation.Station_LoadMonitor,
      payload: { num: 1 }
    }),
    setOperations: () => ({
      type: routes.RouteLocation.Operations_Dashboard
    }),
    setQuality: () => ({
      type: routes.RouteLocation.Quality_Dashboard
    }),
    setAnalysis: () => ({
      type: routes.RouteLocation.Analysis_Efficiency
    })
  }
)(ChooseMode);
