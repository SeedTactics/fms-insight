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
import Grid from "@material-ui/core/Grid";
import Card from "@material-ui/core/Card";
import CardContent from "@material-ui/core/CardContent";
import Hidden from "@material-ui/core/Hidden";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

import StationOEEs from "./OEESummary";
import CurrentJobs from "./CurrentJobs";

function FillViewportDashboard() {
  return (
    <main style={{ height: "calc(100vh - 64px)", display: "flex" }}>
      <div
        style={{
          flexBasis: "50%",
          padding: "8px",
          display: "flex",
          flexDirection: "column"
        }}
      >
        <Card style={{ flexGrow: 1, display: "flex", flexDirection: "column" }}>
          <CurrentJobs fillViewport={true} />
        </Card>
      </div>
      <div
        style={{
          flexBasis: "50%",
          padding: "8px",
          display: "flex",
          flexDirection: "column"
        }}
      >
        <Card style={{ flexGrow: 1 }}>
          <CardContent style={{ overflow: "auto" }}>
            <StationOEEs />
          </CardContent>
        </Card>
      </div>
    </main>
  );
}

function ScrollableDashboard() {
  return (
    <main style={{ padding: "8px" }}>
      <Grid container spacing={2}>
        <Grid item xs={12} sm={6}>
          <Card>
            <CardContent>
              <CurrentJobs fillViewport={false} />
            </CardContent>
          </Card>
        </Grid>
        <Grid item xs={12} sm={6}>
          <Card>
            <CardContent>
              <StationOEEs />
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </main>
  );
}

export default function Dashboard() {
  return (
    <DocumentTitle title="Dashboard - FMS Insight">
      <div>
        <Hidden mdDown>
          <FillViewportDashboard />
        </Hidden>
        <Hidden lgUp>
          <ScrollableDashboard />
        </Hidden>
      </div>
    </DocumentTitle>
  );
}
