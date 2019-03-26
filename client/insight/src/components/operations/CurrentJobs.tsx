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
import { createSelector } from "reselect";

import { connect, Store } from "../../store/store";
import {
  FlexibleWidthXYPlot,
  FlexibleXYPlot,
  HorizontalBarSeries,
  XAxis,
  YAxis,
  CustomSVGSeries,
  VerticalGridLines,
  HorizontalGridLines,
  Hint
} from "react-vis";

import { CompletedDataPoint, jobsToPoints, DataPoints } from "../../data/job-bullet";

// --------------------------------------------------------------------------------
// Data
// --------------------------------------------------------------------------------

// --------------------------------------------------------------------------------
// Plot
// --------------------------------------------------------------------------------

interface PlotProps {
  readonly cnt: number;
  readonly children: (JSX.Element | undefined)[];
}

function FillViewportPlot({ children }: PlotProps) {
  return (
    <div style={{ flexGrow: 1, display: "flex", flexDirection: "column" }}>
      <div style={{ flexGrow: 1, position: "relative" }}>
        <div style={{ position: "absolute", top: 0, left: 0, bottom: 0, right: 0 }}>
          <FlexibleXYPlot margin={{ left: 70, right: 10, top: 10, bottom: 40 }} yType="ordinal">
            {children}
          </FlexibleXYPlot>
        </div>
      </div>
      <div style={{ textAlign: "center", marginBottom: "8px", color: "#6b6b76" }}>Machine Hours</div>
    </div>
  );
}

function ScrollablePlot({ children, cnt }: PlotProps) {
  return (
    <div>
      <FlexibleWidthXYPlot height={cnt * 40} margin={{ left: 70, right: 10, top: 10, bottom: 40 }} yType="ordinal">
        {children}
      </FlexibleWidthXYPlot>
      <div style={{ textAlign: "center" }}>Machine Hours</div>
    </div>
  );
}

const targetMark = () => <rect x="-2" y="-5" width="4" height="10" fill="black" />;

interface CurrentJobsProps extends DataPoints {
  readonly fillViewport: boolean;
}

interface JobState {
  readonly hoveredJob?: CompletedDataPoint;
}

function format_hint(j: CompletedDataPoint) {
  return [
    { title: "Part", value: j.part },
    { title: "Completed", value: j.completedCount },
    { title: "Planned", value: j.totalCount },
    {
      title: "Remaining Time",
      value: (j.totalPlan - j.completed).toFixed(1) + " hours"
    }
  ];
}

function format_tick(p: CompletedDataPoint) {
  return (
    <tspan>
      <tspan>{p.part}</tspan>
      <tspan x={0} dy="1.2em">
        {p.completedCount} / {p.totalCount}
      </tspan>
    </tspan>
  );
}

class CurrentJobs extends React.PureComponent<CurrentJobsProps, JobState> {
  state: JobState = {};

  setHint = (j: CompletedDataPoint) => {
    this.setState({ hoveredJob: j });
  };

  clearHint = () => {
    this.setState({ hoveredJob: undefined });
  };

  render() {
    const Plot = this.props.fillViewport ? FillViewportPlot : ScrollablePlot;
    return (
      <Plot cnt={this.props.completedData.length}>
        <XAxis />
        <YAxis tickFormat={(y: number, i: number) => format_tick(this.props.completedData[i])} />
        <HorizontalGridLines />
        <VerticalGridLines />
        <HorizontalBarSeries
          data={this.props.completedData}
          color="#795548"
          onValueMouseOver={this.setHint}
          onValueMouseOut={this.clearHint}
        />
        <CustomSVGSeries
          data={this.props.planData}
          customComponent={targetMark}
          onValueMouseOver={(p: { x: number; y: number }) => this.setHint({ ...this.props.completedData[p.y], x: p.x })}
          onValueMouseOut={this.clearHint}
        />
        {this.state.hoveredJob === undefined ? undefined : <Hint value={this.state.hoveredJob} format={format_hint} />}
      </Plot>
    );
  }
}

const jobsToPointsSelector = createSelector(
  (s: Store) => s.Current.current_status.jobs,
  js => jobsToPoints(Object.values(js))
);

export default connect(s => {
  return jobsToPointsSelector(s);
})(CurrentJobs);
