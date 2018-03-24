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
import { FormControl } from 'material-ui/Form';
import { Seq, Set } from 'immutable';

import * as routes from '../../data/routes';
import { Store } from '../../data/store';
import * as api from '../../data/api';

const toolbarStyle = {
  'display': 'flex',
  'backgroundColor': '#E0E0E0',
  'paddingLeft': '24px',
  'paddingRight': '24px',
  'paddingBottom': '4px',
  'height': '2.5em',
  'alignItems': 'flex-end' as 'flex-end',
};

export interface StationToolbarProps {
  readonly current_route: routes.State;
  readonly queues: { [key: string]: api.IQueueSize };
  readonly insp_types: Set<string>;
  readonly displayLoadStation:
    // tslint:disable-next-line:no-any
    (num: number, queues: ReadonlyArray<string>, freeMaterial: boolean) => any;

  // tslint:disable-next-line:no-any
  readonly displayInspection: (type: string | undefined) => any;

  // tslint:disable-next-line:no-any
  readonly displayWash: () => any;
}

const freeMaterialSym = "@@insight_free_material@@";
const allInspSym = "@@all_inspection_display@@";

export function StationToolbar(props: StationToolbarProps) {
  const queueNames = Object.keys(props.queues).sort();

  function setStation(s: string) {
    const type = s as routes.StationMonitorType;
    switch (type) {
      case routes.StationMonitorType.LoadUnload:
        props.displayLoadStation(
          props.current_route.selected_load_id,
          props.current_route.station_queues,
          props.current_route.station_free_material
        );
        break;

      case routes.StationMonitorType.Inspection:
        props.displayInspection(props.current_route.selected_insp_type);
        break;

      case routes.StationMonitorType.Wash:
        props.displayWash();
        break;
    }
  }

  function setLoadNumber(valStr: string) {
    const val = parseFloat(valStr);
    if (!isNaN(val) && isFinite(val)) {
      props.displayLoadStation(
        val,
        props.current_route.station_queues,
        props.current_route.station_free_material
      );
    }
  }

  function setInspType(type: string) {
    props.displayInspection(type === allInspSym ? undefined : type);
  }

  let curQueueCount = props.current_route.station_queues.length;
  if (curQueueCount > 3) {
    curQueueCount = 3;
  }

  // the material-ui type bindings specify `e.target.value` to have type string, but
  // when multiple selects are enabled it is actually a type string[]
  // tslint:disable-next-line:no-any
  function setQueues(newQueuesAny: any) {
    const newQueues = newQueuesAny as ReadonlyArray<string>;
    const free = newQueues.includes(freeMaterialSym);
    props.displayLoadStation(
      props.current_route.selected_load_id,
      Seq(newQueues).take(3).filter(q => q !== freeMaterialSym).toArray(),
      free
    );
  }

  let queues: string[] = [...props.current_route.station_queues];
  if (props.current_route.station_free_material) {
    queues.push(freeMaterialSym);
  }

  return (
    <nav style={toolbarStyle}>
      <div>
        <Select
          value={props.current_route.station_monitor}
          onChange={e => setStation(e.target.value)}
          autoWidth
        >
          <MenuItem value={routes.StationMonitorType.LoadUnload}>
            Load Station
          </MenuItem>
          <MenuItem value={routes.StationMonitorType.Inspection}>
            Inspection
          </MenuItem>
          <MenuItem value={routes.StationMonitorType.Wash}>
            Wash
          </MenuItem>
        </Select>
        {
          props.current_route.station_monitor === routes.StationMonitorType.LoadUnload ?
            <Input
              type="number"
              key="loadnumselect"
              value={props.current_route.selected_load_id}
              onChange={e => setLoadNumber(e.target.value)}
              style={{width: '3em', marginLeft: '1em'}}
            />
            : undefined
        }
        {
          props.current_route.station_monitor === routes.StationMonitorType.Inspection ?
            <Select
              key="inspselect"
              value={props.current_route.selected_insp_type || allInspSym}
              onChange={e => setInspType(e.target.value)}
              style={{marginLeft: '1em'}}
            >
              <MenuItem key={allInspSym} value={allInspSym}><em>All</em></MenuItem>
              {
                props.insp_types.toSeq().sort().map(ty =>
                  <MenuItem key={ty} value={ty}>{ty}</MenuItem>
                )
              }
            </Select>
            : undefined
        }
        {
          props.current_route.station_monitor === routes.StationMonitorType.LoadUnload ?
            <FormControl style={{marginLeft: '1em'}}>
              {
                queues.length === 0 ?
                  <label
                    style={{
                      position: 'absolute',
                      top: '24px',
                      left: 0,
                      color: 'rgba(0,0,0,0.54)',
                      fontSize: '0.9rem'}}
                  >
                    Display queue(s)
                  </label>
                  : undefined
              }
              <Select
                multiple
                key="queueselect"
                displayEmpty
                value={queues}
                inputProps={{id: "queueselect"}}
                style={{minWidth: '10em'}}
                onChange={e => setQueues(e.target.value)}
              >
                <MenuItem key={freeMaterialSym} value={freeMaterialSym}>Free Material</MenuItem>
                {
                  queueNames.map((q, idx) =>
                    <MenuItem key={idx} value={q}>{q}</MenuItem>
                  )
                }
              </Select>
            </FormControl>
            : undefined
        }
      </div>
    </nav>
  );
}

export default connect(
  (st: Store) => ({
    current_route: st.Route,
    queues: st.Current.current_status.queues,
    insp_types: st.Events.last30.mat_summary.inspTypes,
  }),
  {
    displayLoadStation: routes.displayLoadStation,
    displayInspection: routes.displayInspectionType,
    displayWash: routes.displayWash,
  }
)(StationToolbar);