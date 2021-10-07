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

import {
  FlexibleWidthXYPlot,
  FlexibleXYPlot,
  HorizontalBarSeries,
  XAxis,
  YAxis,
  CustomSVGSeries,
  VerticalGridLines,
  HorizontalGridLines,
  Hint,
} from "react-vis";

import { CompletedDataPoint, jobsToPoints } from "../../data/job-bullet";
import { currentStatus } from "../../cell-status/current-status";
import { useRecoilValue } from "recoil";

// --------------------------------------------------------------------------------
// Data
// --------------------------------------------------------------------------------

// --------------------------------------------------------------------------------
// Plot
// --------------------------------------------------------------------------------

interface PlotProps {
  readonly cnt: number;
  readonly longestPartName: number;
  readonly children: (JSX.Element | undefined)[];
}

function FillViewportPlot({ longestPartName, children }: PlotProps) {
  const margin = Math.min(100, longestPartName > 6 ? (longestPartName - 6) * 6 + 60 : 60);
  return (
    <div style={{ flexGrow: 1, display: "flex", flexDirection: "column" }}>
      <div style={{ flexGrow: 1, position: "relative" }}>
        <div style={{ position: "absolute", top: 0, left: 0, bottom: 0, right: 0 }}>
          <FlexibleXYPlot margin={{ left: margin, right: 10, top: 10, bottom: 40 }} yType="ordinal">
            {children}
          </FlexibleXYPlot>
        </div>
      </div>
      <div style={{ textAlign: "center", marginBottom: "8px", color: "#6b6b76" }}>Machine Hours</div>
    </div>
  );
}

function ScrollablePlot({ children, cnt, longestPartName }: PlotProps) {
  const margin = Math.min(100, longestPartName > 6 ? (longestPartName - 6) * 6 + 60 : 60);
  return (
    <div>
      <FlexibleWidthXYPlot height={cnt * 40} margin={{ left: margin, right: 10, top: 10, bottom: 40 }} yType="ordinal">
        {children}
      </FlexibleWidthXYPlot>
      <div style={{ textAlign: "center" }}>Machine Hours</div>
    </div>
  );
}

const targetMark = () => <rect x="-2" y="-5" width="4" height="10" fill="black" />;

interface CurrentJobsProps {
  readonly fillViewport: boolean;
}

function format_hint(
  j: CompletedDataPoint
): ReadonlyArray<{ readonly title: string; readonly value: string | number }> {
  const vals = [
    { title: "Part", value: j.part },
    { title: "Completed", value: j.completedCount },
    { title: "Planned", value: j.totalCount },
    {
      title: "Remaining Time",
      value: (j.totalPlan - j.completed).toFixed(1) + " hours",
    },
  ];
  if (j.workorders !== "") {
    vals.push({ title: "Workorders", value: j.workorders });
  }
  return vals;
}

function format_tick(p: CompletedDataPoint) {
  return (
    <tspan>
      <tspan>{p.part}</tspan>
      <tspan x={0} dy="1.2em">
        {p.totalCount > 100 ? `${p.completedCount}/${p.totalCount}` : `${p.completedCount} / ${p.totalCount}`}
      </tspan>
    </tspan>
  );
}

export const CurrentJobs = React.memo(function CurrentJobs(props: CurrentJobsProps) {
  const [hoveredJob, setHoveredJob] = React.useState<CompletedDataPoint | null>(null);
  const st = useRecoilValue(currentStatus);
  const points = React.useMemo(() => jobsToPoints(Object.values(st.jobs)), [st.jobs]);

  const Plot = props.fillViewport ? FillViewportPlot : ScrollablePlot;
  return (
    <Plot cnt={points.completedData.length} longestPartName={points.longestPartName}>
      <XAxis />
      <YAxis tickFormat={(y: number, i: number) => format_tick(points.completedData[i])} />
      <HorizontalGridLines />
      <VerticalGridLines />
      <HorizontalBarSeries
        data={points.completedData}
        color="#795548"
        onValueMouseOver={setHoveredJob}
        onValueMouseOut={() => setHoveredJob(null)}
      />
      <CustomSVGSeries
        data={points.planData}
        customComponent={targetMark}
        onValueMouseOver={(p: { x: number; y: number }) => setHoveredJob({ ...points.completedData[p.y], x: p.x })}
        onValueMouseOut={() => setHoveredJob(null)}
      />
      {hoveredJob === null ? undefined : <Hint value={hoveredJob} format={format_hint} />}
    </Plot>
  );
});
