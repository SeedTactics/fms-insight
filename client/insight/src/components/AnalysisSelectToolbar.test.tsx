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
import { shallow } from 'enzyme';

import { AnalysisSelectToolbar } from './AnalysisSelectToolbar';
import * as events from '../data/events';

it('displays the toolbar', () => {
  const analyzeLast30 = jest.fn();
  const analyzeMonth = jest.fn();
  const setMonth = jest.fn();

  let val = shallow(
    <AnalysisSelectToolbar
      period={events.AnalysisPeriod.Last30Days}
      period_month={new Date(2018, 0, 1)}
      analyzeLast30Days={analyzeLast30}
      analyzeMonth={analyzeMonth}
      setMonth={setMonth}
    />);
  expect(val).toMatchSnapshot('last 30 selected with january');

  val = shallow(
    <AnalysisSelectToolbar
      period={events.AnalysisPeriod.SpecificMonth}
      period_month={new Date(2018, 1, 1)}
      analyzeLast30Days={analyzeLast30}
      analyzeMonth={analyzeMonth}
      setMonth={setMonth}
    />);
  expect(val).toMatchSnapshot('specific month with february');
});

it('operates when period is last 30', () => {
  const month = new Date(2018, 1, 1);
  const analyzeLast30 = jest.fn();
  const analyzeMonth = jest.fn();
  const setMonth = jest.fn();

  let val = shallow(
    <AnalysisSelectToolbar
      period={events.AnalysisPeriod.Last30Days}
      period_month={month}
      analyzeLast30Days={analyzeLast30}
      analyzeMonth={analyzeMonth}
      setMonth={setMonth}
    />);

  // tslint:disable-next-line
  const textFieldChange = val.find('TextField').prop('onChange') as any;
  // tslint:disable-next-line
  const textFieldBlur = val.find('TextField').prop('onBlur') as any;

  // switch to month
  val
    .find({label: "Select Month"})
    .prop("control")
    .props.onChange(null, {target: {value: true}});
  expect(analyzeMonth).toHaveBeenCalledWith(month);
  analyzeMonth.mockReset();

  // change month should set but not analyze
  textFieldChange({target: {value: '2018-05'}});
  expect(setMonth).not.toHaveBeenCalled();
  expect(analyzeMonth).not.toHaveBeenCalled();

  textFieldBlur();
  expect(setMonth).toHaveBeenCalledWith(new Date(2018, 4, 1));
  expect(analyzeMonth).not.toHaveBeenCalled();
  setMonth.mockReset();
});

it("operates when period is specific month", () => {
  const month = new Date(2018, 2, 1);
  const analyzeLast30 = jest.fn();
  const analyzeMonth = jest.fn();
  const setMonth = jest.fn();

  let val = shallow(
    <AnalysisSelectToolbar
      period={events.AnalysisPeriod.SpecificMonth}
      period_month={month}
      analyzeLast30Days={analyzeLast30}
      analyzeMonth={analyzeMonth}
      setMonth={setMonth}
    />);

  // tslint:disable-next-line
  const textFieldChange = val.find('TextField').prop('onChange') as any;
  // tslint:disable-next-line
  const textFieldBlur = val.find('TextField').prop('onBlur') as any;

  // now set back to last 30 days
  val
    .find({label: "Last 30 days"})
    .prop("control")
    .props.onChange(null, {target: {value: true}});
  expect(analyzeLast30).toHaveBeenCalled();
  analyzeLast30.mockReset();

  // change month
  textFieldChange({target: {value: '2018-06'}});
  expect(setMonth).not.toHaveBeenCalled();
  expect(analyzeMonth).not.toHaveBeenCalled();

  // blur month, which should reload
  textFieldBlur();
  expect(setMonth).not.toHaveBeenCalled();
  expect(analyzeMonth).toHaveBeenCalledWith(new Date(2018, 5, 1));
  analyzeMonth.mockReset();
});