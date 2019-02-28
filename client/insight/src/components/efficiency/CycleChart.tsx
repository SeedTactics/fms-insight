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
import Dialog from "@material-ui/core/Dialog";
import DialogTitle from "@material-ui/core/DialogTitle";
import DialogContent from "@material-ui/core/DialogContent";
import DialogContentText from "@material-ui/core/DialogContentText";
import TextField from "@material-ui/core/TextField";
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
  readonly set_date_zoom_range: (p: { zoom?: { start: Date; end: Date } }) => void;
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
  readonly zoom_dialog_open: boolean;
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

function encodeDateForInput(d: Date): string {
  return format(d, "YYYY-MM-DD");
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
            <Highlight
              onBrushStart={() => this.setState({ brushing: true })}
              onBrushEnd={(area: { left: Date; right: Date; bottom: number; top: number }) => {
                if (area) {
                  this.props.set_date_zoom_range({ zoom: { start: area.left, end: area.right } });
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
              {seriesNames.length > 1 ? (
                <DiscreteColorLegend
                  orientation="horizontal"
                  items={seriesNames.map(s => ({ title: s, disabled: this.state.disabled_series[s] }))}
                  onItemClick={this.toggleSeries}
                />
              ) : (
                undefined
              )}
            </div>
            {this.props.current_date_zoom || this.state.current_y_zoom_range ? (
              <Button
                size="small"
                style={{ position: "absolute", right: "10em", top: 0 }}
                onClick={() => {
                  this.props.set_date_zoom_range({ zoom: undefined });
                  this.setState({ current_y_zoom_range: null });
                }}
              >
                Reset Zoom
              </Button>
            ) : (
              undefined
            )}
            <Button
              size="small"
              style={{ position: "absolute", right: 0, top: 0 }}
              onClick={() => this.setState({ zoom_dialog_open: true })}
            >
              Set Zoom
            </Button>
          </div>
          <Dialog onClose={() => this.setState({ zoom_dialog_open: false })} open={this.state.zoom_dialog_open}>
            <DialogTitle>Set Zoom</DialogTitle>
            <DialogContent>
              <DialogContentText>
                <em>You can also zoom by clicking and dragging on the chart.</em>
              </DialogContentText>
              <div>
                <div style={{ marginTop: "0.5em" }}>
                  <TextField
                    label="Starting Day"
                    type="date"
                    inputProps={{ step: 1 }}
                    value={encodeDateForInput(
                      this.props.current_date_zoom ? this.props.current_date_zoom.start : dateRange[0]
                    )}
                    onChange={e =>
                      this.props.set_date_zoom_range({
                        zoom: {
                          start: new Date((e.target as HTMLInputElement).valueAsDate),
                          end: this.props.current_date_zoom ? this.props.current_date_zoom.end : dateRange[1]
                        }
                      })
                    }
                  />
                </div>
                <div style={{ marginTop: "0.5em" }}>
                  <TextField
                    label="Ending Day"
                    type="date"
                    inputProps={{ step: 1 }}
                    value={encodeDateForInput(
                      this.props.current_date_zoom ? this.props.current_date_zoom.end : dateRange[1]
                    )}
                    onChange={e =>
                      this.props.set_date_zoom_range({
                        zoom: {
                          start: this.props.current_date_zoom ? this.props.current_date_zoom.start : dateRange[0],
                          end: new Date((e.target as HTMLInputElement).valueAsDate)
                        }
                      })
                    }
                  />
                </div>
                <div style={{ marginTop: "0.5em" }}>
                  <TextField
                    label="Low Y Value (min)"
                    type="number"
                    inputProps={{ step: 1 }}
                    placeholder="auto"
                    value={this.state.current_y_zoom_range ? this.state.current_y_zoom_range.y_low : ""}
                    onChange={e =>
                      this.setState({
                        current_y_zoom_range: {
                          y_low: parseInt(e.target.value, 10),
                          y_high: this.state.current_y_zoom_range ? this.state.current_y_zoom_range.y_high : 60
                        }
                      })
                    }
                  />
                </div>
                <div style={{ marginTop: "0.5em" }}>
                  <TextField
                    label="High Y Value (min)"
                    type="number"
                    inputProps={{ step: 1 }}
                    placeholder="auto"
                    value={this.state.current_y_zoom_range ? this.state.current_y_zoom_range.y_high : ""}
                    onChange={e =>
                      this.setState({
                        current_y_zoom_range: {
                          y_low: this.state.current_y_zoom_range ? this.state.current_y_zoom_range.y_low : 0,
                          y_high: parseInt(e.target.value, 10)
                        }
                      })
                    }
                  />
                </div>
              </div>
            </DialogContent>
          </Dialog>
        </div>
      );
    }
  }
);
