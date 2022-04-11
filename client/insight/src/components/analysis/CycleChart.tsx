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
import { format } from "date-fns";
import { HashMap } from "prelude-ts";
import {
  Dialog,
  DialogContent,
  DialogActions,
  TextField,
  IconButton,
  Button,
  styled,
  ToggleButton,
  Stack,
} from "@mui/material";
import ZoomIn from "@mui/icons-material/ZoomIn";
import { StatisticalCycleTime } from "../../cell-status/estimated-cycle-times";
import { chartTheme, seriesColor } from "../../util/chart-colors";
import { grey } from "@mui/material/colors";
import { useImmer } from "../../util/recoil-util";
import { localPoint } from "@visx/event";
import { PickD3Scale, scaleLinear, scaleTime } from "@visx/scale";
import { Group } from "@visx/group";
import { useTooltip, TooltipWithBounds as VisxTooltip, defaultStyles as defaultTooltipStyles } from "@visx/tooltip";
import useMeasure from "react-use-measure";
import { AnimatedAxis, AnimatedGridColumns, AnimatedGridRows } from "@visx/react-spring";
import { useSpring, useSprings, animated } from "react-spring";

export interface CycleChartPoint {
  readonly cntr: number;
  readonly x: Date;
  readonly y: number;
}

export interface ExtraTooltip {
  title: string;
  value: string;
  link?: () => void;
}

export interface DataToPlotProps {
  readonly points: HashMap<string, ReadonlyArray<CycleChartPoint>>;
  readonly stats?: StatisticalCycleTime;
  readonly partCntPerPoint?: number;
  readonly plannedTimeMinutes?: number;
}

export interface ScaleZoomProps {
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
}

export type CycleChartProps = DataToPlotProps &
  ScaleZoomProps & {
    readonly series_label: string;
    readonly extra_tooltip?: (point: CycleChartPoint) => ReadonlyArray<ExtraTooltip>;
  };

interface DataToPlot {
  readonly series: ReadonlyArray<{
    readonly name: string;
    readonly color: string;
    readonly points: ReadonlyArray<CycleChartPoint>;
  }>;
  readonly median: { readonly low: number; readonly high: number } | null;
}

function useDataToPlot({ points, stats, partCntPerPoint }: DataToPlotProps): DataToPlot {
  const series = React.useMemo(() => {
    const seriesNames = points.keySet().toArray({ sortOn: (x) => x });

    return seriesNames.map((name, idx) => ({
      name,
      color: seriesColor(idx, seriesNames.length),
      points: points.get(name).getOrElse([]),
    }));
  }, [points]);

  const median = React.useMemo(() => {
    if (stats) {
      const low = (partCntPerPoint ?? 1) * (stats.medianMinutesForSingleMat - stats.MAD_belowMinutes);
      const high = (partCntPerPoint ?? 1) * (stats.medianMinutesForSingleMat + stats.MAD_aboveMinutes);

      return { low, high };
    } else {
      return null;
    }
  }, [stats, partCntPerPoint]);

  return { series, median };
}

const marginLeft = 50;
const marginBottom = 20;
const marginTop = 10;
const marginRight = 2;

interface CycleChartDimensions {
  readonly height: number;
  readonly width: number;
}

interface CycleChartScales {
  readonly xScale: PickD3Scale<"time", number>;
  readonly yScale: PickD3Scale<"linear", number>;
}

function useScales({
  points,
  yZoom,
  default_date_range,
  current_date_zoom,
  containerWidth,
}: {
  readonly points: HashMap<string, ReadonlyArray<CycleChartPoint>>;
  readonly yZoom: YZoomRange | null;
  readonly containerWidth: number | null | undefined;
} & ScaleZoomProps): CycleChartDimensions & CycleChartScales {
  const [height, setChartHeight] = React.useState(window.innerHeight - 250);
  React.useEffect(() => {
    setChartHeight(window.innerHeight - 250);
  }, []);

  const width = containerWidth === null || containerWidth === undefined || containerWidth === 0 ? 400 : containerWidth;

  const xMax = width - marginLeft - marginRight;
  const yMax = height - marginTop - marginBottom;

  const xScale = React.useMemo(() => {
    if (current_date_zoom) {
      return scaleTime({
        domain: [current_date_zoom.start, current_date_zoom.end],
        range: [0, xMax],
      });
    } else {
      return scaleTime({
        domain: [default_date_range[0], default_date_range[1]],
        range: [0, xMax],
      });
    }
  }, [current_date_zoom, default_date_range, xMax]);

  const maxYVal = React.useMemo(() => {
    if (points.isEmpty()) return 60;
    const m = points.foldLeft(0, (v, [, pts]) => pts.reduce((w, p) => Math.max(w, p.y), v));
    // round up to nearest 5
    return Math.ceil(m / 5) * 5;
  }, [points]);

  const yScale = React.useMemo(() => {
    if (yZoom) {
      return scaleLinear({
        domain: [yZoom.y_low, yZoom.y_high],
        range: [yMax, 0],
      });
    } else {
      return scaleLinear({
        domain: [0, maxYVal],
        range: [yMax, 0],
      });
    }
  }, [yMax, yZoom, maxYVal]);

  return { height, width, xScale, yScale };
}

const AxisAndGrid = React.memo(function AxisAndGrid({ xScale, yScale }: CycleChartScales) {
  return (
    <>
      <AnimatedAxis
        scale={xScale}
        top={yScale.range()[0]}
        orientation="bottom"
        labelProps={chartTheme.axisStyles.y.left.axisLabel}
        stroke={chartTheme.axisStyles.x.bottom.axisLine.stroke}
        strokeWidth={chartTheme.axisStyles.x.bottom.axisLine.strokeWidth}
        tickLength={chartTheme.axisStyles.x.bottom.tickLength}
        tickStroke={chartTheme.axisStyles.x.bottom.tickLine.stroke}
        tickLabelProps={() => chartTheme.axisStyles.x.bottom.tickLabel}
      />
      <AnimatedAxis
        scale={yScale}
        orientation="left"
        left={xScale.range()[0]}
        label="Minutes"
        labelProps={chartTheme.axisStyles.y.left.axisLabel}
        stroke={chartTheme.axisStyles.y.left.axisLine.stroke}
        strokeWidth={chartTheme.axisStyles.y.left.axisLine.strokeWidth}
        tickLength={chartTheme.axisStyles.y.left.tickLength}
        tickStroke={chartTheme.axisStyles.y.left.tickLine.stroke}
        tickLabelProps={() => ({ ...chartTheme.axisStyles.y.left.tickLabel, width: marginLeft })}
      />
      <AnimatedGridColumns
        height={yScale.range()[0] - yScale.range()[1]}
        scale={xScale}
        lineStyle={chartTheme.gridStyles}
      />
      <AnimatedGridRows
        width={xScale.range()[1] - xScale.range()[0]}
        scale={yScale}
        lineStyle={chartTheme.gridStyles}
      />
    </>
  );
});

interface YZoomRange {
  readonly y_low: number;
  readonly y_high: number;
}

const SetYZoomButton = React.memo(function SetYZoomButton(props: {
  readonly yZoom: YZoomRange | null;
  readonly setZoom: (f: (zoom: YZoomRange | null) => YZoomRange | null) => void;
}) {
  const [low, setLow] = React.useState<number>();
  const [high, setHigh] = React.useState<number>();
  const [open, setOpen] = React.useState<boolean>(false);

  function close() {
    setOpen(false);
    setLow(undefined);
    setHigh(undefined);
  }

  return (
    <>
      <IconButton size="small" onClick={() => setOpen(true)}>
        <ZoomIn fontSize="inherit" />
      </IconButton>
      <Dialog open={open} onClose={close}>
        <DialogContent>
          <div style={{ marginBottom: "1em" }}>
            <TextField
              type="number"
              label="Y Low"
              value={low !== undefined ? (isNaN(low) ? "" : low) : props.yZoom?.y_low ?? ""}
              onChange={(e) => setLow(parseFloat(e.target.value))}
              onBlur={() => {
                if (low) {
                  props.setZoom((z) => (z ? { ...z, y_low: low } : { y_low: low, y_high: 60 }));
                }
              }}
            />
          </div>
          <div style={{ marginBottom: "1em" }}>
            <TextField
              type="number"
              label="Y High"
              value={high !== undefined ? (isNaN(high) ? "" : high) : props.yZoom?.y_high ?? ""}
              onChange={(e) => setHigh(parseFloat(e.target.value))}
              onBlur={() => {
                if (high) {
                  props.setZoom((z) => (z ? { ...z, y_high: high } : { y_low: 0, y_high: high }));
                }
              }}
            />
          </div>
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>Close</Button>
        </DialogActions>
      </Dialog>
    </>
  );
});

const StatsSeries = React.memo(function StatsSeries({
  median,
  plannedMinutes,
  xScale,
  yScale,
}: CycleChartScales & {
  readonly median: { readonly low: number; readonly high: number } | null;
  readonly plannedMinutes: number | null | undefined;
}) {
  const medianSpring = useSpring({
    to: {
      x: xScale.range()[0],
      y: median ? yScale(median.high) : yScale.range()[0],
      width: xScale.range()[1] - xScale.range()[0],
      height: median ? yScale(median.low) - yScale(median.high) : 0,
    },
    from: {
      x: xScale.range()[0],
      y: yScale.range()[0],
      width: xScale.range()[1] - xScale.range()[0],
      height: 0,
    },
  });
  const plannedSpring = useSpring({
    to: { y: plannedMinutes ? yScale(plannedMinutes) : yScale.range()[0] },
    from: { y: yScale.range()[0] },
  });
  return (
    <g>
      {median ? (
        <animated.rect
          x={medianSpring.x}
          y={medianSpring.y}
          width={medianSpring.width}
          height={medianSpring.height}
          fill={grey[700]}
          opacity={0.2}
          pointerEvents="none"
        />
      ) : undefined}
      {plannedMinutes ? (
        <animated.line
          stroke="black"
          x1={xScale.range()[0]}
          x2={xScale.range()[1]}
          y1={plannedSpring.y}
          y2={plannedSpring.y}
        />
      ) : undefined}
    </g>
  );
});

type ShowTooltipFunc = (a: {
  readonly tooltipLeft?: number;
  readonly tooltipTop?: number;
  readonly tooltipData?: { readonly pt: CycleChartPoint; readonly seriesName: string };
}) => void;

const SingleSeries = React.memo(function SingleSeries({
  seriesName,
  points,
  color,
  xScale,
  yScale,
  showTooltip,
}: CycleChartScales & {
  readonly seriesName: string;
  readonly points: ReadonlyArray<CycleChartPoint>;
  readonly color: string;
  readonly showTooltip: ShowTooltipFunc;
}) {
  const show = React.useCallback(
    (e: React.MouseEvent) => {
      const p = localPoint(e);
      if (p === null) return;
      const idxS = (e.target as SVGCircleElement).dataset.idx;
      if (idxS === undefined) return;
      showTooltip({
        tooltipLeft: p.x,
        tooltipTop: p.y,
        tooltipData: { pt: points[parseInt(idxS)], seriesName },
      });
    },
    [showTooltip, points, seriesName]
  );

  const springs = useSprings(
    points.length,
    points.map((pt) => ({
      from: { x: xScale(pt.x), y: yScale.range()[0] + 15 },
      to: { x: xScale(pt.x), y: yScale(pt.y) },
    }))
  );

  return (
    <g>
      {springs.map((pt, idx) => (
        <animated.circle
          key={idx}
          className="bms-cycle-chart-pt"
          fill={color}
          r={5}
          cx={pt.x}
          cy={pt.y}
          data-idx={idx}
          onClick={show}
        />
      ))}
    </g>
  );
});

const AllPointsSeries = React.memo(function AllPointsSeries({
  series,
  xScale,
  yScale,
  showTooltip,
  disabledSeries,
}: CycleChartScales & {
  readonly series: ReadonlyArray<{
    readonly name: string;
    readonly color: string;
    readonly points: ReadonlyArray<CycleChartPoint>;
  }>;
  readonly showTooltip: ShowTooltipFunc;
  readonly disabledSeries: ReadonlySet<string>;
}) {
  return (
    <g>
      {series
        .filter((s) => !disabledSeries.has(s.name))
        .map((s) => (
          <SingleSeries
            key={s.name}
            points={s.points}
            seriesName={s.name}
            color={s.color}
            xScale={xScale}
            yScale={yScale}
            showTooltip={showTooltip}
          />
        ))}
    </g>
  );
});

const Legend = React.memo(function Legend({
  series,
  disabledSeries,
  adjustDisabled,
}: {
  readonly series: ReadonlyArray<{
    readonly name: string;
    readonly color: string;
    readonly points: ReadonlyArray<CycleChartPoint>;
  }>;
  readonly disabledSeries: ReadonlySet<string>;
  readonly adjustDisabled: (f: (s: Set<string>) => void) => void;
}) {
  return (
    <div style={{ marginTop: "1em", display: "flex", flexWrap: "wrap", justifyContent: "space-around" }}>
      {series.map((s) => (
        <ToggleButton
          key={s.name}
          selected={!disabledSeries.has(s.name)}
          value={s}
          onChange={() => adjustDisabled((b) => (b.has(s.name) ? b.delete(s.name) : b.add(s.name)))}
        >
          <div style={{ display: "flex", alignItems: "center" }}>
            <div style={{ width: "14px", height: "14px", backgroundColor: s.color }} />
            <div style={{ marginLeft: "1em" }}>{s.name}</div>
          </div>
        </ToggleButton>
      ))}
    </div>
  );
});

const NoPointerEvents = styled("g", { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })<{
  $noPtrEvents?: boolean;
}>(({ $noPtrEvents }) =>
  $noPtrEvents
    ? {
        "& .bms-cycle-chart-pt": {
          pointerevents: "none",
        },
      }
    : undefined
);

interface ChartMouseEventProps {
  readonly setYZoom: (r: YZoomRange) => void;
  readonly setXZoom: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
  readonly hideTooltip: () => void;
  readonly highlightStart: { readonly x: number; readonly y: number; readonly nowMS: number } | null;
  readonly setHighlightStart: (p: { readonly x: number; readonly y: number; readonly nowMS: number } | null) => void;
}

const ChartMouseEvents = React.memo(function ChartMouseEvents({
  setYZoom,
  setXZoom,
  hideTooltip,
  xScale,
  yScale,
  highlightStart,
  setHighlightStart,
}: CycleChartScales & ChartMouseEventProps) {
  // mouse click and drag zooms
  const [curHighlight, setCurrent] = React.useState<{ x: number; y: number } | null>(null);

  const pointerDown = React.useCallback(
    (e: React.PointerEvent) => {
      const p = localPoint(e);
      if (p === null) return;
      setCurrent(null);
      setHighlightStart({ x: p.x - marginLeft, y: p.y - marginTop, nowMS: Date.now() });
      hideTooltip();
    },
    [setHighlightStart, hideTooltip]
  );

  const pointerMove = React.useCallback(
    (e: React.PointerEvent) => {
      const p = localPoint(e);
      if (p === null) return;
      setCurrent({ x: p.x - marginLeft, y: p.y - marginTop });
    },
    [setCurrent]
  );

  const pointerUp = React.useCallback(
    (e: React.PointerEvent) => {
      if (highlightStart === null) return;
      if (Date.now() - highlightStart.nowMS > 500) {
        const p = localPoint(e);
        if (p !== null) {
          const time1 = xScale.invert(highlightStart.x);
          const time2 = xScale.invert(p.x - marginLeft);
          const y1 = yScale.invert(highlightStart.y);
          const y2 = yScale.invert(p.y - marginTop);

          setYZoom(y1 < y2 ? { y_low: y1, y_high: y2 } : { y_low: y2, y_high: y1 });
          setXZoom?.({
            zoom: time1.getTime() < time2.getTime() ? { start: time1, end: time2 } : { start: time2, end: time1 },
          });
        }
      }

      setHighlightStart(null);
      setCurrent(null);
    },
    [highlightStart, setHighlightStart, setCurrent]
  );

  return (
    <g>
      <rect
        x={0}
        y={0}
        width={xScale.range()[1]}
        height={yScale.range()[0]}
        fill="transparent"
        onPointerDown={pointerDown}
        onPointerMove={highlightStart !== null ? pointerMove : undefined}
        onPointerUp={highlightStart !== null ? pointerUp : undefined}
      />
      {highlightStart !== null && curHighlight !== null ? (
        <rect
          x={Math.min(highlightStart.x, curHighlight.x)}
          y={Math.min(highlightStart.y, curHighlight.y)}
          width={Math.abs(highlightStart.x - curHighlight.x)}
          height={Math.abs(highlightStart.y - curHighlight.y)}
          color="red"
          pointerEvents="none"
          opacity={0.3}
        />
      ) : undefined}
    </g>
  );
});

const ChartZoomButtons = React.memo(function ChartZoomButtons({
  set_date_zoom_range,
  current_date_zoom,
  yZoom,
  setYZoom,
  median,
}: {
  readonly yZoom: YZoomRange | null;
  readonly setYZoom: (a: React.SetStateAction<YZoomRange | null>) => void;
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
  readonly median: { readonly low: number; readonly high: number } | null;
}) {
  return (
    <div>
      {set_date_zoom_range && (current_date_zoom || yZoom) ? (
        <>
          <Button
            size="small"
            onClick={() => {
              set_date_zoom_range?.({ zoom: undefined });
              setYZoom(null);
            }}
          >
            Reset Zoom
          </Button>
          <SetYZoomButton yZoom={yZoom} setZoom={setYZoom} />
        </>
      ) : undefined}
      {set_date_zoom_range && !current_date_zoom && !yZoom ? (
        <span style={{ color: "#6b6b76" }}>
          Zoom via mouse drag
          {median ? (
            <>
              <span> or </span>
              <Button
                size="small"
                onClick={() => {
                  const high = median.high;
                  const low = median.low;
                  const extra = 0.2 * (high - low);
                  setYZoom({ y_low: low - extra, y_high: high + extra });
                }}
              >
                Zoom To Inliers
              </Button>
            </>
          ) : undefined}
          <SetYZoomButton yZoom={yZoom} setZoom={setYZoom} />
        </span>
      ) : undefined}
    </div>
  );
});

const ChartTooltip = React.memo(function ChartTooltip({
  tooltipData,
  tooltipTop,
  tooltipLeft,
  extraTooltip,
  seriesLabel,
}: {
  readonly tooltipData: { readonly pt: CycleChartPoint; readonly seriesName: string };
  readonly tooltipTop: number | undefined;
  readonly tooltipLeft: number | undefined;
  readonly extraTooltip?: (point: CycleChartPoint) => ReadonlyArray<ExtraTooltip>;
  readonly seriesLabel: string;
}) {
  return (
    <VisxTooltip
      left={tooltipLeft}
      top={tooltipTop}
      style={{ ...defaultTooltipStyles, backgroundColor: grey[800], color: "white" }}
    >
      <Stack direction="column" spacing={0.6}>
        <div>Time: {format(tooltipData.pt.x, "MMM d, yyyy, h:mm aaaa")}</div>
        <div>
          {seriesLabel}: {tooltipData.seriesName}
        </div>
        <div>Cycle Time: {tooltipData.pt.y.toFixed(1)} minutes</div>
        {extraTooltip
          ? extraTooltip(tooltipData.pt).map((e, idx) => (
              <div key={idx}>
                {e.title}:{" "}
                {e.link ? (
                  <a
                    style={{ color: "white", pointerEvents: "auto", cursor: "pointer", borderBottom: "1px solid" }}
                    onClick={e.link}
                  >
                    {e.value}
                  </a>
                ) : (
                  <span>e.value</span>
                )}
              </div>
            ))
          : undefined}
      </Stack>
    </VisxTooltip>
  );
});

export const CycleChart = React.memo(function CycleChart(props: CycleChartProps) {
  // the state of the chart
  const { showTooltip, hideTooltip, tooltipData, tooltipLeft, tooltipTop } =
    useTooltip<{ readonly pt: CycleChartPoint; readonly seriesName: string }>();
  const [yZoom, setYZoom] = React.useState<YZoomRange | null>(null);
  const [disabledSeries, adjustDisabled] = useImmer<ReadonlySet<string>>(new Set());
  const [highlightStart, setHighlightStart] = React.useState<{
    readonly x: number;
    readonly y: number;
    readonly nowMS: number;
  } | null>(null);

  // computed scales and values
  const [measureRef, bounds] = useMeasure();
  const { width, height, xScale, yScale } = useScales({
    points: props.points,
    yZoom,
    default_date_range: props.default_date_range,
    current_date_zoom: props.current_date_zoom,
    set_date_zoom_range: props.set_date_zoom_range,
    containerWidth: bounds?.width,
  });
  const { series, median } = useDataToPlot({
    points: props.points,
    stats: props.stats,
    partCntPerPoint: props.partCntPerPoint,
  });

  const pointerLeave = React.useCallback(() => {
    hideTooltip();
    setHighlightStart(null);
  }, [hideTooltip, setHighlightStart]);

  return (
    <div style={{ position: "relative" }} ref={measureRef} onPointerLeave={pointerLeave}>
      <svg width={width} height={height}>
        <Group left={marginLeft} top={marginTop}>
          <AxisAndGrid xScale={xScale} yScale={yScale} />
          <StatsSeries median={median} plannedMinutes={props.plannedTimeMinutes} xScale={xScale} yScale={yScale} />
          <ChartMouseEvents
            setYZoom={setYZoom}
            setXZoom={props.set_date_zoom_range}
            hideTooltip={hideTooltip}
            xScale={xScale}
            yScale={yScale}
            highlightStart={highlightStart}
            setHighlightStart={setHighlightStart}
          />
          <NoPointerEvents $noPtrEvents={highlightStart !== null}>
            <AllPointsSeries
              series={series}
              disabledSeries={disabledSeries}
              xScale={xScale}
              yScale={yScale}
              showTooltip={showTooltip}
            />
          </NoPointerEvents>
        </Group>
      </svg>

      <div style={{ display: "flex", flexWrap: "wrap" }}>
        <div style={{ color: "#6b6b76" }}>Click on a point for details</div>
        <div style={{ flexGrow: 1 }} />
        <ChartZoomButtons
          set_date_zoom_range={props.set_date_zoom_range}
          current_date_zoom={props.current_date_zoom}
          yZoom={yZoom}
          setYZoom={setYZoom}
          median={median}
        />
      </div>
      <Legend series={series} disabledSeries={disabledSeries} adjustDisabled={adjustDisabled} />
      {tooltipData ? (
        <ChartTooltip
          seriesLabel={props.series_label}
          extraTooltip={props.extra_tooltip}
          tooltipData={tooltipData}
          tooltipLeft={tooltipLeft}
          tooltipTop={tooltipTop}
        />
      ) : undefined}
    </div>
  );
});
