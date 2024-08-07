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
import { SetStateAction, MouseEvent, PointerEvent, useMemo, memo, useState, useCallback } from "react";
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
  Box,
} from "@mui/material";
import { ZoomIn } from "@mui/icons-material";
import { StatisticalCycleTime } from "../../cell-status/estimated-cycle-times.js";
import { chartTheme, seriesColor } from "../../util/chart-colors.js";
import { grey } from "@mui/material/colors";
import { localPoint } from "@visx/event";
import { PickD3Scale, scaleLinear, scaleTime } from "@visx/scale";
import { Group } from "@visx/group";
import { ChartTooltip } from "../ChartTooltip.js";
import { Axis } from "@visx/axis";
import { GridColumns, GridRows } from "@visx/grid";
import { useSpring, useSprings, animated } from "@react-spring/web";
import { ParentSize } from "@visx/responsive";
import { HashSet, LazySeq } from "@seedtactics/immutable-collections";

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
  readonly points: ReadonlyMap<string, ReadonlyArray<CycleChartPoint>>;
  readonly stats?: StatisticalCycleTime;
  readonly partCntPerPoint?: number;
  readonly plannedTimeMinutes?: number;
}

export interface ScaleZoomProps {
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
  readonly yZoom: YZoomRange | null;
  readonly setYZoom: (a: SetStateAction<YZoomRange | null>) => void;
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
  const series = useMemo(() => {
    return (
      LazySeq.of(points)
        // need to sort first so the color indices are correct
        .toSortedArray(([k]) => k)
        .map(([seriesName, seriesPoints], idx) => ({
          name: seriesName,
          color: seriesColor(idx, points.size),
          points: seriesPoints ?? [],
        }))
    );
  }, [points]);

  const median = useMemo(() => {
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
  containerHeight,
}: {
  readonly points: ReadonlyMap<string, ReadonlyArray<CycleChartPoint>>;
  readonly yZoom: YZoomRange | null;
  readonly containerWidth: number;
  readonly containerHeight: number;
} & ScaleZoomProps): CycleChartDimensions & CycleChartScales {
  const width = containerWidth === 0 ? 400 : containerWidth;
  const height = containerHeight === 0 ? 400 : containerHeight;

  const xMax = width - marginLeft - marginRight;
  const yMax = height - marginTop - marginBottom;

  const xScale = useMemo(() => {
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

  const maxYVal = useMemo(() => {
    if (points.size === 0) return 60;
    const m =
      LazySeq.of(points)
        .flatMap(([, pts]) => pts.map((p) => p.y))
        .maxBy((y) => y) ?? 1;
    // round up to nearest 5
    return Math.ceil(m / 5) * 5;
  }, [points]);

  const yScale = useMemo(() => {
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

const AxisAndGrid = memo(function AxisAndGrid({ xScale, yScale }: CycleChartScales) {
  return (
    <>
      <Axis
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
      <Axis
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
      <GridColumns
        height={yScale.range()[0] - yScale.range()[1]}
        scale={xScale}
        lineStyle={chartTheme.gridStyles}
      />
      <GridRows
        width={xScale.range()[1] - xScale.range()[0]}
        scale={yScale}
        lineStyle={chartTheme.gridStyles}
      />
    </>
  );
});

export interface YZoomRange {
  readonly y_low: number;
  readonly y_high: number;
}

const SetYZoomButton = memo(function SetYZoomButton(props: {
  readonly yZoom: YZoomRange | null;
  readonly setZoom: (f: (zoom: YZoomRange | null) => YZoomRange | null) => void;
}) {
  const [low, setLow] = useState<number>();
  const [high, setHigh] = useState<number>();
  const [open, setOpen] = useState<boolean>(false);

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

const StatsSeries = memo(function StatsSeries({
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

interface TooltipData {
  readonly left: number;
  readonly top: number;
  readonly pt: CycleChartPoint;
  readonly seriesName: string;
}

type ShowTooltipFunc = (a: TooltipData | null) => void;

const SingleSeries = memo(function SingleSeries({
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
  const show = useCallback(
    (e: MouseEvent) => {
      const p = localPoint(e);
      if (p === null) return;
      const idxS = (e.target as SVGCircleElement).dataset.idx;
      if (idxS === undefined) return;
      showTooltip({
        left: p.x,
        top: p.y,
        pt: points[parseInt(idxS)],
        seriesName,
      });
    },
    [showTooltip, points, seriesName],
  );

  const springs = useSprings(
    points.length,
    points.map((pt) => ({
      from: { x: xScale(pt.x), y: yScale.range()[0] + 15 },
      to: { x: xScale(pt.x), y: yScale(pt.y) },
    })),
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

const AllPointsSeries = memo(function AllPointsSeries({
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

const Legend = memo(function Legend({
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
  readonly adjustDisabled: (f: (s: HashSet<string>) => HashSet<string>) => void;
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
    : undefined,
);

interface ChartMouseEventProps {
  readonly setYZoom: (r: YZoomRange) => void;
  readonly setXZoom: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
  readonly setTooltip: ShowTooltipFunc;
  readonly highlightStart: { readonly x: number; readonly y: number; readonly nowMS: number } | null;
  readonly setHighlightStart: (
    p: { readonly x: number; readonly y: number; readonly nowMS: number } | null,
  ) => void;
}

const ChartMouseEvents = memo(function ChartMouseEvents({
  setYZoom,
  setXZoom,
  setTooltip,
  xScale,
  yScale,
  highlightStart,
  setHighlightStart,
}: CycleChartScales & ChartMouseEventProps) {
  // mouse click and drag zooms
  const [curHighlight, setCurrent] = useState<{ x: number; y: number } | null>(null);

  const pointerDown = useCallback(
    (e: PointerEvent) => {
      const p = localPoint(e);
      if (p === null) return;
      setCurrent(null);
      setHighlightStart({ x: p.x - marginLeft, y: p.y - marginTop, nowMS: Date.now() });
      setTooltip(null);
    },
    [setHighlightStart, setTooltip],
  );

  const pointerMove = useCallback(
    (e: PointerEvent) => {
      const p = localPoint(e);
      if (p === null) return;
      setCurrent({ x: p.x - marginLeft, y: p.y - marginTop });
    },
    [setCurrent],
  );

  const pointerUp = useCallback(
    (e: PointerEvent) => {
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
            zoom:
              time1.getTime() < time2.getTime() ? { start: time1, end: time2 } : { start: time2, end: time1 },
          });
        }
      }

      setHighlightStart(null);
      setCurrent(null);
    },
    [highlightStart, setHighlightStart, setCurrent, setXZoom, setYZoom, xScale, yScale],
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

const ChartZoomButtons = memo(function ChartZoomButtons({
  set_date_zoom_range,
  current_date_zoom,
  yZoom,
  setYZoom,
  median,
}: {
  readonly yZoom: YZoomRange | null;
  readonly setYZoom: (a: SetStateAction<YZoomRange | null>) => void;
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

const CycleChartTooltip = memo(function CycleChartTooltip({
  tooltip,
  extraTooltip,
  seriesLabel,
}: {
  readonly tooltip: TooltipData | null;
  readonly extraTooltip?: (point: CycleChartPoint) => ReadonlyArray<ExtraTooltip>;
  readonly seriesLabel: string;
}) {
  if (tooltip === null) return null;
  return (
    <ChartTooltip left={tooltip.left} top={tooltip.top}>
      <Stack direction="column" spacing={0.6}>
        <div>
          Time:{" "}
          {tooltip.pt.x.toLocaleString(undefined, {
            month: "short",
            day: "numeric",
            year: "numeric",
            hour: "numeric",
            minute: "2-digit",
          })}
        </div>
        <div>
          {seriesLabel}: {tooltip.seriesName}
        </div>
        <div>Cycle Time: {tooltip.pt.y.toFixed(1)} minutes</div>
        {extraTooltip
          ? extraTooltip(tooltip.pt).map((e, idx) => (
              <div key={idx}>
                {e.title}:{" "}
                {e.link ? (
                  <a
                    style={{
                      color: "white",
                      pointerEvents: "auto",
                      cursor: "pointer",
                      borderBottom: "1px solid",
                    }}
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
    </ChartTooltip>
  );
});

function CycleChartSvg(
  props: CycleChartProps &
    DataToPlot & {
      readonly containerHeight: number;
      readonly containerWidth: number;
      readonly highlightStart: { readonly x: number; readonly y: number; readonly nowMS: number } | null;
      readonly setHighlightStart: (
        p: { readonly x: number; readonly y: number; readonly nowMS: number } | null,
      ) => void;
      readonly showTooltip: ShowTooltipFunc;
      readonly disabledSeries: ReadonlySet<string>;
    },
) {
  // computed scales and values
  const { width, height, xScale, yScale } = useScales({
    points: props.points,
    yZoom: props.yZoom,
    setYZoom: props.setYZoom,
    default_date_range: props.default_date_range,
    current_date_zoom: props.current_date_zoom,
    set_date_zoom_range: props.set_date_zoom_range,
    containerWidth: props.containerWidth,
    containerHeight: props.containerHeight,
  });

  return (
    <svg width={width} height={height}>
      <Group left={marginLeft} top={marginTop}>
        <AxisAndGrid xScale={xScale} yScale={yScale} />
        <StatsSeries
          median={props.median}
          plannedMinutes={props.plannedTimeMinutes}
          xScale={xScale}
          yScale={yScale}
        />
        <ChartMouseEvents
          setYZoom={props.setYZoom}
          setXZoom={props.set_date_zoom_range}
          xScale={xScale}
          setTooltip={props.showTooltip}
          yScale={yScale}
          highlightStart={props.highlightStart}
          setHighlightStart={props.setHighlightStart}
        />
        <NoPointerEvents $noPtrEvents={props.highlightStart !== null}>
          <AllPointsSeries
            series={props.series}
            disabledSeries={props.disabledSeries}
            xScale={xScale}
            yScale={yScale}
            showTooltip={props.showTooltip}
          />
        </NoPointerEvents>
      </Group>
    </svg>
  );
}

export const CycleChart = memo(function CycleChart(props: CycleChartProps) {
  // the state of the chart
  const [tooltip, setTooltip] = useState<TooltipData | null>(null);
  const [disabledSeries, setDisabled] = useState(HashSet.empty<string>());

  const [highlightStart, setHighlightStart] = useState<{
    readonly x: number;
    readonly y: number;
    readonly nowMS: number;
  } | null>(null);

  const { series, median } = useDataToPlot({
    points: props.points,
    stats: props.stats,
    partCntPerPoint: props.partCntPerPoint,
  });

  const pointerLeave = useCallback(() => {
    setTooltip(null);
    setHighlightStart(null);
  }, [setTooltip, setHighlightStart]);

  return (
    <div style={{ position: "relative" }} onPointerLeave={pointerLeave}>
      <Box
        sx={{ height: { xs: "calc(100vh - 320px)", md: "calc(100vh - 280px)", xl: "calc(100vh - 220px)" } }}
      >
        <ParentSize>
          {(parent) => (
            <CycleChartSvg
              {...props}
              containerHeight={parent.height}
              containerWidth={parent.width}
              series={series}
              median={median}
              yZoom={props.yZoom}
              setYZoom={props.setYZoom}
              highlightStart={highlightStart}
              setHighlightStart={setHighlightStart}
              showTooltip={setTooltip}
              disabledSeries={disabledSeries}
            />
          )}
        </ParentSize>
      </Box>
      <div style={{ display: "flex", flexWrap: "wrap" }}>
        <div style={{ color: "#6b6b76" }}>Click on a point for details</div>
        <div style={{ flexGrow: 1 }} />
        <ChartZoomButtons
          set_date_zoom_range={props.set_date_zoom_range}
          current_date_zoom={props.current_date_zoom}
          yZoom={props.yZoom}
          setYZoom={props.setYZoom}
          median={median}
        />
      </div>
      <Legend series={series} disabledSeries={disabledSeries} adjustDisabled={setDisabled} />
      <CycleChartTooltip
        tooltip={tooltip}
        seriesLabel={props.series_label}
        extraTooltip={props.extra_tooltip}
      />
    </div>
  );
});
