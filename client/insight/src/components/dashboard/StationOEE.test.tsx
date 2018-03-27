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
import { StationOEE, StationOEEs, stationHoursInLastWeek } from './StationOEE';
import * as im from 'immutable';
import { shallow } from 'enzyme';

import * as api from '../../data/api';

it('calculates station hours', () => {
  const date = new Date();
  const hours = [
    {date, station: 'zzz', hours: 2},
    {date, station: 'abc', hours: 4},
    {date, station: 'abc', hours: 10},
    {date, station: 'zzz', hours: 5},
    {date, station: 'zzz', hours: 4},
    {date, station: 'abc', hours: 11},
  ];

  expect(stationHoursInLastWeek(im.List(hours)).toArray()).toEqual(
    [
      ["zzz", 2 + 5 + 4],
      ["abc", 4 + 10 + 11],
    ]
  );
});

it('displays station hours', () => {
  const hours = im.Map({
    "abc #1": 40,
    "zzz #1": 3,
  });
  const pals = im.Map({
    "aaa #1": {
      pal: {
        pallet: {
          pallet: "5",
          fixtureOnPallet: "",
          onHold: false,
          numFaces: 2,
          currentPalletLocation: new api.PalletLocation({
            loc: api.PalletLocationEnum.LoadUnload,
            group: "abc",
            num: 1
          }),
        },
        material: []
      },
    }
  });
  const val = shallow(
    <StationOEEs
      system_active_hours_per_week={100}
      station_active_hours_past_week={hours}
      pallets={pals}
    />);
  expect(val).toMatchSnapshot('station oee table');
});

it('displays a single station oee', () => {
  const pal = {
    pallet: {
      pallet: "7",
      fixtureOnPallet: "",
      onHold: false,
      numFaces: 2,
      currentPalletLocation: new api.PalletLocation({
        loc: api.PalletLocationEnum.LoadUnload,
        group: "aaa",
        num: 1,
      }),
    },
    material: [
      {
        materialID: 10,
        jobUnique: "aaa",
        partName: "aaa",
        process: 2,
        path: 1,
        signaledInspections: ["a", "b"],
        location: new api.InProcessMaterialLocation({
          type: api.LocType.OnPallet,
          pallet: "7",
          face: 1,
          queuePosition: 0,
        }),
        action: new api.InProcessMaterialAction({
          type: api.ActionType.Loading,
          loadOntoFace: 1,
          processAfterLoad: 2,
          pathAfterLoad: 1,
        }),
      }
    ]
  };

  const val = shallow(<StationOEE station="aaa" oee={0.43} pallet={pal}/>).dive().dive();
  expect(val).toMatchSnapshot('station oee gauge');
});

it('displays a single station without oee', () => {
  const val = shallow(<StationOEE station="bbb" oee={0.01}/>).dive().dive();
  expect(val).toMatchSnapshot('station empty guage');
});
