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
import { PointerEvent, useMemo, memo, useRef, useCallback } from "react";
import { addDays } from "date-fns";
import { Select, MenuItem, Tooltip, IconButton, Stack, Box, Typography, FormControl } from "@mui/material";
import { ImportExport } from "@mui/icons-material";
import { ScaleBand, scaleBand, ScaleLinear, scaleLinear } from "d3-scale";

import { LazySeq } from "@seedtactics/immutable-collections";
import { localPoint } from "../../util/chart-helpers.js";
import { AxisBottom, AxisLeft } from "../AxisAndGrid.js";
import { measureSvgString } from "../../util/chart-helpers.js";
import { atom, useSetAtom } from "jotai";
import { ChartWithTooltip } from "../ChartTooltip.js";

export type HeatChartYType = "Station" | "Part";

export interface HeatChartPoint {
  readonly x: Date;
  readonly y: string;
  readonly color: number;
  readonly label: string;
}

export interface HeatChartProps {
  readonly points: ReadonlyArray<HeatChartPoint>;
  readonly y_title: HeatChartYType;
  readonly dateRange: [Date, Date];
  readonly label_title: string;
}

interface HeatChartDimensions {
  readonly height: number;
  readonly width: number;
  readonly marginLeft: number;
}

interface HeatChartScales {
  readonly xScale: ScaleBand<Date>;
  readonly yScale: ScaleBand<string>;
  readonly colorScale: ScaleLinear<string, string>;
}

const marginBottom = 20;
const marginTop = 10;
const marginRight = 2;
const color1 = "#E8F5E9";
const color2 = "#1B5E20";

function useScales({
  yType,
  dateRange,
  containerWidth,
  points,
}: {
  readonly yType: HeatChartYType;
  readonly points: ReadonlyArray<HeatChartPoint>;
  readonly dateRange: [Date, Date];
  readonly containerWidth: number | null | undefined;
}): HeatChartDimensions & HeatChartScales {
  const width =
    containerWidth === null || containerWidth === undefined || containerWidth === 0 ? 400 : containerWidth;

  const maxStatLen = useMemo(
    () =>
      LazySeq.of(points)
        .map((p) => p.y)
        .distinct()
        .map(measureSvgString)
        .maxBy((w) => w ?? 0) ?? 20,
    [points],
  );

  const marginLeft = maxStatLen + 5;

  const xMax = width - marginLeft - marginRight;

  const dateRangeStart = dateRange[0];
  const dateRangeEnd = dateRange[1];
  const xScale = useMemo(() => {
    const xValues: Array<Date> = [];
    if (dateRangeEnd < dateRangeStart) {
      // should never happen, just do something in case
      xValues.push(dateRangeStart);
    } else {
      let d = dateRangeStart;
      while (d < dateRangeEnd) {
        xValues.push(d);
        d = addDays(d, 1);
      }
    }
    return scaleBand<Date>().domain(xValues).range([0, xMax]).align(0).padding(0.05);
  }, [dateRangeStart, dateRangeEnd, xMax]);

  const { yScale, height, colorScale } = useMemo(() => {
    const yValues = new Set<string>();
    for (const pt of points) {
      yValues.add(pt.y);
    }

    const yMax = 60 * yValues.size;
    const height = yMax + marginTop + marginBottom;
    const yScale = scaleBand()
      .domain(Array.from(yValues).sort((a, b) => a.localeCompare(b)))
      .range([0, yMax])
      .align(0)
      .padding(0.05);
    let colorScale: ScaleLinear<string, string>;
    if (yType === "Station") {
      colorScale = scaleLinear<string, string>().domain([0, 1]).range([color1, color2]);
    } else {
      const maxCnt = LazySeq.of(points).maxBy((pt) => pt.color)?.color ?? 1;
      colorScale = scaleLinear<string, string>().domain([0, maxCnt]).range([color1, color2]);
    }

    return { yScale, height, colorScale };
  }, [points, yType]);

  return { height, width, xScale, yScale, colorScale, marginLeft };
}

const HeatAxis = memo(function HeatAxis({ xScale, yScale }: HeatChartScales) {
  return (
    <>
      <AxisBottom
        scale={xScale}
        top={yScale.range()[1]}
        tickFormat={(d) => d.toLocaleDateString(undefined, { month: "short", day: "numeric" })}
      />
      <AxisLeft scale={yScale} left={xScale.range()[0]} />
    </>
  );
});

interface TooltipData {
  readonly left: number;
  readonly top: number;
  readonly data: HeatChartPoint;
}

type ShowTooltipFunc = (a: TooltipData | null) => void;

const HeatSeries = memo(function HeatSeries({
  points,
  xScale,
  yScale,
  colorScale,
  setTooltip,
}: {
  readonly points: ReadonlyArray<HeatChartPoint>;
  readonly setTooltip: ShowTooltipFunc;
} & HeatChartScales) {
  const hideRef = useRef<number | null>(null);
  const pointerEnter = useCallback(
    (e: PointerEvent<SVGRectElement>) => {
      const pt = localPoint(e);
      if (pt === null) return;
      if (hideRef.current !== null) {
        clearTimeout(hideRef.current);
        hideRef.current = null;
      }
      const idxS = (e.target as SVGRectElement).dataset.idx;
      if (idxS === undefined) return;
      const heatPoint = points[parseInt(idxS)];
      setTooltip({ left: pt.x, top: pt.y, data: heatPoint });
    },
    [points, setTooltip],
  );

  const pointerLeave = useCallback(() => {
    if (hideRef.current === null) {
      hideRef.current = window.setTimeout(() => {
        hideRef.current = null;
        setTooltip(null);
      }, 100);
    }
  }, [setTooltip]);

  return (
    <g>
      {points.map((pt, idx) => {
        return (
          <rect
            key={idx}
            data-idx={idx}
            x={xScale(pt.x) ?? 0}
            width={xScale.bandwidth()}
            y={yScale(pt.y) ?? 0}
            height={yScale.bandwidth()}
            rx={6}
            fill={colorScale(pt.color)}
            onPointerEnter={pointerEnter}
            onPointerLeave={pointerLeave}
          />
        );
      })}
    </g>
  );
});

const HeatTooltip = memo(function HeatTooltip({
  yType,
  seriesLabel,
  tooltip,
}: {
  yType: HeatChartYType;
  seriesLabel: string;
  tooltip: TooltipData | null;
}) {
  if (tooltip === null) return null;
  return (
    <Stack direction="column" spacing={0.6}>
      <div>
        {yType}: {tooltip.data.y}
      </div>
      <div>Day: {tooltip.data.x.toDateString()}</div>
      <div>
        {seriesLabel}: {tooltip.data.label}
      </div>
    </Stack>
  );
});

function HeatChart(props: {
  readonly y_title: HeatChartYType;
  readonly points: ReadonlyArray<HeatChartPoint>;
  readonly dateRange: [Date, Date];
  readonly parentWidth: number;
  readonly setTooltip: ShowTooltipFunc;
}) {
  const { width, height, xScale, yScale, colorScale, marginLeft } = useScales({
    yType: props.y_title,
    points: props.points,
    dateRange: props.dateRange,
    containerWidth: props.parentWidth,
  });

  return (
    <svg width={width} height={height}>
      <g transform={`translate(${marginLeft}, ${marginTop})`}>
        <HeatSeries
          points={props.points}
          xScale={xScale}
          yScale={yScale}
          colorScale={colorScale}
          setTooltip={props.setTooltip}
        />
        <HeatAxis xScale={xScale} yScale={yScale} colorScale={colorScale} />
      </g>
    </svg>
  );
}

//--------------------------------------------------------------------------------
// Selection and Card
//--------------------------------------------------------------------------------

export interface SelectableHeatCardProps<T extends string> {
  readonly label: string;
  readonly onExport: () => void;

  readonly cur_selected: T;
  readonly setSelected?: (p: T) => void;
  readonly options: ReadonlyArray<T>;
}

function ChartToolbar<T extends string>(props: SelectableHeatCardProps<T>) {
  const setSelected = props.setSelected;
  return (
    <Box
      component="nav"
      sx={{
        display: "flex",
        minHeight: "2.5em",
        alignItems: "center",
        maxWidth: "calc(100vw - 24px - 24px)",
      }}
    >
      <Typography variant="subtitle1">{props.label}</Typography>
      <Box flexGrow={1} />
      {setSelected ? (
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={props.cur_selected}
            onChange={(e) => setSelected(e.target.value as T)}
          >
            {props.options.map((v, idx) => (
              <MenuItem key={idx} value={v}>
                {v}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      ) : undefined}
      <Tooltip title="Copy to Clipboard">
        <IconButton
          onClick={props.onExport}
          style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
          size="large"
        >
          <ImportExport />
        </IconButton>
      </Tooltip>
    </Box>
  );
}

export type SelectableHeatChartProps<T extends string> = HeatChartProps & SelectableHeatCardProps<T>;

export function SelectableHeatChart<T extends string>(props: SelectableHeatChartProps<T>) {
  const tooltipAtom = useMemo(() => atom<TooltipData | null>(null), []);
  const setTooltip = useSetAtom(tooltipAtom);
  const pointerLeave = useCallback(() => {
    setTooltip(null);
  }, [setTooltip]);

  return (
    <Box paddingLeft="24px" paddingRight="24px" paddingTop="10px">
      <ChartToolbar
        label={props.label}
        setSelected={props.setSelected}
        cur_selected={props.cur_selected}
        onExport={props.onExport}
        options={props.options}
      />
      <main onPointerLeave={pointerLeave}>
        <ChartWithTooltip
          sx={{ width: "100%" }}
          tooltipAtom={tooltipAtom}
          autoHeight
          chart={({ width }) => (
            <HeatChart
              points={props.points}
              y_title={props.y_title}
              dateRange={props.dateRange}
              parentWidth={width}
              setTooltip={setTooltip}
            />
          )}
          TooltipContent={({ tooltip }) => (
            <HeatTooltip yType={props.y_title} seriesLabel={props.label_title} tooltip={tooltip} />
          )}
        />
      </main>
    </Box>
  );
}
