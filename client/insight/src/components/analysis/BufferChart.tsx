/* Copyright (c) 2020, John Lenz

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
  VerticalGridLines,
  HorizontalGridLines,
  XAxis,
  YAxis,
  LineSeries,
  DiscreteColorLegend,
} from "react-vis";
import { useSelector } from "../../store/store";
import { AnalysisPeriod } from "../../data/events";
import { addDays, startOfToday, addMonths } from "date-fns";
import { buildBufferChart } from "../../data/results.bufferchart";
import { seriesColor } from "./CycleChart";

export interface BufferChartProps {
  readonly movingAverageDistanceInHours: number;
}

export const BufferChart = React.memo(function BufferChart(props: BufferChartProps) {
  const analysisPeriod = useSelector((s) => s.Events.analysis_period);
  const analysisPeriodMonth = useSelector((s) => s.Events.analysis_period_month);
  const defaultDateRange =
    analysisPeriod === AnalysisPeriod.Last30Days
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [analysisPeriodMonth, addMonths(analysisPeriodMonth, 1)];
  const entries = useSelector((s) =>
    s.Events.analysis_period === AnalysisPeriod.Last30Days
      ? s.Events.last30.buffering.entries
      : s.Events.selected_month.buffering.entries
  );
  const series = React.useMemo(
    () => buildBufferChart(defaultDateRange[0], defaultDateRange[1], props.movingAverageDistanceInHours, entries),
    [defaultDateRange[0], defaultDateRange[1], entries, props.movingAverageDistanceInHours]
  );
  return (
    <div>
      <FlexibleWidthXYPlot
        height={window.innerHeight - 200}
        animation
        xType="time"
        margin={{ bottom: 50 }}
        dontCheckIfEmpty
        xDomain={defaultDateRange}
        yDomain={series.length === 0 ? [0, 10] : undefined}
      >
        <VerticalGridLines />
        <HorizontalGridLines />
        <XAxis tickLabelAngle={-45} />
        <YAxis />
        {series.map((s, idx) => (
          <LineSeries key={s.label} data={s.points} curve="curveCatmullRom" color={seriesColor(idx, series.length)} />
        ))}
      </FlexibleWidthXYPlot>
      <div style={{ textAlign: "center" }}>
        <DiscreteColorLegend
          orientation="horizontal"
          items={series.map((s, idx) => ({
            title: s.label,
            color: seriesColor(idx, series.length),
          }))}
        />
      </div>
    </div>
  );
});
