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
import { connect } from 'react-redux';
import * as numerable from 'numeral';
import Grid from 'material-ui/Grid';
import List, { ListItem, ListItemText } from 'material-ui/List';
import Checkbox from 'material-ui/Checkbox';

import AnalysisSelectToolbar from './AnalysisSelectToolbar';
import * as events from '../data/events';
import { Store } from '../data/store';

export interface StationCycleChartProps {
  by_station: im.Map<string, ReadonlyArray<events.StationCycle>>;
}

interface StationCycleChartTooltip {
  readonly x: Date;
  readonly y: number;
  readonly stat: string;
}

interface StationCycleChartState {
  tooltip?: StationCycleChartTooltip;
}

function format_hint(tip: StationCycleChartTooltip) {
  return [
    {title: 'Time', value: format(tip.x, 'MMM D, YYYY, H:mm a')},
    {title: 'Station', value: tip.stat},
    {title: 'Cycle Time', value: numerable(tip.y).format('0.0') + " minutes"},
  ];
}

export class StationCycleChart extends React.Component<StationCycleChartProps, StationCycleChartState> {
  state = {}  as StationCycleChartState;

  setClosestPoint = (stat: string) => (point: events.StationCycle) => {
    if (this.state.tooltip === undefined) {
      this.setState({tooltip: {...point, stat: stat}});
    } else {
      this.setState({tooltip: undefined});
    }
  }

  clearTooltip = () => {
    this.setState({tooltip: undefined});
  }

  render() {
    const stations =
      this.props.by_station.toSeq()
      .sortBy((points, stat) => stat);
    return (
      <div>
        <FlexibleWidthXYPlot
            height={window.innerHeight - 250}
            xType="time"
            onMouseLeave={this.clearTooltip}
        >
          <VerticalGridLines/>
          <HorizontalGridLines/>
          <XAxis/>
          <YAxis/>
          {
            stations.map((points, stat) =>
              <MarkSeries
                key={stat}
                data={points}
                onValueClick={this.setClosestPoint(stat)}
              />
            ).toIndexedSeq()
          }
          {
            this.state.tooltip === undefined ? undefined :
              <Hint value={this.state.tooltip} format={format_hint}/>
          }
        </FlexibleWidthXYPlot>

        <div style={{textAlign: 'center'}}>
          <DiscreteColorLegend orientation="horizontal" items={stations.keySeq()}/>
        </div>
      </div>
    );
  }
}

export interface PartStationCycleProps {
  by_part: im.Map<string, im.Map<string, ReadonlyArray<events.StationCycle>>>;
}

export class PartStationCycleChart extends React.Component<PartStationCycleProps, {part?: string}> {
  state = {part: undefined} as {part?: string};

  setPart = (part: string) => () => {
    this.setState({part});
  }

  render() {
    return (
      <Grid container>
        <Grid item xs={12} md={2}>
          <List>
            {
              this.props.by_part.keySeq().sort().map(part =>
                <ListItem
                  button
                  key={part}
                  onClick={this.setPart(part)}
                >
                  <Checkbox
                    checked={this.state.part === part}
                    tabIndex={-1}
                    disableRipple
                  />
                  <ListItemText primary={part}/>
                </ListItem>
              )
            }
          </List>
        </Grid>
        <Grid item xs={12} md={10}>
          {
            this.state.part === undefined ? undefined :
              <StationCycleChart by_station={this.props.by_part.get(this.state.part, im.Map())}/>
          }
        </Grid>
      </Grid>
    );
  }
}

const ConnectedPartStationCycleChart = connect(
  (st: Store) => ({
    by_part: st.Events.last30.station_cycles.by_part_then_stat
  })
)(PartStationCycleChart);

export default function Efficiency() {
  return (
    <>
      <AnalysisSelectToolbar/>
      <main style={{'padding': '24px'}}>
        <Card>
          <CardHeader title="Station Cycles"/>
          <CardContent>
            <ConnectedPartStationCycleChart/>
          </CardContent>
        </Card>
      </main>
    </>
  );
}