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
import { addDays, startOfToday, addMonths } from "date-fns";
import { buildBufferChart } from "../../data/results.bufferchart";
import { seriesColor } from "./CycleChart";
import { HashSet } from "prelude-ts";
import { useRecoilValue } from "recoil";
import { rawMaterialQueues } from "../../cell-status/names";
import { selectedAnalysisPeriod } from "../../network/load-specific-month";
import { last30BufferEntries, specificMonthBufferEntries } from "../../cell-status/buffers";

export interface BufferChartProps {
  readonly movingAverageDistanceInHours: number;
}

export const BufferChart = React.memo(function BufferChart(props: BufferChartProps) {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];
  const entries = useRecoilValue(period.type === "Last30" ? last30BufferEntries : specificMonthBufferEntries);
  const rawMatQueues = useRecoilValue(rawMaterialQueues);

  const [disabledBuffers, setDisabledBuffers] = React.useState<HashSet<string>>(HashSet.empty());

  const series = React.useMemo(
    () =>
      buildBufferChart(
        defaultDateRange[0],
        defaultDateRange[1],
        props.movingAverageDistanceInHours,
        rawMatQueues,
        entries
      ),
    [defaultDateRange[0], defaultDateRange[1], entries, props.movingAverageDistanceInHours]
  );

  const [chartHeight, setChartHeight] = React.useState(500);
  React.useEffect(() => {
    setChartHeight(window.innerHeight - 200);
  }, []);

  const emptySeries = series.findIndex((s) => !disabledBuffers.contains(s.label)) < 0;

  return (
    <div>
      <FlexibleWidthXYPlot
        height={chartHeight}
        animation
        xType="time"
        margin={{ bottom: 50 }}
        dontCheckIfEmpty
        xDomain={defaultDateRange}
        yDomain={emptySeries ? [0, 10] : undefined}
      >
        <VerticalGridLines />
        <HorizontalGridLines />
        <XAxis tickLabelAngle={-45} />
        <YAxis />
        {series.map((s, idx) =>
          disabledBuffers.contains(s.label) ? undefined : (
            <LineSeries key={s.label} data={s.points} curve="curveCatmullRom" color={seriesColor(idx, series.length)} />
          )
        )}
      </FlexibleWidthXYPlot>
      <div style={{ textAlign: "center" }}>
        <DiscreteColorLegend
          orientation="horizontal"
          items={series.map((s, idx) => ({
            title: s.label,
            color: seriesColor(idx, series.length),
            disabled: disabledBuffers.contains(s.label),
          }))}
          onItemClick={({ title }: { title: string }) =>
            disabledBuffers.contains(title)
              ? setDisabledBuffers(disabledBuffers.remove(title))
              : setDisabledBuffers(disabledBuffers.add(title))
          }
        />
      </div>
    </div>
  );
});
