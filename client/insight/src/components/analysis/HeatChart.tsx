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
import { addDays, format } from "date-fns";
import { Card, CardContent, CardHeader, Select, MenuItem, Tooltip, IconButton, Stack } from "@mui/material";
import { ImportExport } from "@mui/icons-material";
import { PickD3Scale, scaleBand, scaleLinear } from "@visx/scale";
import { ParentSize } from "@visx/responsive";

import { LazySeq } from "@seedtactics/immutable-collections";
import { chartTheme } from "../../util/chart-colors.js";
import { Axis } from "@visx/axis";
import { Group } from "@visx/group";
import { useTooltip, Tooltip as VisxTooltip } from "@visx/tooltip";
import { localPoint } from "@visx/event";

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
  const width = containerWidth === null || containerWidth === undefined || containerWidth === 0 ? 400 : containerWidth;

  const xMax = width - marginLeft - marginRight;

  const xScale = React.useMemo(() => {
    const xValues: Array<Date> = [];
    if (dateRange[1] < dateRange[0]) {
      // should never happen, just do something in case
      xValues.push(dateRange[0]);
    } else {
      let d = dateRange[0];
      while (d < dateRange[1]) {
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
  }, [dateRange[0], dateRange[1], xMax]);

  const { yScale, height, colorScale } = React.useMemo(() => {
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
      const maxCnt = LazySeq.ofIterable(points).maxBy((pt) => pt.color)?.color ?? 1;
      colorScale = scaleLinear({
        domain: [0, maxCnt],
        range: [color1, color2],
      });
    }

    return { yScale, height, colorScale };
  }, [points]);

  return { height, width, xScale, yScale, colorScale };
}

const HeatAxis = React.memo(function HeatAxis({ xScale, yScale }: HeatChartScales) {
  return (
    <>
      <Axis
        scale={xScale}
        top={yScale.range()[1]}
        orientation="bottom"
        numTicks={15}
        tickFormat={(d) => format(d, "MMM d")}
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

type ShowTooltipFunc = (a: {
  readonly tooltipLeft?: number;
  readonly tooltipTop?: number;
  readonly tooltipData?: HeatChartPoint;
}) => void;

const HeatSeries = React.memo(function HeatSeries({
  points,
  xScale,
  yScale,
  colorScale,
  showTooltip,
  hideTooltip,
}: {
  readonly points: ReadonlyArray<HeatChartPoint>;
  readonly showTooltip: ShowTooltipFunc;
  readonly hideTooltip: () => void;
} & HeatChartScales) {
  const hideRef = React.useRef<number | null>(null);
  const pointerEnter = React.useCallback(
    (e: React.PointerEvent<SVGRectElement>) => {
      const pt = localPoint(e);
      if (pt === null) return;
      if (hideRef.current !== null) {
        clearTimeout(hideRef.current);
        hideRef.current = null;
      }
      const idxS = (e.target as SVGRectElement).dataset.idx;
      if (idxS === undefined) return;
      const heatPoint = points[parseInt(idxS)];
      showTooltip({ tooltipLeft: pt.x, tooltipTop: pt.y, tooltipData: heatPoint });
    },
    [points, showTooltip]
  );

  const pointerLeave = React.useCallback(() => {
    if (hideRef.current === null) {
      hideRef.current = window.setTimeout(() => {
        hideRef.current = null;
        hideTooltip();
      }, 100);
    }
  }, [showTooltip]);

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

const HeatTooltip = React.memo(function HeatTooltip({
  yType,
  seriesLabel,
  tooltipData,
  tooltipLeft,
  tooltipTop,
}: {
  yType: HeatChartYType;
  seriesLabel: string;
  tooltipData: HeatChartPoint;
  tooltipLeft: number | undefined;
  tooltipTop: number | undefined;
}) {
  return (
    <VisxTooltip left={tooltipLeft} top={tooltipTop}>
      <Stack direction="column" spacing={0.6}>
        <div>
          {yType}: {tooltipData.y}
        </div>
        <div>Day: {tooltipData.x.toDateString()}</div>
        <div>
          {seriesLabel}: {tooltipData.label}
        </div>
      </Stack>
    </VisxTooltip>
  );
});

const HeatChart = React.memo(function HeatChart(props: HeatChartProps & { readonly parentWidth: number }) {
  const { showTooltip, hideTooltip, tooltipData, tooltipLeft, tooltipTop } = useTooltip<HeatChartPoint>();

  const { width, height, xScale, yScale, colorScale } = useScales({
    yType: props.y_title,
    points: props.points,
    dateRange: props.dateRange,
    containerWidth: props.parentWidth,
  });

  const pointerLeave = React.useCallback(() => {
    hideTooltip();
  }, [hideTooltip]);

  return (
    <div style={{ position: "relative" }} onPointerLeave={pointerLeave}>
      <svg width={width} height={height}>
        <Group left={marginLeft} top={marginTop}>
          <HeatSeries
            points={props.points}
            xScale={xScale}
            yScale={yScale}
            colorScale={colorScale}
            showTooltip={showTooltip}
            hideTooltip={hideTooltip}
          />
          <HeatAxis xScale={xScale} yScale={yScale} colorScale={colorScale} />
        </Group>
      </svg>
      {tooltipData ? (
        <HeatTooltip
          yType={props.y_title}
          seriesLabel={props.label_title}
          tooltipData={tooltipData}
          tooltipLeft={tooltipLeft}
          tooltipTop={tooltipTop}
        />
      ) : undefined}
    </div>
  );
});

//--------------------------------------------------------------------------------
// Selection and Card
//--------------------------------------------------------------------------------

export interface SelectableHeatCardProps<T extends string> {
  readonly icon: JSX.Element;
  readonly card_label: string;
  readonly onExport: () => void;

  readonly cur_selected: T;
  readonly setSelected?: (p: T) => void;
  readonly options: ReadonlyArray<T>;
}

function CardTitle<T extends string>(props: SelectableHeatCardProps<T>) {
  const setSelected = props.setSelected;
  return (
    <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
      {props.icon}
      <div style={{ marginLeft: "10px", marginRight: "3em" }}>{props.card_label}</div>
      <div style={{ flexGrow: 1 }} />
      <Tooltip title="Copy to Clipboard">
        <IconButton onClick={props.onExport} style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }} size="large">
          <ImportExport />
        </IconButton>
      </Tooltip>
      {setSelected ? (
        <Select
          name={props.card_label.replace(" ", "-") + "-heatchart-planned-or-actual"}
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
      ) : undefined}
    </div>
  );
}

export type SelectableHeatChartProps<T extends string> = HeatChartProps & SelectableHeatCardProps<T>;

export function SelectableHeatChart<T extends string>(props: SelectableHeatChartProps<T>) {
  return (
    <Card raised>
      <CardHeader
        title={
          <CardTitle
            icon={props.icon}
            card_label={props.card_label}
            setSelected={props.setSelected}
            cur_selected={props.cur_selected}
            onExport={props.onExport}
            options={props.options}
          />
        }
      />
      <CardContent>
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
      </CardContent>
    </Card>
  );
}
