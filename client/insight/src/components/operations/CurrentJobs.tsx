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
import { grey } from "@mui/material/colors";
import { useRecoilValue } from "recoil";
import {
  AnimatedAxis,
  Grid,
  XYChart,
  Tooltip as VisxTooltip,
  AnimatedBarSeries,
  AnimatedGlyphSeries,
} from "@visx/xychart";
import { Stack } from "@mui/material";
import { defaultStyles as defaultTooltipStyles } from "@visx/tooltip";

import { chartTheme } from "../../util/chart-colors";
import { IndexedDataPoint, jobsToPoints } from "../../data/job-bullet";
import { currentStatus } from "../../cell-status/current-status";

export interface CurrentJobsProps {
  readonly fillViewport: boolean;
}

function JobTooltip({ job }: { readonly job: IndexedDataPoint | undefined }) {
  if (job === undefined) return null;
  return (
    <Stack direction="column" spacing={0.6}>
      <div>Part: {job.part}</div>
      <div>Completed: {job.completedCount}</div>
      <div>Planned: {job.totalCount}</div>
      <div>Remaining Time: {(job.totalPlan - job.completed).toFixed(1)} hours</div>
      {job.workorders !== "" ? <div>Workorders: {job.workorders}</div> : undefined}
    </Stack>
  );
}

function JobsChart({ fixedHeight }: { readonly fixedHeight?: boolean | undefined }) {
  const st = useRecoilValue(currentStatus);
  const points = React.useMemo(() => jobsToPoints(Object.values(st.jobs)), [st.jobs]);
  const marginLeft = Math.min(100, points.longestPartName > 6 ? (points.longestPartName - 6) * 6 + 60 : 60);
  return (
    <XYChart
      height={fixedHeight ? points.jobs.length * 40 : undefined}
      xScale={{ type: "linear" }}
      yScale={{ type: "band", padding: 0.2 }}
      horizontal
      margin={{ left: marginLeft, top: 50, right: 50, bottom: 50 }}
      theme={chartTheme}
    >
      <AnimatedAxis orientation="bottom" label="Machine Hours" />
      <AnimatedAxis orientation="left" tickFormat={(idx: number) => points.jobs[idx]?.part} />
      <Grid />

      <AnimatedBarSeries
        enableEvents
        dataKey="jobs"
        data={points.jobs as IndexedDataPoint[]}
        xAccessor={(p) => p.completed}
        yAccessor={(p) => p.idx}
        colorAccessor={() => "#795548"}
      />

      <AnimatedGlyphSeries
        data={points.jobs as IndexedDataPoint[]}
        dataKey="jobsCompleted"
        xAccessor={(p) => p.totalPlan}
        yAccessor={(p) => p.idx}
      />

      <VisxTooltip<IndexedDataPoint>
        renderTooltip={({ tooltipData }) => <JobTooltip job={tooltipData?.nearestDatum?.datum} />}
        style={{ ...defaultTooltipStyles, backgroundColor: grey[800], color: "white" }}
      />
    </XYChart>
  );
}

export const CurrentJobs = React.memo(function CurrentJobs(props: CurrentJobsProps) {
  if (props.fillViewport) {
    return (
      <div style={{ flexGrow: 1, display: "flex", flexDirection: "column" }}>
        <div style={{ flexGrow: 1, position: "relative" }}>
          <div style={{ position: "absolute", top: 0, left: 0, bottom: 0, right: 0 }}>
            <JobsChart />
          </div>
        </div>
      </div>
    );
  } else {
    return (
      <div>
        <JobsChart fixedHeight />
      </div>
    );
  }
});
