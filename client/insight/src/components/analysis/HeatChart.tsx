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
import { PointerEvent, useMemo, memo, useRef, useCallback, useState } from "react";
import { addDays } from "date-fns";
import { Select, MenuItem, Tooltip, IconButton, Stack, Box, Typography, FormControl } from "@mui/material";
import { ImportExport } from "@mui/icons-material";
import { PickD3Scale, scaleBand, scaleLinear } from "@visx/scale";
import { ParentSize } from "@visx/responsive";

import { LazySeq } from "@seedtactics/immutable-collections";
import { chartTheme } from "../../util/chart-colors.js";
import { Axis } from "@visx/axis";
import { Group } from "@visx/group";
import { localPoint } from "@visx/event";
import { ChartTooltip } from "../ChartTooltip.js";

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
}

interface HeatChartScales {
  readonly xScale: PickD3Scale<"band", number, Date>;
  readonly yScale: PickD3Scale<"band", number, string>;
  readonly colorScale: PickD3Scale<"linear", string, string>;
}

const marginLeft = 50;
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
    return scaleBand({
      domain: xValues,
      range: [0, xMax],
      align: 0,
      padding: 0.05,
    });
  }, [dateRangeStart, dateRangeEnd, xMax]);

  const { yScale, height, colorScale } = useMemo(() => {
    const yValues = new Set<string>();
    for (const pt of points) {
      yValues.add(pt.y);
    }

    const yMax = 60 * yValues.size;
    const height = yMax + marginTop + marginBottom;
    const yScale = scaleBand({
      domain: Array.from(yValues).sort((a, b) => a.localeCompare(b)),
      range: [0, yMax],
      align: 0,
      padding: 0.05,
    });

    let colorScale: PickD3Scale<"linear", string, string>;
    if (yType === "Station") {
      colorScale = scaleLinear({
        domain: [0, 1],
        range: [color1, color2],
      });
    } else {
      const maxCnt = LazySeq.of(points).maxBy((pt) => pt.color)?.color ?? 1;
      colorScale = scaleLinear({
        domain: [0, maxCnt],
        range: [color1, color2],
      });
    }

    return { yScale, height, colorScale };
  }, [points, yType]);

  return { height, width, xScale, yScale, colorScale };
}

const HeatAxis = memo(function HeatAxis({ xScale, yScale }: HeatChartScales) {
  return (
    <>
      <Axis
        scale={xScale}
        top={yScale.range()[1]}
        orientation="bottom"
        numTicks={15}
        tickFormat={(d) => d.toLocaleDateString(undefined, { month: "short", day: "numeric" })}
        labelProps={chartTheme.axisStyles.y.left.axisLabel}
        stroke={chartTheme.axisStyles.x.bottom.axisLine.stroke}
        strokeWidth={chartTheme.axisStyles.x.bottom.axisLine.strokeWidth}
        tickLength={chartTheme.axisStyles.x.bottom.tickLength}
        tickStroke={chartTheme.axisStyles.x.bottom.tickLine.stroke}
        tickLabelProps={() => chartTheme.axisStyles.x.bottom.tickLabel}
      />
      <Axis
        scale={yScale}
        orientation="left"
        left={xScale.range()[0]}
        tickValues={yScale.domain()}
        labelProps={chartTheme.axisStyles.y.left.axisLabel}
        stroke={chartTheme.axisStyles.y.left.axisLine.stroke}
        strokeWidth={chartTheme.axisStyles.y.left.axisLine.strokeWidth}
        tickLength={chartTheme.axisStyles.y.left.tickLength}
        tickStroke={chartTheme.axisStyles.y.left.tickLine.stroke}
        tickLabelProps={() => ({ ...chartTheme.axisStyles.y.left.tickLabel, width: marginLeft })}
      />
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
    <ChartTooltip left={tooltip.left} top={tooltip.top}>
      <Stack direction="column" spacing={0.6}>
        <div>
          {yType}: {tooltip.data.y}
        </div>
        <div>Day: {tooltip.data.x.toDateString()}</div>
        <div>
          {seriesLabel}: {tooltip.data.label}
        </div>
      </Stack>
    </ChartTooltip>
  );
});

const HeatChart = memo(function HeatChart(props: HeatChartProps & { readonly parentWidth: number }) {
  const [tooltip, setTooltip] = useState<TooltipData | null>(null);

  const { width, height, xScale, yScale, colorScale } = useScales({
    yType: props.y_title,
    points: props.points,
    dateRange: props.dateRange,
    containerWidth: props.parentWidth,
  });

  const pointerLeave = useCallback(() => {
    setTooltip(null);
  }, [setTooltip]);

  return (
    <div style={{ position: "relative" }} onPointerLeave={pointerLeave}>
      <svg width={width} height={height}>
        <Group left={marginLeft} top={marginTop}>
          <HeatSeries
            points={props.points}
            xScale={xScale}
            yScale={yScale}
            colorScale={colorScale}
            setTooltip={setTooltip}
          />
          <HeatAxis xScale={xScale} yScale={yScale} colorScale={colorScale} />
        </Group>
      </svg>
      <HeatTooltip yType={props.y_title} seriesLabel={props.label_title} tooltip={tooltip} />
    </div>
  );
});

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
  return (
    <Box paddingLeft="24px" paddingRight="24px" paddingTop="10px">
      <ChartToolbar
        label={props.label}
        setSelected={props.setSelected}
        cur_selected={props.cur_selected}
        onExport={props.onExport}
        options={props.options}
      />
      <main>
        <ParentSize>
          {(parent) => (
            <HeatChart
              points={props.points}
              y_title={props.y_title}
              dateRange={props.dateRange}
              label_title={props.label_title}
              parentWidth={parent.width}
            />
          )}
        </ParentSize>
      </main>
    </Box>
  );
}
