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
import Radio from "@material-ui/core/Radio";
import TextField from "@material-ui/core/TextField";
import FormControlLabel from "@material-ui/core/FormControlLabel";
import { startOfMonth, format, parse } from "date-fns";

import * as events from "../data/events";
import * as gui from "../data/gui-state";
import { Store, connect } from "../store/store";

const toolbarStyle = {
  display: "flex",
  backgroundColor: "#E0E0E0",
  paddingLeft: "24px",
  paddingRight: "24px",
  minHeight: "2.5em",
  alignItems: "center" as "center",
  justifyContent: "space-evenly" as "space-evenly"
};

interface AnalysisSelectToolbarProps {
  period: events.AnalysisPeriod;
  period_month: Date;
  analyzeLast30Days: () => void;
  analyzeMonth: (month: Date) => void;
  setMonth: (month: Date) => void;
}

class AnalysisSelectToolbar extends React.PureComponent<AnalysisSelectToolbarProps, { temp_month?: Date }> {
  state = { temp_month: undefined } as { temp_month?: Date };

  setTempMonth = (m: Date) => {
    this.setState({ temp_month: startOfMonth(m) });
  };

  blurMonth = () => {
    const m = this.state.temp_month;
    if (m === undefined) {
      return;
    }
    if (this.props.period_month === m) {
      return;
    }

    if (this.props.period === events.AnalysisPeriod.SpecificMonth) {
      // if month type is selected, reload data
      this.props.analyzeMonth(m);
    } else {
      // otherwise, just store month
      this.props.setMonth(m);
    }

    this.setState({ temp_month: undefined });
  };

  render() {
    const curMonth: Date = this.state.temp_month || this.props.period_month;

    return (
      <nav style={toolbarStyle}>
        <FormControlLabel
          control={
            <Radio
              checked={this.props.period === events.AnalysisPeriod.Last30Days}
              onChange={(e, checked) => (checked ? this.props.analyzeLast30Days() : null)}
            />
          }
          label="Last 30 days"
        />
        <div style={{ display: "flex", alignItems: "center" }}>
          <FormControlLabel
            control={
              <Radio
                checked={this.props.period === events.AnalysisPeriod.SpecificMonth}
                onChange={(e, checked) => (checked ? this.props.analyzeMonth(curMonth) : null)}
              />
            }
            label="Select Month"
          />
          <TextField
            type="month"
            placeholder="Choose Month"
            value={format(curMonth, "YYYY-MM")}
            onChange={m => this.setTempMonth(parse(m.target.value))}
            onBlur={() => this.blurMonth()}
          />
        </div>
      </nav>
    );
  }
}

export default connect(
  (s: Store) => ({
    period: s.Events.analysis_period,
    period_month: s.Events.analysis_period_month
  }),
  {
    analyzeLast30Days: () => [
      events.analyzeLast30Days(),
      { type: gui.ActionType.SetStationCycleDateZoom, zoom: undefined },
      { type: gui.ActionType.SetPalletCycleDateZoom, zoom: undefined }
    ],
    analyzeMonth: (m: Date) => [
      ...events.analyzeSpecificMonth(m),
      { type: gui.ActionType.SetStationCycleDateZoom, zoom: undefined },
      { type: gui.ActionType.SetPalletCycleDateZoom, zoom: undefined }
    ],
    setMonth: (m: Date) => [
      events.setAnalysisMonth(m),
      { type: gui.ActionType.SetStationCycleDateZoom, zoom: undefined },
      { type: gui.ActionType.SetPalletCycleDateZoom, zoom: undefined }
    ]
  }
)(AnalysisSelectToolbar);
