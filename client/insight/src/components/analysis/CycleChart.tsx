/* Copyright (c) 2018, John Lenz

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
  DiscreteColorLegend
} from "react-vis";
import Button from "@material-ui/core/Button";
import { createStyles, withStyles, WithStyles } from "@material-ui/core";
import { HashMap } from "prelude-ts";

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
}

interface CycleChartTooltip {
  readonly x: Date;
  readonly y: number;
  readonly series: string;
  readonly extra: ReadonlyArray<ExtraTooltip>;
}

interface YZoomRange {
  y_low: number;
  y_high: number;
}

interface CycleChartState {
  readonly tooltip?: CycleChartTooltip;
  readonly disabled_series: { [key: string]: boolean };
  readonly current_y_zoom_range: YZoomRange | null;
  readonly brushing: boolean;
}

function memoize<A, R>(f: (x: A) => R): (x: A) => R {
  let memo = new Map<A, R>();
  return x => {
    let ret = memo.get(x);
    if (!ret) {
      ret = f(x);
      memo.set(x, ret);
    }
    return ret;
  };
}

// https://github.com/uber/react-vis/issues/1067
const cycleChartStyles = createStyles({
  noSeriesPointerEvts: {
    "& .rv-xy-plot__series.rv-xy-plot__series--mark": {
      pointerEvents: "none"
    }
  }
});

// https://personal.sron.nl/~pault/
const paulTolSeqColors = [
  ["4477aa"],
  ["4477aa", "cc6677"],
  ["4477aa", "ddcc77", "cc6677"],
  ["4477aa", "117733", "ddcc77", "cc6677"],
  ["332288", "88ccee", "117733", "ddcc77", "cc6677"],
  ["332288", "88ccee", "117733", "ddcc77", "cc6677", "aa4499"],
  ["332288", "88ccee", "44aa99", "117733", "ddcc77", "cc6677", "aa4499"],
  ["332288", "88ccee", "44aa99", "117733", "999933", "ddcc77", "cc6677", "aa4499"],
  ["332288", "88ccee", "44aa99", "117733", "999933", "ddcc77", "cc6677", "882255", "aa4499"],
  ["332288", "88ccee", "44aa99", "117733", "999933", "ddcc77", "661100", "cc6677", "882255", "aa4499"],
  ["332288", "6699cc", "88ccee", "44aa99", "117733", "999933", "ddcc77", "661100", "cc6677", "882255", "aa4499"],
  [
    "332288",
    "6699cc",
    "88ccee",
    "44aa99",
    "117733",
    "999933",
    "ddcc77",
    "661100",
    "cc6677",
    "aa4466",
    "882255",
    "aa4499"
  ]
];

function paulTolColor(idx: number, count: number): string {
  return "#" + paulTolSeqColors[count - 1][idx];
}

// https://github.com/google/palette.js/blob/master/palette.js
const mpn65Colors = [
  "ff0029",
  "377eb8",
  "66a61e",
  "984ea3",
  "00d2d5",
  "ff7f00",
  "af8d00",
  "7f80cd",
  "b3e900",
  "c42e60",
  "a65628",
  "f781bf",
  "8dd3c7",
  "bebada",
  "fb8072",
  "80b1d3",
  "fdb462",
  "fccde5",
  "bc80bd",
  "ffed6f",
  "c4eaff",
  "cf8c00",
  "1b9e77",
  "d95f02",
  "e7298a",
  "e6ab02",
  "a6761d",
  "0097ff",
  "00d067",
  "000000",
  "252525",
  "525252",
  "737373",
  "969696",
  "bdbdbd",
  "f43600",
  "4ba93b",
  "5779bb",
  "927acc",
  "97ee3f",
  "bf3947",
  "9f5b00",
  "f48758",
  "8caed6",
  "f2b94f",
  "eff26e",
  "e43872",
  "d9b100",
  "9d7a00",
  "698cff",
  "d9d9d9",
  "00d27e",
  "d06800",
  "009f82",
  "c49200",
  "cbe8ff",
  "fecddf",
  "c27eb6",
  "8cd2ce",
  "c4b8d9",
  "f883b0",
  "a49100",
  "f48800",
  "27d0df",
  "a04a9b"
];

function seriesColor(idx: number, count: number): string {
  if (count <= 12) {
    return paulTolColor(idx, count);
  } else {
    return mpn65Colors[idx % mpn65Colors.length];
  }
}

export const CycleChart = withStyles(cycleChartStyles)(
  class CycleChartWithStyles extends React.PureComponent<
    CycleChartProps & WithStyles<typeof cycleChartStyles>,
    CycleChartState
  > {
    state = {
      tooltip: undefined,
      disabled_series: {},
      current_y_zoom_range: null,
      brushing: false,
      zoom_dialog_open: false
    } as CycleChartState;

    // memoize on the series name, since the function from CycleChartPoint => void is
    // passed as a prop to the chart, and reusing the same function keeps the props
    // unchanged so PureComponent can avoid a re-render
    setTooltip = memoize((series: string) => (point: CycleChartPoint) => {
      if (this.state.tooltip === undefined) {
        this.setState({
          tooltip: { ...point, series: series, extra: this.props.extra_tooltip ? this.props.extra_tooltip(point) : [] }
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
      const newState = !!!this.state.disabled_series[series.title];
      this.setState({
        disabled_series: {
          ...this.state.disabled_series,
          [series.title]: newState
        }
      });
    };

    formatHint = (tip: CycleChartTooltip) => {
      return [
        { title: "Time", value: format(tip.x, "MMM D, YYYY, H:mm a") },
        { title: this.props.series_label, value: tip.series },
        { title: "Cycle Time", value: tip.y.toFixed(1) + " minutes" },
        ...tip.extra.map(e => ({
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
          )
        }))
      ];
    };

    render() {
      const seriesNames = this.props.points.keySet().toArray({ sortOn: x => x });
      const dateRange = this.props.default_date_range;
      const setZoom = this.props.set_date_zoom_range;

      return (
        <div className={this.state.brushing ? this.props.classes.noSeriesPointerEvts : undefined}>
          <FlexibleWidthXYPlot
            height={window.innerHeight - 200}
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
            {setZoom ? (
              <Highlight
                onBrushStart={() => this.setState({ brushing: true })}
                onBrushEnd={(area: { left: Date; right: Date; bottom: number; top: number }) => {
                  if (area) {
                    setZoom({ zoom: { start: area.left, end: area.right } });
                    this.setState({
                      current_y_zoom_range: { y_low: area.bottom, y_high: area.top },
                      brushing: false
                    });
                  } else {
                    this.setState({
                      brushing: false
                    });
                  }
                }}
              />
            ) : (
              undefined
            )}
            {seriesNames.map((series, idx) => (
              <MarkSeries
                key={series}
                color={seriesColor(idx, seriesNames.length)}
                data={this.props.points.get(series).getOrElse([])}
                onValueClick={this.state.disabled_series[series] ? undefined : this.setTooltip(series)}
                {...(this.state.disabled_series[series] ? { opacity: 0.2 } : null)}
              />
            ))}
            {this.state.tooltip === undefined ? (
              undefined
            ) : (
              <Hint value={this.state.tooltip} format={this.formatHint} />
            )}
          </FlexibleWidthXYPlot>

          <div style={{ position: "relative" }}>
            <div style={{ textAlign: "center" }}>
              {seriesNames.length > 1 ? (
                <DiscreteColorLegend
                  orientation="horizontal"
                  items={seriesNames.map((s, idx) => ({
                    title: s,
                    color: seriesColor(idx, seriesNames.length),
                    disabled: this.state.disabled_series[s]
                  }))}
                  onItemClick={this.toggleSeries}
                />
              ) : (
                undefined
              )}
            </div>
            {setZoom && (this.props.current_date_zoom || this.state.current_y_zoom_range) ? (
              <Button
                size="small"
                style={{ position: "absolute", right: 0, top: 0 }}
                onClick={() => {
                  setZoom({ zoom: undefined });
                  this.setState({ current_y_zoom_range: null });
                }}
              >
                Reset Zoom
              </Button>
            ) : (
              undefined
            )}
            {setZoom && !this.props.current_date_zoom && !this.state.current_y_zoom_range ? (
              <span style={{ position: "absolute", right: 0, top: 0, color: "#6b6b76" }}>Zoom via mouse drag</span>
            ) : (
              undefined
            )}
          </div>
        </div>
      );
    }
  }
);
