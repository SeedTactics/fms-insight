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
import { connect } from 'react-redux';
import Select from 'material-ui/Select';
import { MenuItem } from 'material-ui/Menu';
import Input from 'material-ui/Input';

import * as routes from '../../data/routes';
import { Store } from '../../data/store';

const toolbarStyle = {
  'display': 'flex',
  'backgroundColor': '#E0E0E0',
  'paddingLeft': '24px',
  'paddingRight': '24px',
  'height': '2.5em',
  'alignItems': 'center' as 'center',
  'justifyContent': 'space-evenly' as 'space-evenly'
};

export interface StationToolbarProps {
  current_route: routes.State;
  // tslint:disable-next-line:no-any
  setStationRoute: (station: string, num: number) => any;
}

export function StationToolbar(props: StationToolbarProps) {
  function setNumber(valStr: string) {
    const val = parseFloat(valStr);
    if (!isNaN(val) && isFinite(val)) {
      props.setStationRoute(props.current_route.selected_station_type, val);
    }
  }
  return (
    <nav style={toolbarStyle}>
      <div>
        <Select
          value={props.current_route.selected_station_type}
          onChange={e => props.setStationRoute(e.target.value, props.current_route.selected_station_id)}
          autoWidth
        >
          <MenuItem value={routes.SelectedStationType.LoadStation}>
            Load Station
          </MenuItem>
          <MenuItem value={routes.SelectedStationType.Inspection}>
            Inspection
          </MenuItem>
          <MenuItem value={routes.SelectedStationType.Wash}>
            Wash
          </MenuItem>
        </Select>
        <Input
          type="number"
          value={props.current_route.selected_station_id}
          onChange={e => setNumber(e.target.value)}
          style={{width: '3em', marginLeft: '1em'}}
        />
      </div>
      <div>
        <Select value={0} autoWidth>
          <MenuItem value={0}>No Queues</MenuItem>
          <MenuItem value={1}>One Queue</MenuItem>
          <MenuItem value={2}>Two Queues</MenuItem>
          <MenuItem value={4}>Four Queues</MenuItem>
        </Select>
      </div>
    </nav>
  );
}

const ConnectedStationToolbar = connect(
  (st: Store) => ({
    current_route: st.Route,
  }),
  {
    setStationRoute: routes.switchToStationMonitor
  }
)(StationToolbar);

export default function StationMonitor() {
  return (
    <>
      <ConnectedStationToolbar/>
      <main style={{'padding': '8px'}}>
        <p>Station Monitor Page</p>
      </main>
    </>
  );
}