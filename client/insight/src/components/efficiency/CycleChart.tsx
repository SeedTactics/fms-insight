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
import * as React from 'react';
import * as im from 'immutable';
import { format, addDays } from 'date-fns';
import { MarkSeries,
         XAxis,
         YAxis,
         Hint,
         FlexibleWidthXYPlot,
         VerticalGridLines,
         HorizontalGridLines,
         DiscreteColorLegend
       } from 'react-vis';
import Card from '@material-ui/core/Card';
import CardContent from '@material-ui/core/CardContent';
import CardHeader from '@material-ui/core/CardHeader';
import * as numerable from 'numeral';
import Select from '@material-ui/core/Select';
import MenuItem from '@material-ui/core/MenuItem';
import { PartIdenticon } from '../station-monitor/Material';

export interface CycleChartPoint {
  readonly x: Date;
  readonly y: number;
}

export interface CycleChartProps {
  readonly points: im.Map<string, ReadonlyArray<CycleChartPoint>>;
  readonly series_label: string;
  readonly default_date_range?: Date[];
}

interface CycleChartTooltip {
  readonly x: Date;
  readonly y: number;
  readonly series: string;
}

interface CycleChartState {
  readonly tooltip?: CycleChartTooltip;
  readonly disabled_series: { [key: string]: boolean };
}

function memoize<A, R>(f: (x: A) => R): ((x: A) => R) {
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

export class CycleChart extends React.PureComponent<CycleChartProps, CycleChartState> {
  state = {
    tooltip: undefined,
    disabled_series: {}
  } as CycleChartState;

  // memoize on the series name, since the function from CycleChartPoint => void is
  // passed as a prop to the chart, and reusing the same function keeps the props
  // unchanged so PureComponent can avoid a re-render
  setTooltip = memoize((series: string) => (point: CycleChartPoint) => {
    if (this.state.tooltip === undefined) {
      this.setState({tooltip: {...point, series: series}});
    } else {
      this.setState({tooltip: undefined});
    }
  });

  clearTooltip = () => {
    this.setState({tooltip: undefined});
  }

  toggleSeries = (series: {title: string}) => {
    const newState = !!!this.state.disabled_series[series.title];
    this.setState({
      disabled_series: {...this.state.disabled_series,
        [series.title]: newState
      }
    });
  }

  formatHint = (tip: CycleChartTooltip) => {
    return [
      {title: 'Time', value: format(tip.x, 'MMM D, YYYY, H:mm a')},
      {title: this.props.series_label, value: tip.series},
      {title: 'Cycle Time', value: numerable(tip.y).format('0.0') + " minutes"},
    ];
  }

  render() {
    const seriesNames =
      this.props.points.toSeq()
      .sortBy((points, s) => s);

    let dateRange = this.props.default_date_range;
    if (dateRange === undefined) {
      const now = new Date();
      const oneMonthAgo = addDays(now, -30);
      dateRange = [now, oneMonthAgo];
    }

    return (
      <div>
        <FlexibleWidthXYPlot
            height={window.innerHeight - 200}
            xType="time"
            margin={{bottom: 50}}
            onMouseLeave={this.clearTooltip}
            dontCheckIfEmpty
            xDomain={this.props.points.isEmpty() ? dateRange : undefined}
            yDomain={this.props.points.isEmpty() ? [0, 60] : undefined}
        >
          <VerticalGridLines/>
          <HorizontalGridLines/>
          <XAxis tickLabelAngle={-45}/>
          <YAxis/>
          {
            seriesNames.map((points, series) =>
              <MarkSeries
                key={series}
                data={points}
                onValueClick={this.state.disabled_series[series] ? undefined : this.setTooltip(series)}
                {...(this.state.disabled_series[series] ? {opacity: 0.2} : null)}
              />
            ).toIndexedSeq()
          }
          {
            this.state.tooltip === undefined ? undefined :
              <Hint value={this.state.tooltip} format={this.formatHint}/>
          }
        </FlexibleWidthXYPlot>

        <div style={{textAlign: 'center'}}>
          <DiscreteColorLegend
            orientation="horizontal"
            items={
              seriesNames.keySeq().map(s =>
                ({title: s, disabled: this.state.disabled_series[s]})
              ).toArray()
            }
            onItemClick={this.toggleSeries}
          />
        </div>
      </div>
    );
  }
}

export interface SelectableCycleChartProps {
  readonly points: im.Map<string, im.Map<string, ReadonlyArray<CycleChartPoint>>>;
  readonly select_label: string;
  readonly series_label: string;
  readonly card_label: string;
  readonly icon: JSX.Element;
  readonly default_date_range?: Date[];
  readonly selected?: string;
  readonly useIdenticon?: boolean;
  readonly setSelected: (s: string) => void;
}

export function SelectableCycleChart(props: SelectableCycleChartProps) {
  let validValue = props.selected !== undefined && props.points.has(props.selected);
  function stripAfterDash(s: string): string {
    const idx = s.indexOf('-');
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
          <div style={{display: 'flex', flexWrap: 'wrap', alignItems: 'center'}}>
            {props.icon}
            <div style={{marginLeft: '10px', marginRight: '3em'}}>
              {props.card_label}
            </div>
            <div style={{flexGrow: 1}}/>
            <Select
              autoWidth
              displayEmpty
              value={validValue ? props.selected : ""}
              onChange={e => props.setSelected(e.target.value)}
            >
              {
                validValue ? undefined :
                  <MenuItem key={0} value=""><em>Select {props.select_label}</em></MenuItem>
              }
              {
                props.points.keySeq().sort().map(n =>
                  <MenuItem key={n} value={n}>
                    <div style={{display: "flex", alignItems: "center"}}>
                      <PartIdenticon part={stripAfterDash(n)} size={30}/>
                      <span style={{marginRight: '1em'}}>{n}</span>
                    </div>
                  </MenuItem>
                )
              }
            </Select>
          </div>}
      />
      <CardContent>
        <CycleChart
          points={props.points.get(props.selected || "", im.Map())}
          series_label={props.series_label}
          default_date_range={props.default_date_range}
        />
      </CardContent>
    </Card>
  );
  }