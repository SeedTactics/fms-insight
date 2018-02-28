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

import * as gui from './gui-state';

it('creates initial state', () => {
  // tslint:disable no-any
  let s = gui.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(gui.initial);
});

it('selects a part for the station cycle', () => {
  let s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetSelectedStationCyclePart,
    part: "abcdef"
  });
  expect(s.station_cycle_selected_part).toEqual("abcdef");
});

it('selects a pallet for the pallet cycle chart', () => {
  let s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetSelectedPalletCycle,
    pallet: "pal555"
  });
  expect(s.pallet_cycle_selected).toEqual("pal555");
});

it("sets the station oee heatmap type", () => {
  let s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetStationOeeHeatmapType,
    ty: gui.PlannedOrActual.PlannedMinusActual
  });
  expect(s.station_oee_heatmap_type).toEqual(gui.PlannedOrActual.PlannedMinusActual);
});
