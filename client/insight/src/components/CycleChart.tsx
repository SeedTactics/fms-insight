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
import { format } from 'date-fns';
import { MarkSeries,
         XAxis,
         YAxis,
         Hint,
         FlexibleWidthXYPlot,
         VerticalGridLines,
         HorizontalGridLines,
         DiscreteColorLegend
       } from 'react-vis';
import Card, { CardHeader, CardContent } from 'material-ui/Card';
import * as numerable from 'numeral';
import Select from 'material-ui/Select';
import { MenuItem } from 'material-ui/Menu';

export interface CycleChartPoint {
  readonly x: Date;
  readonly y: number;
}

export interface CycleChartProps {
  points: im.Map<string, ReadonlyArray<CycleChartPoint>>;
  series_label: string;
}

interface CycleChartTooltip {
  readonly x: Date;
  readonly y: number;
  readonly series: string;
}

interface CycleChartState {
  tooltip?: CycleChartTooltip;
  disabled_series: { [key: string]: boolean };
}

export class CycleChart extends React.PureComponent<CycleChartProps, CycleChartState> {
  state = {
    tooltip: undefined,
    disabled_series: {}
  } as CycleChartState;

  setClosestPoint = (series: string) => (point: CycleChartPoint) => {
    if (this.state.tooltip === undefined) {
      this.setState({tooltip: {...point, series: series}});
    } else {
      this.setState({tooltip: undefined});
    }
  }

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
    return (
      <div>
        <FlexibleWidthXYPlot
            height={window.innerHeight - 200}
            xType="time"
            margin={{bottom: 50}}
            onMouseLeave={this.clearTooltip}
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
                onValueClick={this.setClosestPoint(series)}
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
  points: im.Map<string, im.Map<string, ReadonlyArray<CycleChartPoint>>>;
  select_label: string;
  series_label: string;
  card_label: string;
  icon: JSX.Element;
  selected?: string;
  setSelected: (s: string) => void;
}

export function SelectableCycleChart(props: SelectableCycleChartProps) {
  let validValue = props.selected !== undefined && props.points.has(props.selected);
  return (
    <Card>
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
                  <MenuItem key={n} value={n}>{n}</MenuItem>
                )
              }
            </Select>
          </div>}
      />
      <CardContent>
        <CycleChart
          points={props.points.get(props.selected || "", im.Map())}
          series_label={props.series_label}
        />
      </CardContent>
    </Card>
  );
  }