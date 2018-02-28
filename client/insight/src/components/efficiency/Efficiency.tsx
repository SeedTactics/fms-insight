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
import { connect } from 'react-redux';
import WorkIcon from 'material-ui-icons/Work';
import BasketIcon from 'material-ui-icons/ShoppingBasket';
import { addMonths, addDays } from 'date-fns';

import AnalysisSelectToolbar from '../AnalysisSelectToolbar';
import { SelectableCycleChart } from './CycleChart';
import * as events from '../../data/events';
import { Store } from '../../data/store';
import * as guiState from '../../data/gui-state';

export interface PartStationCycleChartProps {
  readonly points: im.Map<string, im.Map<string, ReadonlyArray<events.CycleData>>>;
  readonly default_date_range?: Date[];
  readonly selected?: string;
  readonly setSelected: (s: string) => void;
}

export function PartStationCycleChart(props: PartStationCycleChartProps) {
  return (
    <SelectableCycleChart
      select_label="Part"
      series_label="Station"
      card_label="Station Cycles"
      icon={<WorkIcon style={{color: "#6D4C41"}}/>}
      {...props}
    />
  );
}

const ConnectedPartStationCycleChart = connect(
  (st: Store) => {
    if (st.Events.analysis_period === events.AnalysisPeriod.Last30Days) {
      const now = new Date();
      const oneMonthAgo = addDays(now, -30);
      return {
        points: st.Events.last30.cycles.by_part_then_stat,
        selected: st.Gui.station_cycle_selected_part,
        default_date_range: [now, oneMonthAgo],
      };
    } else {
      return {
        points: st.Events.selected_month.cycles.by_part_then_stat,
        selected: st.Gui.station_cycle_selected_part,
        default_date_range: [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)]
      };
    }
  },
  {
    setSelected: (s: string) =>
      ({ type: guiState.ActionType.SetSelectedStationCyclePart, part: s})
  }
)(PartStationCycleChart);

export interface PalletCycleChartProps {
  readonly points: im.Map<string, ReadonlyArray<events.CycleData>>;
  readonly default_date_range?: Date[];
  readonly selected?: string;
  readonly setSelected: (s: string) => void;
}

export function PalletCycleChart(props: PalletCycleChartProps) {
  const points = props.points.map((cs, pal) => im.Map({[pal]: cs}));
  return (
    <SelectableCycleChart
      select_label="Pallet"
      series_label="Pallet"
      card_label="Pallet Cycles"
      icon={<BasketIcon style={{color: "#6D4C41"}}/>}
      selected={props.selected}
      setSelected={props.setSelected}
      points={points}
    />
  );
}

const ConnectedPalletCycleChart = connect(
  (st: Store) => {
    if (st.Events.analysis_period === events.AnalysisPeriod.Last30Days) {
      const now = new Date();
      const oneMonthAgo = addDays(now, -30);
      return {
        points: st.Events.last30.cycles.by_pallet,
        selected: st.Gui.pallet_cycle_selected,
        default_date_range: [now, oneMonthAgo],
      };
    } else {
      return {
        points: st.Events.selected_month.cycles.by_pallet,
        selected: st.Gui.pallet_cycle_selected,
        default_date_range: [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)]
      };
    }
  },
  {
    setSelected: (p: string) => ({ type: guiState.ActionType.SetSelectedPalletCycle, pallet: p })
  }
)(PalletCycleChart);

export default function Efficiency() {
  return (
    <>
      <AnalysisSelectToolbar/>
      <main style={{'padding': '24px'}}>
        <ConnectedPartStationCycleChart/>
        <div style={{marginTop: '3em'}}>
          <ConnectedPalletCycleChart/>
        </div>
      </main>
    </>
  );
}