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
import Radio from 'material-ui/Radio';
import TextField from 'material-ui/TextField';
import { FormControlLabel } from 'material-ui/Form';
import { connect } from 'react-redux';
import { startOfMonth, format, parse } from 'date-fns';

import * as events from '../data/events';
import { Store } from '../data/store';

const toolbarStyle = {
  'display': 'flex',
  'backgroundColor': '#E0E0E0',
  'paddingLeft': '24px',
  'paddingRight': '24px',
  'minHeight': '2.5em',
  'alignItems': 'center' as 'center',
  'justifyContent': 'space-evenly' as 'space-evenly'
};

export interface AnalysisSelectToolbarProps {
  period: events.AnalysisPeriod;
  period_month: Date;
  // tslint:disable-next-line:no-any
  analyzeLast30Days: () => any;
  // tslint:disable-next-line:no-any
  analyzeMonth: (month: Date) => any;
  // tslint:disable-next-line:no-any
  setMonth: (month: Date) => any;
}

export class AnalysisSelectToolbar extends React.PureComponent<AnalysisSelectToolbarProps, {temp_month?: Date}> {

  state = {temp_month: undefined};

  setTempMonth = (m: Date) => {
    this.setState({temp_month: startOfMonth(m)});
  }

  blurMonth = () => {
    var m = this.state.temp_month;
    if (m === undefined) { return; }
    if (this.props.period_month === m) { return; }

    if (this.props.period === events.AnalysisPeriod.SpecificMonth) {
      // if month type is selected, reload data
      this.props.analyzeMonth(m);
    } else {
      // otherwise, just store month
      this.props.setMonth(m);
    }

    this.setState({temp_month: undefined});
  }

  render() {
    const curMonth: Date = this.state.temp_month || this.props.period_month;

    return (
      <nav style={toolbarStyle}>
        <FormControlLabel
          control={
            <Radio
              checked={this.props.period === events.AnalysisPeriod.Last30Days}
              onChange={e => e.target.value ? this.props.analyzeLast30Days() : null}
            />
          }
          label="Last 30 days"
        />
        <div>
          <FormControlLabel
            control={
              <Radio
                checked={this.props.period === events.AnalysisPeriod.SpecificMonth}
                onChange={e => e.target.value ? this.props.analyzeMonth(curMonth) : null}
              />}
            label="Select Month"
          />
          <TextField
            type="month"
            value={format(curMonth, 'YYYY-MM')}
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
    period_month: s.Events.analysis_period_month,
  }),
  {
    analyzeLast30Days: events.analyzeLast30Days,
    analyzeMonth: events.analyzeSpecificMonth,
    setMonth: events.setAnalysisMonth
  }
)(AnalysisSelectToolbar);