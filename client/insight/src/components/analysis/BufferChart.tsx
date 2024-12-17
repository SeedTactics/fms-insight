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

import { ComponentType, ChangeEvent, memo, useState, useMemo } from "react";
import { addDays, startOfToday, addMonths } from "date-fns";
import { curveCatmullRom } from "@visx/curve";
import { XYChart, AnimatedAxis, AnimatedLineSeries, Grid } from "@visx/xychart";
import { BufferChartPoint, buildBufferChart } from "../../data/results.bufferchart.js";
import { chartTheme, seriesColor } from "../../util/chart-colors.js";
import { rawMaterialQueues } from "../../cell-status/names.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { last30BufferEntries, specificMonthBufferEntries } from "../../cell-status/buffers.js";
import { Box, ToggleButton, Slider, Typography } from "@mui/material";
import { useSetTitle } from "../routes.js";
import { useAtomValue } from "jotai";
import { HashSet } from "@seedtactics/immutable-collections";

type BufferChartProps = {
  readonly movingAverageDistanceInHours: number;
};

const BufferChart = memo(function BufferChart(props: BufferChartProps) {
  const period = useAtomValue(selectedAnalysisPeriod);
  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];
  const entries = useAtomValue(period.type === "Last30" ? last30BufferEntries : specificMonthBufferEntries);
  const rawMatQueues = useAtomValue(rawMaterialQueues);

  const [disabledBuffers, setDisabledBuffers] = useState(HashSet.empty<string>());

  const defaultDateRangeStart = defaultDateRange[0];
  const defaultDateRangeEnd = defaultDateRange[1];
  const series = useMemo(
    () =>
      buildBufferChart(
        defaultDateRangeStart,
        defaultDateRangeEnd,
        props.movingAverageDistanceInHours,
        rawMatQueues,
        entries.valuesToLazySeq(),
      ),
    [defaultDateRangeStart, defaultDateRangeEnd, entries, props.movingAverageDistanceInHours, rawMatQueues],
  );

  const emptySeries = series.findIndex((s) => !disabledBuffers.has(s.label)) < 0;

  return (
    <div>
      <Box
        sx={{ height: { xs: "calc(100vh - 350px)", md: "calc(100vh - 285px)", xl: "calc(100vh - 220px)" } }}
      >
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
            ),
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
              setDisabledBuffers((db) => (db.has(s.label) ? db.delete(s.label) : db.add(s.label)))
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
const SliderAny: ComponentType<any> = Slider;

export function BufferOccupancyChart() {
  useSetTitle("Buffer Occupancy");

  const [movingAverageHours, setMovingAverage] = useState(12);
  return (
    <Box paddingLeft="24px" paddingRight="24px" paddingTop="10px">
      <Box
        component="nav"
        sx={{
          display: "flex",
          minHeight: "2.5em",
          alignItems: "center",
          maxWidth: "calc(100vw - 24px - 24px)",
        }}
      >
        <Typography variant="subtitle1">
          Average material occupancy of rotary tables and stocker positions over time
        </Typography>
        <Box flexGrow={1} />
        <span style={{ fontSize: "small", marginRight: "1em" }}>Moving Average Window: </span>
        <SliderAny
          style={{ width: "10em" }}
          min={1}
          max={36}
          steps={0.2}
          valueLabelDisplay="off"
          value={movingAverageHours}
          onChange={(e: ChangeEvent<unknown>, v: number) => setMovingAverage(v)}
        />
      </Box>

      <main>
        <BufferChart movingAverageDistanceInHours={movingAverageHours} />
      </main>
    </Box>
  );
}
