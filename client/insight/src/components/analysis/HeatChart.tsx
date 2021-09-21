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
import { Card } from "@material-ui/core";
import { CardContent } from "@material-ui/core";
import { CardHeader } from "@material-ui/core";
import { Select } from "@material-ui/core";
import { MenuItem } from "@material-ui/core";
import { Tooltip } from "@material-ui/core";
import { IconButton } from "@material-ui/core";
import ImportExport from "@material-ui/icons/ImportExport";

import { LazySeq } from "../../util/lazyseq";

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
    { title: labelTitle, value: p.label },
  ];
};

function tick_format(d: Date | string): string {
  if (typeof d === "string") {
    const ddd = new Date(d);
    return format(ddd, "iii MMM d");
  } else {
    return format(d, "iii MMM d");
  }
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
          style={{
            stroke: "white",
            strokeWidth: "2px",
            rectStyle: {
              rx: 10,
              ry: 10,
            },
          }}
        />
        {this.state.selected_point === undefined ? undefined : (
          <Hint value={this.state.selected_point} format={formatHint(this.props.y_title, this.props.label_title)} />
        )}
      </FlexibleWidthXYPlot>
    );
  }
}

export interface SelectableHeatChartProps<T extends string> {
  readonly icon: JSX.Element;
  readonly card_label: string;
  readonly y_title: string;
  readonly label_title: string;
  readonly cur_selected: T;
  readonly options: ReadonlyArray<T>;
  readonly setSelected?: (p: T) => void;
  readonly onExport: () => void;

  readonly points: ReadonlyArray<HeatChartPoint>;
}

// https://github.com/uber/react-vis/issues/1092
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(LabelSeries as any).propTypes = {};

export function SelectableHeatChart<T extends string>(props: SelectableHeatChartProps<T>) {
  const setSelected = props.setSelected;
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            {props.icon}
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>{props.card_label}</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton onClick={props.onExport} style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}>
                <ImportExport />
              </IconButton>
            </Tooltip>
            {setSelected ? (
              <Select
                name={props.card_label.replace(" ", "-") + "-heatchart-planned-or-actual"}
                autoWidth
                displayEmpty
                value={props.cur_selected}
                onChange={(e) => setSelected(e.target.value as T)}
              >
                {props.options.map((v, idx) => (
                  <MenuItem key={idx} value={v}>
                    {v}
                  </MenuItem>
                ))}
              </Select>
            ) : undefined}
          </div>
        }
      />
      <CardContent>
        <HeatChart
          points={props.points}
          y_title={props.y_title}
          label_title={props.label_title}
          row_count={LazySeq.ofIterable(props.points)
            .toSet((p) => p.y)
            .length()}
        />
      </CardContent>
    </Card>
  );
}
