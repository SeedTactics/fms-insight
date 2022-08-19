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
import { addDays, startOfToday, addMonths } from "date-fns";
import { curveCatmullRom } from "@visx/curve";
import { XYChart, AnimatedAxis, AnimatedLineSeries, Grid } from "@visx/xychart";
import { BufferChartPoint, buildBufferChart } from "../../data/results.bufferchart.js";
import { chartTheme, seriesColor } from "../../util/chart-colors.js";
import { useRecoilValue } from "recoil";
import { rawMaterialQueues } from "../../cell-status/names.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { last30BufferEntries, specificMonthBufferEntries } from "../../cell-status/buffers.js";
import { Box, ToggleButton, Card, CardContent, CardHeader, Slider } from "@mui/material";
import { DonutSmall as DonutIcon } from "@mui/icons-material";
import { useImmer } from "../../util/recoil-util.js";

type BufferChartProps = {
  readonly movingAverageDistanceInHours: number;
};

const BufferChart = React.memo(function BufferChart(props: BufferChartProps) {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];
  const entries = useRecoilValue(period.type === "Last30" ? last30BufferEntries : specificMonthBufferEntries);
  const rawMatQueues = useRecoilValue(rawMaterialQueues);

  const [disabledBuffers, setDisabledBuffers] = useImmer<ReadonlySet<string>>(new Set<string>());

  const series = React.useMemo(
    () =>
      buildBufferChart(
        defaultDateRange[0],
        defaultDateRange[1],
        props.movingAverageDistanceInHours,
        rawMatQueues,
        entries.valuesToLazySeq()
      ),
    [defaultDateRange[0], defaultDateRange[1], entries, props.movingAverageDistanceInHours]
  );

  const emptySeries = series.findIndex((s) => !disabledBuffers.has(s.label)) < 0;

  return (
    <div>
      <Box sx={{ height: "calc(100vh - 220px)" }}>
        <XYChart xScale={{ type: "time" }} yScale={{ type: "linear" }} theme={chartTheme}>
          <AnimatedAxis orientation="bottom" />
          <AnimatedAxis orientation="left" label="Buffer Size" />
          <Grid />
          {series.map((s, idx) =>
            disabledBuffers.has(s.label) ? undefined : (
              <AnimatedLineSeries
                key={s.label}
                dataKey={s.label}
                data={s.points as BufferChartPoint[]}
                stroke={seriesColor(idx, series.length)}
                curve={curveCatmullRom}
                xAccessor={(p) => p.x}
                yAccessor={(p) => p.y}
              />
            )
          )}
          {emptySeries ? (
            <AnimatedLineSeries
              dataKey="__emptyInvisibleSeries"
              stroke="transparent"
              data={defaultDateRange}
              xAccessor={(p) => p}
              yAccessor={(_) => 1}
            />
          ) : undefined}
        </XYChart>
      </Box>
      <div style={{ marginTop: "1em", display: "flex", flexWrap: "wrap", justifyContent: "space-around" }}>
        {series.map((s, idx) => (
          <ToggleButton
            key={s.label}
            selected={!disabledBuffers.has(s.label)}
            value={s.label}
            onChange={() =>
              setDisabledBuffers((db) => {
                if (db.has(s.label)) {
                  db.delete(s.label);
                } else {
                  db.add(s.label);
                }
              })
            }
          >
            <div style={{ display: "flex", alignItems: "center" }}>
              <div
                style={{ width: "14px", height: "14px", backgroundColor: seriesColor(idx, series.length) }}
              />
              <div style={{ marginLeft: "1em" }}>{s.label}</div>
            </div>
          </ToggleButton>
        ))}
      </div>
    </div>
  );
});

// https://github.com/mui-org/material-ui/issues/20191
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const SliderAny: React.ComponentType<any> = Slider;

export function BufferOccupancyChart() {
  const [movingAverageHours, setMovingAverage] = React.useState(12);
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <DonutIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Buffer Occupancy</div>
            <div style={{ flexGrow: 1 }} />
            <span style={{ fontSize: "small", marginRight: "1em" }}>Moving Average Window: </span>
            <SliderAny
              style={{ width: "10em" }}
              min={1}
              max={36}
              steps={0.2}
              valueLabelDisplay="off"
              value={movingAverageHours}
              onChange={(e: React.ChangeEvent<unknown>, v: number) => setMovingAverage(v)}
            />
          </div>
        }
      />
      <CardContent>
        <BufferChart movingAverageDistanceInHours={movingAverageHours} />
      </CardContent>
    </Card>
  );
}
