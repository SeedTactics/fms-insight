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
import { format, addDays } from "date-fns";
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
import Card from "@material-ui/core/Card";
import CardContent from "@material-ui/core/CardContent";
import CardHeader from "@material-ui/core/CardHeader";
import Select from "@material-ui/core/Select";
import Button from "@material-ui/core/Button";
import MenuItem from "@material-ui/core/MenuItem";
import { PartIdenticon } from "../station-monitor/Material";
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

interface CycleChartProps {
  readonly points: HashMap<string, ReadonlyArray<CycleChartPoint>>;
  readonly series_label: string;
  readonly extra_tooltip?: (point: CycleChartPoint) => ReadonlyArray<ExtraTooltip>;
  readonly default_date_range?: Date[];
}

interface CycleChartTooltip {
  readonly x: Date;
  readonly y: number;
  readonly series: string;
  readonly extra: ReadonlyArray<ExtraTooltip>;
}

interface ZoomRange {
  x_low: Date;
  x_high: Date;
  y_low: number;
  y_high: number;
}

interface CycleChartState {
  readonly tooltip?: CycleChartTooltip;
  readonly disabled_series: { [key: string]: boolean };
  readonly current_zoom_range: ZoomRange | null;
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

const CycleChart = withStyles(cycleChartStyles)(
  class CycleChartWithStyles extends React.PureComponent<
    CycleChartProps & WithStyles<typeof cycleChartStyles>,
    CycleChartState
  > {
    state = {
      tooltip: undefined,
      disabled_series: {},
      current_zoom_range: null,
      brushing: false
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

      let dateRange = this.props.default_date_range;
      if (dateRange === undefined) {
        const now = new Date();
        const oneMonthAgo = addDays(now, -30);
        dateRange = [now, oneMonthAgo];
      }

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
              this.state.current_zoom_range
                ? [this.state.current_zoom_range.x_low, this.state.current_zoom_range.x_high]
                : this.props.points.isEmpty()
                ? dateRange
                : undefined
            }
            yDomain={
              this.state.current_zoom_range
                ? [this.state.current_zoom_range.y_low, this.state.current_zoom_range.y_high]
                : this.props.points.isEmpty()
                ? [0, 60]
                : undefined
            }
          >
            <VerticalGridLines />
            <HorizontalGridLines />
            <XAxis tickLabelAngle={-45} />
            <YAxis />
            <Highlight
              onBrushStart={() => this.setState({ brushing: true })}
              onBrushEnd={(area: { left: Date; right: Date; bottom: number; top: number }) => {
                if (area) {
                  this.setState({
                    current_zoom_range: { x_low: area.left, x_high: area.right, y_low: area.bottom, y_high: area.top },
                    brushing: false
                  });
                } else {
                  this.setState({
                    brushing: false
                  });
                }
              }}
            />
            {seriesNames.map(series => (
              <MarkSeries
                key={series}
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
              <DiscreteColorLegend
                orientation="horizontal"
                items={seriesNames.map(s => ({ title: s, disabled: this.state.disabled_series[s] }))}
                onItemClick={this.toggleSeries}
              />
            </div>
            {this.state.current_zoom_range ? (
              <Button
                size="small"
                style={{ position: "absolute", right: 0, top: 0 }}
                onClick={() => this.setState({ current_zoom_range: null })}
              >
                Reset Zoom
              </Button>
            ) : (
              undefined
            )}
          </div>
        </div>
      );
    }
  }
);

export interface SelectableCycleChartProps {
  readonly points: HashMap<string, HashMap<string, ReadonlyArray<CycleChartPoint>>>;
  readonly select_label: string;
  readonly series_label: string;
  readonly card_label: string;
  readonly icon: JSX.Element;
  readonly default_date_range?: Date[];
  readonly selected?: string;
  readonly extra_tooltip?: (point: CycleChartPoint) => ReadonlyArray<ExtraTooltip>;
  readonly useIdenticon?: boolean;
  readonly setSelected: (s: string) => void;
}

export function SelectableCycleChart(props: SelectableCycleChartProps) {
  let validValue = props.selected !== undefined && props.points.containsKey(props.selected);
  function stripAfterDash(s: string): string {
    const idx = s.indexOf("-");
    if (idx >= 0) {
      return s.substring(0, idx);
    } else {
      return s;
    }
  }
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            {props.icon}
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>{props.card_label}</div>
            <div style={{ flexGrow: 1 }} />
            <Select
              name={props.card_label.replace(" ", "-") + "-cycle-chart-select"}
              autoWidth
              displayEmpty
              value={validValue ? props.selected : ""}
              onChange={e => props.setSelected(e.target.value)}
            >
              {validValue ? (
                undefined
              ) : (
                <MenuItem key={0} value="">
                  <em>Select {props.select_label}</em>
                </MenuItem>
              )}
              {props.points
                .keySet()
                .toArray({ sortOn: x => x })
                .map(n => (
                  <MenuItem key={n} value={n}>
                    <div style={{ display: "flex", alignItems: "center" }}>
                      {props.useIdenticon ? <PartIdenticon part={stripAfterDash(n)} size={30} /> : undefined}
                      <span style={{ marginRight: "1em" }}>{n}</span>
                    </div>
                  </MenuItem>
                ))}
            </Select>
          </div>
        }
      />
      <CardContent>
        <CycleChart
          points={props.points.get(props.selected || "").getOrElse(HashMap.empty())}
          series_label={props.series_label}
          default_date_range={props.default_date_range}
          extra_tooltip={props.extra_tooltip}
        />
      </CardContent>
    </Card>
  );
}
