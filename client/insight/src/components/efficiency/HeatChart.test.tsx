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
import WorkIcon from 'material-ui-icons/Work';

import { HeatChart, HeatChartProps, SelectableHeatChart, SelectableHeatChartProps } from './HeatChart';

import * as gui from '../../data/gui-state';

const points = [
  {x: new Date(2018, 2, 1), y: "aaa", color: 1},
  {x: new Date(2018, 2, 2), y: "aaa", color: 2},
  {x: new Date(2018, 2, 3), y: "aaa", color: 3},
  {x: new Date(2018, 2, 1), y: "bbb", color: 4},
  {x: new Date(2018, 2, 2), y: "bbb", color: 5},
  {x: new Date(2018, 2, 3), y: "bbb", color: 6},
];

it('displays a cycle chart', () => {
  const props: HeatChartProps = {
    points: points,
    color_label: "colorlabel",
    row_count: 10,
  };

  const val = shallow(<HeatChart {...props}/>);
  expect(val).toMatchSnapshot('heat chart');
});

it('displays a selectable heat chart with actual points', () => {
  const props: SelectableHeatChartProps = {
    points: points,
    color_label: "colorlabel",
    card_label: "cardlabel",
    icon: <WorkIcon/>,
    planned_or_actual: gui.PlannedOrActual.Actual,
    setType: jest.fn()
  };

  const val = shallow(<SelectableHeatChart {...props}/>);
  expect(val).toMatchSnapshot('selectable heat chart with actual data');
});

it('displays a selectable heat chart with planned points', () => {
  const props: SelectableHeatChartProps = {
    points: points,
    color_label: "colorlabel",
    card_label: "cardlabel",
    icon: <WorkIcon/>,
    planned_or_actual: gui.PlannedOrActual.Planned,
    setType: jest.fn()
  };

  const val = shallow(<SelectableHeatChart {...props}/>);
  expect(val).toMatchSnapshot('selectable heat chart with planned data');
});