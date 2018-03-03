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
import { Seq } from 'immutable';

import * as routes from '../../data/routes';
import { Store } from '../../data/store';
import * as api from '../../data/api';

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
  readonly current_route: routes.State;
  readonly queues: { [key: string]: api.IQueueSize };
  // tslint:disable-next-line:no-any
  readonly setStationRoute: (station: routes.SelectedStationType, num: number, queues: ReadonlyArray<string>) => any;
}

export function StationToolbar(props: StationToolbarProps) {
  const queueNames = Object.keys(props.queues).sort();

  function setStation(s: string) {
    props.setStationRoute(
      s as routes.SelectedStationType,
      props.current_route.selected_station_id,
      props.current_route.station_queues
    );
  }

  function setNumber(valStr: string) {
    const val = parseFloat(valStr);
    if (!isNaN(val) && isFinite(val)) {
      props.setStationRoute(
        props.current_route.selected_station_type,
        val,
        props.current_route.station_queues);
    }
  }

  let curQueueCount = props.current_route.station_queues.length;
  if (curQueueCount > 3) {
    curQueueCount = 3;
  }

  // the material-ui type bindings specify `e.target.value` to have type string, but
  // when multiple selects are enabled it is actually a type string[]
  // tslint:disable-next-line:no-any
  function setQueues(newQueuesAny: any) {
    const newQueues: ReadonlyArray<string> = newQueuesAny;
    props.setStationRoute(
      props.current_route.selected_station_type,
      props.current_route.selected_station_id,
      Seq(newQueues).take(3).toArray()
    );
  }

  let queueSelect: JSX.Element | undefined;
  if (queueNames.length > 0) {
    queueSelect = (
      <div>
        <Select
          multiple
          value={props.current_route.station_queues as string[]}
          autoWidth
          onChange={e => setQueues(e.target.value)}
        >
          {
            queueNames.map((q, idx) =>
              <MenuItem key={idx} value={q}>{q}</MenuItem>
            )
          }
        </Select>
      </div>
    );
  }

  return (
    <nav style={toolbarStyle}>
      <div>
        <Select
          value={props.current_route.selected_station_type}
          onChange={e => setStation(e.target.value)}
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
      {queueSelect}
    </nav>
  );
}

export default connect(
  (st: Store) => ({
    current_route: st.Route,
    queues: st.Current.current_status.queues,
  }),
  {
    setStationRoute: routes.switchToStationMonitor
  }
)(StationToolbar);