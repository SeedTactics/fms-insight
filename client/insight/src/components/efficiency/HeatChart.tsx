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
import { HeatmapSeries, XAxis, YAxis, Hint, FlexibleWidthXYPlot, LabelSeries } from "react-vis";
import Card from "@material-ui/core/Card";
import CardContent from "@material-ui/core/CardContent";
import CardHeader from "@material-ui/core/CardHeader";
import Select from "@material-ui/core/Select";
import MenuItem from "@material-ui/core/MenuItem";

import * as gui from "../../data/gui-state";
import { LazySeq } from "../../data/lazyseq";

export interface HeatChartPoint {
  readonly x: Date;
  readonly y: string;
  readonly color: number;
  readonly label: string;
}

interface HeatChartProps {
  readonly points: ReadonlyArray<HeatChartPoint>;
  readonly y_title: string;
  readonly row_count: number;
  readonly label_title: string;
}

interface HeatChartState {
  readonly selected_point?: HeatChartPoint;
}

const formatHint = (yTitle: string, labelTitle: string) => (p: HeatChartPoint) => {
  return [
    { title: yTitle, value: p.y },
    { title: "Day", value: p.x.toDateString() },
    { title: labelTitle, value: p.label }
  ];
};

function tick_format(d: Date): string {
  return format(d, "ddd MMM D");
}

class HeatChart extends React.PureComponent<HeatChartProps, HeatChartState> {
  state: HeatChartState = {};

  render() {
    return (
      <FlexibleWidthXYPlot
        height={this.props.row_count * 75}
        xType="ordinal"
        yType="ordinal"
        colorRange={["#E8F5E9", "#1B5E20"]}
        margin={{ bottom: 60, left: 100 }}
      >
        <XAxis tickFormat={tick_format} tickLabelAngle={-45} />
        <YAxis />
        <HeatmapSeries
          data={this.props.points}
          onValueMouseOver={(pt: HeatChartPoint) => this.setState({ selected_point: pt })}
          onValueMouseOut={() => this.setState({ selected_point: undefined })}
        />
        <LabelSeries
          style={{ pointerEvents: "none", fill: "#6b6b76" }}
          data={this.props.points}
          labelAnchorX="middle"
          labelAnchorY="baseline"
          getLabel={(d: HeatChartPoint) => d.label}
        />
        {this.state.selected_point === undefined ? (
          undefined
        ) : (
          <Hint value={this.state.selected_point} format={formatHint(this.props.y_title, this.props.label_title)} />
        )}
      </FlexibleWidthXYPlot>
    );
  }
}

export interface SelectableHeatChartProps {
  readonly icon: JSX.Element;
  readonly card_label: string;
  readonly y_title: string;
  readonly label_title: string;
  readonly planned_or_actual: gui.PlannedOrActual;
  readonly setType: (p: gui.PlannedOrActual) => void;

  readonly points: ReadonlyArray<HeatChartPoint>;
}

export function SelectableHeatChart(props: SelectableHeatChartProps) {
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            {props.icon}
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>{props.card_label}</div>
            <div style={{ flexGrow: 1 }} />
            <Select
              name={props.card_label.replace(" ", "-") + "-heatchart-planned-or-actual"}
              autoWidth
              displayEmpty
              value={props.planned_or_actual}
              onChange={e => props.setType(e.target.value as gui.PlannedOrActual)}
            >
              <MenuItem key={gui.PlannedOrActual.Actual} value={gui.PlannedOrActual.Actual}>
                Actual
              </MenuItem>
              <MenuItem key={gui.PlannedOrActual.Planned} value={gui.PlannedOrActual.Planned}>
                Planned
              </MenuItem>
            </Select>
          </div>
        }
      />
      <CardContent>
        <HeatChart
          points={props.points}
          y_title={props.y_title}
          label_title={props.label_title}
          row_count={LazySeq.ofIterable(props.points)
            .toSet(p => p.y)
            .length()}
        />
      </CardContent>
    </Card>
  );
}
