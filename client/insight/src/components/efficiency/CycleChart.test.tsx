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
import { shallow } from 'enzyme';
import WorkIcon from '@material-ui/icons/Work';

import { CycleChart, CycleChartProps, SelectableCycleChartProps, SelectableCycleChart } from './CycleChart';

it('displays a cycle chart', () => {
  const props: CycleChartProps = {
    points:
      im.Map({
        "aaa": [{x: new Date(Date.UTC(2018, 1, 1)), y: 15}, {x: new Date(Date.UTC(2018, 1, 2)), y: 66}],
        "bbb": [{x: new Date(Date.UTC(2018, 2, 2)), y: 15}, {x: new Date(Date.UTC(2018, 2, 3)), y: 55}],
        "ccc": [{x: new Date(Date.UTC(2018, 3, 3)), y: 15}],
      }),
    series_label: "serieslabel"
  };

  const val = shallow(<CycleChart {...props}/>);
  expect(val).toMatchSnapshot('cycle chart');
});

it("displays an empty chart", () => {
  const props: CycleChartProps = {
    points: im.Map(),
    series_label: "serieslabel",
    default_date_range: [new Date(Date.UTC(2018, 2, 5)), new Date(Date.UTC(2018, 2, 10))],
  };

  const val = shallow(<CycleChart {...props}/>);
  expect(val).toMatchSnapshot('empty cycle chart');
});

const selectProps: SelectableCycleChartProps = {
    points:
      im.Map({
        "111": im.Map({
            "aaa": [{x: new Date(Date.UTC(2018, 1, 1)), y: 15}, {x: new Date(Date.UTC(2018, 1, 2)), y: 66}],
            "bbb": [{x: new Date(Date.UTC(2018, 2, 2)), y: 53}, {x: new Date(Date.UTC(2018, 2, 3)), y: 55}],
            "ccc": [{x: new Date(Date.UTC(2018, 3, 3)), y: 6}],
          }),
        "222": im.Map({
            "aaa": [{x: new Date(Date.UTC(2018, 4, 4)), y: 62}, {x: new Date(Date.UTC(2018, 4, 45)), y: 512}],
          }),
      }),
    series_label: "serieslabel",
    select_label: "sellabel",
    card_label: 'cardheader',
    icon: <WorkIcon/>,
    selected: undefined,
    setSelected: jest.fn()
  };

it('displays a selectable cycle chart with nothing selected', () => {
  const props = {...selectProps, selected: 'selectedval'};
  const val = shallow(<SelectableCycleChart {...props}/>);
  expect(val).toMatchSnapshot('selectable cycle chart with nothing selected');
});

it('displays a selectable cycle chart with something selected', () => {
  const props = {...selectProps, selected: '111'};
  const val = shallow(<SelectableCycleChart {...props}/>);
  expect(val).toMatchSnapshot('selectable cycle chart with something selected');
});
