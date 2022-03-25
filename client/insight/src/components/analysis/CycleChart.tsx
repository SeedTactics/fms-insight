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
import {
  MarkSeries,
  XAxis,
  YAxis,
  Hint,
  Highlight,
  FlexibleWidthXYPlot,
  VerticalGridLines,
  HorizontalGridLines,
  DiscreteColorLegend,
  AreaSeries,
  LineSeries,
} from "react-vis";
import { Button, styled, ToggleButton } from "@mui/material";
import { HashMap } from "prelude-ts";
import { Dialog } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogActions } from "@mui/material";
import { TextField } from "@mui/material";
import { IconButton } from "@mui/material";
import ZoomIn from "@mui/icons-material/ZoomIn";
import { StatisticalCycleTime } from "../../cell-status/estimated-cycle-times";
import {
  AnimatedAreaSeries,
  AnimatedAxis,
  AnimatedGlyphSeries,
  AnimatedGrid,
  AnimatedLineSeries,
  DataContext,
  GlyphProps,
  Tooltip,
  TooltipContext,
  TooltipContextType,
  XYChart,
} from "@visx/xychart";
import { chartTheme, seriesColor } from "../../util/chart-colors";
import { grey } from "@mui/material/colors";
import { useImmer } from "../../util/recoil-util";
import { localPoint } from "@visx/event";

interface YZoomRange {
  readonly y_low: number;
  readonly y_high: number;
}

interface SetZoomDialogProps {
  readonly open: boolean;
  readonly curZoom: YZoomRange | null;
  readonly close: () => void;
  readonly setLow: (r: number) => void;
  readonly setHigh: (r: number) => void;
}

function SetZoomDialog(props: SetZoomDialogProps) {
  const [low, setLow] = React.useState<number>();
  const [high, setHigh] = React.useState<number>();

  function close() {
    props.close();
    setLow(undefined);
    setHigh(undefined);
  }

  return (
    <Dialog open={props.open} onClose={close}>
      <DialogContent>
        <div style={{ marginBottom: "1em" }}>
          <TextField
            type="number"
            label="Y Low"
            value={low !== undefined ? (isNaN(low) ? "" : low) : props.curZoom?.y_low ?? ""}
            onChange={(e) => setLow(parseFloat(e.target.value))}
            onBlur={() => {
              if (low) {
                props.setLow(low);
              }
            }}
          />
        </div>
        <div style={{ marginBottom: "1em" }}>
          <TextField
            type="number"
            label="Y High"
            value={high !== undefined ? (isNaN(high) ? "" : high) : props.curZoom?.y_high ?? ""}
            onChange={(e) => setHigh(parseFloat(e.target.value))}
            onBlur={() => {
              if (high) {
                props.setHigh(high);
              }
            }}
          />
        </div>
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}

function RenderChartPoint(props: GlyphProps<CycleChartPoint>) {
  const { showTooltip } = React.useContext(TooltipContext as React.Context<TooltipContextType<CycleChartPoint>>) ?? {};
  const show = React.useCallback(
    (event: React.PointerEvent<SVGCircleElement>) =>
      showTooltip({
        key: props.key,
        index: props.index,
        datum: props.datum,
        svgPoint: localPoint(event) ?? undefined,
        event,
      }),
    [props.key, props.index, props.datum, props.x, props.y, showTooltip]
  );
  return (
    <circle
      className="visx-circle-glyph"
      key={props.key}
      tabIndex={props.onBlur || props.onFocus ? 0 : undefined}
      fill={props.color}
      r={props.size / 2}
      cx={props.x}
      cy={props.y}
      onClick={show}
    />
  );
}

function ChartMouseEvents() {
  const { hideTooltip } = React.useContext(TooltipContext as React.Context<TooltipContextType<CycleChartPoint>>) ?? {};
  const { height, width } = React.useContext(DataContext);

  // onMouseLeave triggers when mousing over something else such as an axis line or another point since this rect
  // is at the bottom.  Should instead put onMouseLeave on the <svg> element but to do so need to use a manaully
  // created TooltipProvider
  return (
    <>
      <rect x={0} y={0} width={width} height={height} fill="transparent" onMouseLeave={hideTooltip} />
    </>
  );
}

function ChartTooltip() {
  return <Tooltip renderTooltip={(data) => <div>{JSON.stringify(data)}</div>} />;
}

export interface CycleChartPoint {
  readonly x: Date;
  readonly y: number;
}

export interface ExtraTooltip {
  title: string;
  value: string;
  link?: () => void;
}

export interface CycleChartProps {
  readonly points: HashMap<string, ReadonlyArray<CycleChartPoint>>;
  readonly series_label: string;
  readonly extra_tooltip?: (point: CycleChartPoint) => ReadonlyArray<ExtraTooltip>;
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
  readonly stats?: StatisticalCycleTime;
  readonly partCntPerPoint?: number;
  readonly plannedSeries?: ReadonlyArray<CycleChartPoint>;
}

export const CycleChart2 = React.memo(function CycleChart(props: CycleChartProps) {
  const [chartHeight, setChartHeight] = React.useState(500);
  React.useEffect(() => {
    setChartHeight(window.innerHeight - 200);
  }, []);

  const [zoomDialogOpen, setZoomDialogOpen] = React.useState(false);
  const [yZoom, setYZoom] = React.useState<YZoomRange | null>(null);
  const [disabledSeries, adjustDisabled] = useImmer<ReadonlySet<string>>(new Set());

  const medianData = React.useMemo(() => {
    if (props.stats) {
      const low = (props.partCntPerPoint ?? 1) * (props.stats.medianMinutesForSingleMat - props.stats.MAD_belowMinutes);
      const high =
        (props.partCntPerPoint ?? 1) * (props.stats.medianMinutesForSingleMat + props.stats.MAD_aboveMinutes);

      return [
        { x: props.default_date_range[0], low, high },
        { x: props.default_date_range[1], low, high },
      ];
    } else {
      return null;
    }
  }, [props.stats, props.partCntPerPoint, props.default_date_range]);

  const seriesNames = props.points.keySet().toArray({ sortOn: (x) => x });

  return (
    <div>
      <XYChart
        height={chartHeight}
        xScale={
          props.current_date_zoom
            ? {
                type: "time",
                domain: [props.current_date_zoom.start, props.current_date_zoom.end],
              }
            : { type: "time" }
        }
        yScale={
          yZoom
            ? {
                type: "linear",
                domain: [yZoom.y_low, yZoom.y_high],
                zero: false,
              }
            : { type: "linear" }
        }
        captureEvents={false}
        theme={chartTheme}
      >
        <ChartMouseEvents />
        <AnimatedAxis orientation="bottom" />
        <AnimatedAxis orientation="left" label="Minutes" />
        <AnimatedGrid stroke="#e6e6e9" />
        {medianData ? (
          <AnimatedAreaSeries
            dataKey="__medianSeries"
            data={medianData}
            xAccessor={(p) => p.x}
            yAccessor={(p) => p.high}
            y0Accessor={(p) => p.low}
            fill={grey[700]}
            opacity={0.2}
            enableEvents={false}
          />
        ) : undefined}
        {props.plannedSeries ? (
          <AnimatedLineSeries
            dataKey="___plannedSeries"
            data={props.plannedSeries as CycleChartPoint[]}
            xAccessor={(p) => p.x}
            yAccessor={(p) => p.y}
            stroke="black"
            opacity={0.4}
            enableEvents={false}
          />
        ) : undefined}
        {seriesNames
          .map((series, idx) => ({ series, color: seriesColor(idx, seriesNames.length) }))
          .filter((s) => !disabledSeries.has(s.series))
          .map((s) => (
            <AnimatedGlyphSeries
              key={s.series}
              dataKey={s.series}
              colorAccessor={() => s.color}
              data={props.points.get(s.series).getOrElse([]) as CycleChartPoint[]}
              xAccessor={(p) => p.x}
              yAccessor={(p) => p.y}
              renderGlyph={RenderChartPoint}
              enableEvents={false}
            />
          ))}
        {props.points.isEmpty() ? (
          <AnimatedLineSeries
            dataKey="__emptyInvisibleSeries"
            stroke="transparent"
            data={props.default_date_range}
            xAccessor={(p) => p}
            yAccessor={(_) => 60}
            enableEvents={false}
          />
        ) : undefined}
        <ChartTooltip />
      </XYChart>
      <div style={{ display: "flex", flexWrap: "wrap", marginTop: "-30px" }}>
        <div style={{ color: "#6b6b76" }}>Click on a point for details</div>
        <div style={{ flexGrow: 1 }} />
        <div>
          {props.set_date_zoom_range && (props.current_date_zoom || yZoom) ? (
            <>
              <Button
                size="small"
                onClick={() => {
                  props.set_date_zoom_range?.({ zoom: undefined });
                  setYZoom(null);
                }}
              >
                Reset Zoom
              </Button>
              <IconButton size="small" onClick={() => setZoomDialogOpen(true)}>
                <ZoomIn fontSize="inherit" />
              </IconButton>
            </>
          ) : undefined}
          {props.set_date_zoom_range && !props.current_date_zoom && !yZoom ? (
            <span style={{ color: "#6b6b76" }}>
              Zoom via mouse drag
              {medianData ? (
                <>
                  <span> or </span>
                  <Button
                    size="small"
                    onClick={() => {
                      const high = medianData[0].high;
                      const low = medianData[0].low;
                      const extra = 0.2 * (high - low);
                      setYZoom({ y_low: low - extra, y_high: high + extra });
                    }}
                  >
                    Zoom To Inliers
                  </Button>
                </>
              ) : undefined}
              <IconButton size="small" onClick={() => setZoomDialogOpen(true)}>
                <ZoomIn fontSize="inherit" />
              </IconButton>
            </span>
          ) : undefined}
        </div>
      </div>
      <div style={{ marginTop: "1em", display: "flex", flexWrap: "wrap", justifyContent: "space-around" }}>
        {seriesNames.map((s, idx) => (
          <ToggleButton
            key={s}
            selected={!disabledSeries.has(s)}
            value={s}
            onChange={() => adjustDisabled((b) => (b.has(s) ? b.delete(s) : b.add(s)))}
          >
            <div style={{ display: "flex", alignItems: "center" }}>
              <div style={{ width: "14px", height: "14px", backgroundColor: seriesColor(idx, seriesNames.length) }} />
              <div style={{ marginLeft: "1em" }}>{s}</div>
            </div>
          </ToggleButton>
        ))}
      </div>
      <SetZoomDialog
        open={zoomDialogOpen}
        curZoom={yZoom}
        close={() => setZoomDialogOpen(false)}
        setLow={(v) => setYZoom((z) => (z ? { ...z, y_low: v } : { y_low: v, y_high: 60 }))}
        setHigh={(v) => setYZoom((z) => (z ? { ...z, y_high: v } : { y_high: v, y_low: 0 }))}
      />
    </div>
  );
});

interface CycleChartTooltip {
  readonly x: Date;
  readonly y: number;
  readonly series: string;
  readonly extra: ReadonlyArray<ExtraTooltip>;
}

interface CycleChartState {
  readonly tooltip?: CycleChartTooltip;
  readonly disabled_series: { [key: string]: boolean };
  readonly current_y_zoom_range: YZoomRange | null;
  readonly brushing: boolean;
  readonly zoom_dialog_open: boolean;
  readonly chart_height: number;
}

function memoize<A, R>(f: (x: A) => R): (x: A) => R {
  const memo = new Map<A, R>();
  return (x) => {
    let ret = memo.get(x);
    if (!ret) {
      ret = f(x);
      memo.set(x, ret);
    }
    return ret;
  };
}

// https://github.com/uber/react-vis/issues/1067
const NoSeriesPointerEvents = styled("div", { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })<{
  $noPtrEvents?: boolean;
}>(({ $noPtrEvents }) =>
  $noPtrEvents
    ? {
        "& .rv-xy-plot__series.rv-xy-plot__series--mark": {
          pointerevents: "none",
        },
      }
    : undefined
);

export class CycleChart extends React.PureComponent<CycleChartProps, CycleChartState> {
  state = {
    tooltip: undefined,
    disabled_series: {},
    current_y_zoom_range: null,
    brushing: false,
    zoom_dialog_open: false,
    chart_height: 500,
  } as CycleChartState;

  // memoize on the series name, since the function from CycleChartPoint => void is
  // passed as a prop to the chart, and reusing the same function keeps the props
  // unchanged so PureComponent can avoid a re-render
  setTooltip = memoize((series: string) => (point: CycleChartPoint) => {
    if (this.state.tooltip === undefined) {
      this.setState({
        tooltip: { ...point, series: series, extra: this.props.extra_tooltip ? this.props.extra_tooltip(point) : [] },
      });
    } else {
      this.setState({ tooltip: undefined });
    }
  });

  clearTooltip = (evt: React.MouseEvent) => {
    // onMouseLeave is triggered either when the mouse leaves the actual chart
    // or when the mouse moves over an "a" element inside the tooltip.  When moving
    // over an "a" element inside the tooltip, we do not want to clear it!
    if ((evt.relatedTarget as Element).tagName !== "A") {
      this.setState({ tooltip: undefined });
    }
  };

  toggleSeries = (series: { title: string }) => {
    const newState = !this.state.disabled_series[series.title];
    this.setState({
      disabled_series: {
        ...this.state.disabled_series,
        [series.title]: newState,
      },
    });
  };

  formatHint = (tip: CycleChartTooltip) => {
    return [
      { title: "Time", value: format(tip.x, "MMM d, yyyy, h:mm aaaa") },
      { title: this.props.series_label, value: tip.series },
      { title: "Cycle Time", value: tip.y.toFixed(1) + " minutes" },
      ...tip.extra.map((e) => ({
        title: e.title,
        value: e.link ? (
          <a
            style={{ color: "white", pointerEvents: "auto", cursor: "pointer", borderBottom: "1px solid" }}
            onClick={e.link}
          >
            {e.value}
          </a>
        ) : (
          e.value
        ),
      })),
    ];
  };

  setLowZoom = (val: number) => {
    let high: number | undefined = this.state.current_y_zoom_range?.y_high;
    if (high === undefined) {
      for (const [, points] of this.props.points) {
        for (const point of points) {
          if (!high || high < point.y) {
            high = point.y;
          }
        }
      }
    }
    this.setState({
      current_y_zoom_range: { y_low: val, y_high: high ?? 60 },
    });
  };

  setHighZoom = (val: number) =>
    this.setState({
      current_y_zoom_range: { y_low: this.state.current_y_zoom_range?.y_low ?? 0, y_high: val },
    });

  componentDidMount() {
    this.setState({
      chart_height: window.innerHeight - 200,
    });
  }

  render() {
    const seriesNames = this.props.points.keySet().toArray({ sortOn: (x) => x });
    const dateRange = this.props.default_date_range;
    const setZoom = this.props.set_date_zoom_range;

    let statsSeries: JSX.Element | undefined;
    let statZoom: JSX.Element | undefined;
    if (this.props.stats) {
      const low =
        (this.props.partCntPerPoint ?? 1) *
        (this.props.stats.medianMinutesForSingleMat - this.props.stats.MAD_belowMinutes);
      const high =
        (this.props.partCntPerPoint ?? 1) *
        (this.props.stats.medianMinutesForSingleMat + this.props.stats.MAD_aboveMinutes);

      statsSeries = (
        <AreaSeries
          color="gray"
          opacity={0.2}
          data={[
            {
              x: dateRange[0],
              y0: low,
              y: high,
            },
            {
              x: dateRange[1],
              y0: low,
              y: high,
            },
          ]}
        />
      );

      if (setZoom) {
        const extra = 0.2 * (high - low);
        statZoom = (
          <>
            <span> or </span>
            <Button
              size="small"
              onClick={() => {
                this.setState({ current_y_zoom_range: { y_low: low - extra, y_high: high + extra } });
              }}
            >
              Zoom To Inliers
            </Button>
          </>
        );
      }
    }

    let openZoom: JSX.Element | undefined;
    if (setZoom) {
      openZoom = (
        <IconButton size="small" onClick={() => this.setState({ zoom_dialog_open: true })}>
          <ZoomIn fontSize="inherit" />
        </IconButton>
      );
    }

    return (
      <NoSeriesPointerEvents $noPtrEvents={this.state.brushing}>
        <FlexibleWidthXYPlot
          height={this.state.chart_height}
          animation
          xType="time"
          margin={{ bottom: 50 }}
          onMouseLeave={this.clearTooltip}
          dontCheckIfEmpty
          xDomain={
            this.props.current_date_zoom
              ? [this.props.current_date_zoom.start, this.props.current_date_zoom.end]
              : this.props.points.isEmpty()
              ? dateRange
              : undefined
          }
          yDomain={
            this.state.current_y_zoom_range
              ? [this.state.current_y_zoom_range.y_low, this.state.current_y_zoom_range.y_high]
              : this.props.points.isEmpty()
              ? [0, 60]
              : undefined
          }
        >
          <VerticalGridLines />
          <HorizontalGridLines />
          <XAxis tickLabelAngle={-45} />
          <YAxis />
          {statsSeries}
          {this.props.plannedSeries ? (
            <LineSeries data={this.props.plannedSeries} color="black" opacity={0.4} />
          ) : undefined}
          {setZoom ? (
            <Highlight
              onBrushStart={() => this.setState({ brushing: true })}
              onBrushEnd={(area: { left: Date; right: Date; bottom: number; top: number }) => {
                if (area) {
                  setZoom({ zoom: { start: area.left, end: area.right } });
                  this.setState({
                    current_y_zoom_range: { y_low: area.bottom, y_high: area.top },
                    brushing: false,
                  });
                } else {
                  this.setState({
                    brushing: false,
                  });
                }
              }}
            />
          ) : undefined}
          {seriesNames
            .map((series, idx) => ({ series, color: seriesColor(idx, seriesNames.length) }))
            .filter((s) => !this.state.disabled_series[s.series])
            .map((s) => (
              <MarkSeries
                key={s.series}
                color={s.color}
                data={this.props.points.get(s.series).getOrElse([])}
                onValueClick={this.setTooltip(s.series)}
              />
            ))}
          {this.state.tooltip === undefined ? undefined : <Hint value={this.state.tooltip} format={this.formatHint} />}
        </FlexibleWidthXYPlot>

        <div style={{ position: "relative" }}>
          <div style={{ textAlign: "center" }}>
            {seriesNames.length > 1 ? (
              <DiscreteColorLegend
                orientation="horizontal"
                items={seriesNames.map((s, idx) => ({
                  title: s,
                  color: seriesColor(idx, seriesNames.length),
                  disabled: this.state.disabled_series[s],
                }))}
                onItemClick={this.toggleSeries}
              />
            ) : undefined}
          </div>
          {setZoom && (this.props.current_date_zoom || this.state.current_y_zoom_range) ? (
            <span style={{ position: "absolute", right: 0, top: 0, color: "#6b6b76" }}>
              <Button
                size="small"
                onClick={() => {
                  setZoom({ zoom: undefined });
                  this.setState({ current_y_zoom_range: null });
                }}
              >
                Reset Zoom
              </Button>
              {openZoom}
            </span>
          ) : undefined}
          {setZoom && !this.props.current_date_zoom && !this.state.current_y_zoom_range ? (
            <span style={{ position: "absolute", right: 0, top: 0, color: "#6b6b76" }}>
              Zoom via mouse drag
              {statZoom}
              {openZoom}
            </span>
          ) : undefined}
        </div>
        <SetZoomDialog
          open={this.state.zoom_dialog_open}
          curZoom={this.state.current_y_zoom_range}
          close={() => this.setState({ zoom_dialog_open: false })}
          setLow={this.setLowZoom}
          setHigh={this.setHighZoom}
        />
      </NoSeriesPointerEvents>
    );
  }
}
