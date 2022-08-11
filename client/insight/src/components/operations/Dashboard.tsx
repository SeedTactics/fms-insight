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
import * as React from "react";
import { Box, LinearProgress, Typography } from "@mui/material";
import { useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { IProcPathInfo } from "../../network/api.js";
import { ParentSize } from "@visx/responsive";
import { RecentCycleChart } from "./RecentCycleChart.js";

const pctFormat = new Intl.NumberFormat(undefined, { style: "percent", minimumFractionDigits: 1 });

function countExpectedAtNow(): (p: Readonly<IProcPathInfo>) => number {
  const now = new Date();
  return (p: Readonly<IProcPathInfo>) => {
    if (!p.simulatedProduction) return 0;
    let lastCnt = 0;
    for (const sim of p.simulatedProduction) {
      if (sim.timeUTC > now) break;
      lastCnt = sim.quantity;
    }
    return lastCnt;
  };
}

const CompletedParts = React.memo(function CompletedParts() {
  const currentSt = useRecoilValue(currentStatus);

  const completed = LazySeq.ofObject(currentSt.jobs).sumBy(([, j]) =>
    LazySeq.of(j.completed ?? []).sumBy((c) => LazySeq.of(c).sumBy((d) => d))
  );
  const simulated = LazySeq.ofObject(currentSt.jobs)
    .flatMap(([, j]) => j.procsAndPaths)
    .flatMap((p) => p.paths)
    .sumBy(countExpectedAtNow());
  const planned = LazySeq.ofObject(currentSt.jobs).sumBy(([, j]) => (j.cycles ?? 0) * j.procsAndPaths.length);

  return (
    <Box sx={{ maxWidth: "60em", ml: "auto", mr: "auto", pt: "1em" }}>
      <Box
        sx={{
          display: "grid",
          gridTemplateColumns: "auto 1fr auto",
          gridColumnGap: "0.5em",
          alignItems: "center",
        }}
      >
        <Box sx={{ gridRow: 1, gridColumn: 1 }}>
          <Typography variant="body2">
            Completed Parts: {completed}/{planned}
          </Typography>
        </Box>
        <Box sx={{ gridRow: 1, gridColumn: 2 }}>
          <LinearProgress variant="determinate" value={(completed / planned) * 100} color="secondary" />
        </Box>
        <Box sx={{ gridRow: 1, gridColumn: 3 }}>
          <Typography variant="body2">{pctFormat.format(completed / planned)}</Typography>
        </Box>
        <Box sx={{ gridRow: 2, gridColumn: 1 }}>
          <Typography variant="body2">
            Simulated Parts: {simulated}/{planned}
          </Typography>
        </Box>
        <Box sx={{ gridRow: 2, gridColumn: 2 }}>
          <LinearProgress variant="determinate" value={(simulated / planned) * 100} color="secondary" />
        </Box>
        <Box sx={{ gridRow: 2, gridColumn: 3 }}>
          <Typography variant="body2">{pctFormat.format(simulated / planned)}</Typography>
        </Box>
      </Box>
    </Box>
  );
});

function FillViewportDashboard() {
  return (
    <main style={{ height: "calc(100vh - 64px)", display: "flex", flexDirection: "column" }}>
      <div>
        <CompletedParts />
      </div>
      <div style={{ flexGrow: 1, overflow: "hidden", margin: "8px" }}>
        <ParentSize debounceTime={10}>
          {({ width, height }) => <RecentCycleChart width={width} height={height} />}
        </ParentSize>
      </div>
    </main>
  );
}

export function ScrollableDashboard() {
  return (
    <main style={{ padding: "8px" }}>
      <CompletedParts />
      <div style={{ overflow: "hidden" }}>
        <ParentSize ignoreDimensions={["height", "top"]}>
          {({ width }) => <RecentCycleChart width={width} height={500} />}
        </ParentSize>
      </div>
    </main>
  );
}

export default function Dashboard() {
  React.useEffect(() => {
    document.title = "Dashboard - FMS Insight";
  }, []);
  return (
    <div>
      <Box sx={{ display: { xs: "none", lg: "block" } }}>
        <FillViewportDashboard />
      </Box>
      <Box sx={{ display: { xs: "block", lg: "none" } }}>
        <ScrollableDashboard />
      </Box>
    </div>
  );
}
