/* Copyright (c) 2025, John Lenz

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

import { ComponentType, ChangeEvent, memo, useState, useMemo, useRef, useCallback } from "react";
import { addDays, startOfToday, addMonths } from "date-fns";
import { curveCatmullRom, line } from "d3-shape";
import { BufferChartPoint, BufferChartSeries, buildBufferChart } from "../../data/results.bufferchart.js";
import { seriesColor } from "../../util/chart-colors.js";
import { rawMaterialQueues } from "../../cell-status/names.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { last30BufferEntries, specificMonthBufferEntries } from "../../cell-status/buffers.js";
import { Box, ToggleButton, Slider, Typography, debounce } from "@mui/material";
import { useSetTitle } from "../routes.js";
import { useAtomValue } from "jotai";
import { HashSet, LazySeq } from "@seedtactics/immutable-collections";
import { scaleLinear, scaleTime } from "d3-scale";
import { AxisBottom, AxisLeft, ChartGrid } from "../AxisAndGrid.js";
import { useResizeDetector } from "react-resize-detector";
import { animated, useSpring } from "@react-spring/web";
import { interpolatePath } from "d3-interpolate-path";

const AnimatedPath = memo(function AnimatedPath({
  series,
  xScale,
  yScale,
  color,
}: {
  series: BufferChartSeries;
  xScale: (d: Date) => number;
  yScale: (d: number) => number;
  color: string;
}) {
  const d =
    line<BufferChartPoint>()
      .curve(curveCatmullRom)
      .x((p) => xScale(p.x))
      .y((p) => yScale(p.y))(series.points) ?? "";

  // don't update in quick succession
  const previous = useRef(d);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const setPrevious = useCallback(
    debounce((val: string) => {
      previous.current = val;
    }, 50),
    [],
  );

  const interpolate = interpolatePath(previous.current, d);
  setPrevious(d);

  const { t } = useSpring({
    from: { t: 0 },
    to: { t: 1 },
    reset: true,
    delay: 0,
  });

  return (
    <animated.path
      d={t.to(interpolate)}
      stroke={color}
      strokeWidth={2}
      strokeLinecap="round"
      fill="transparent"
    />
  );
});

const marginLeft = 60;
const marginBottom = 50;
const marginTop = 20;
const marginRight = 20;

function BufferChartSVG({
  width,
  height,
  series,
  disabled,
}: {
  width: number;
  height: number;
  series: ReadonlyArray<BufferChartSeries>;
  disabled: HashSet<string>;
}) {
  const [dateMin, dateMax] = useMemo(() => {
    let min: Date | null = null;
    let max: Date | null = null;
    for (const s of series) {
      for (const p of s.points) {
        if (min === null || p.x < min) {
          min = p.x;
        }
        if (max === null || p.x > max) {
          max = p.x;
        }
      }
    }
    return [min ?? new Date(), max ?? new Date()];
  }, [series]);

  const xScale = useMemo(
    () =>
      scaleTime()
        .range([0, width - marginLeft - marginRight])
        .domain([dateMin, dateMax]),
    [width, dateMin, dateMax],
  );

  const yMax = height - marginBottom - marginTop;

  const yScale = useMemo(
    () =>
      scaleLinear()
        .domain([
          0,
          LazySeq.of(series)
            .flatMap((s) => s.points)
            .map((p) => p.y)
            .maxBy((y) => y) ?? 1,
        ])
        .range([yMax, 0]),
    [series, yMax],
  );

  return (
    <svg width={width} height={height}>
      <g transform={`translate(${marginLeft},${marginTop})`}>
        <AxisBottom scale={xScale} top={yMax} />
        <AxisLeft scale={yScale} left={0} label="Buffer Size" />
        <ChartGrid xScale={xScale} yScale={yScale} width={width - marginLeft - marginRight} height={yMax} />
        {series.map((s, idx) =>
          disabled.has(s.label) ? undefined : (
            <AnimatedPath
              key={s.label}
              series={s}
              xScale={xScale}
              yScale={yScale}
              color={seriesColor(idx, series.length)}
            />
          ),
        )}
      </g>
    </svg>
  );
}

const BufferChart = memo(function BufferChart(props: { movingAverageDistanceInHours: number }) {
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

  const {
    width,
    height,
    ref: chartRef,
  } = useResizeDetector({
    refreshMode: "debounce",
    refreshRate: 100,
  });

  return (
    <div>
      <Box
        ref={chartRef}
        sx={{
          height: { xs: "calc(100vh - 350px)", md: "calc(100vh - 285px)", xl: "calc(100vh - 220px)" },
          overflow: "hidden",
        }}
      >
        {width && height && width > 0 && height > 0 && (
          <BufferChartSVG width={width} height={height} series={series} disabled={disabledBuffers} />
        )}
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
