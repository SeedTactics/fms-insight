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
import { createSelector } from 'reselect';
import * as im from 'immutable';
import { distanceInWordsToNow } from 'date-fns';

import * as api from '../data/api';
import { Store } from '../data/store';
import Table, { TableBody, TableRow, TableCell, TableHead } from 'material-ui/Table';

export interface Status {
  readonly name: string;
  readonly status: string;
}

export interface Props {
  lastEvent: Readonly<api.ILogEntry> | undefined;
  status: ReadonlyArray<Status>;
}

export function MachinePalletStatus(p: Props) {
  let lastMsg: JSX.Element | undefined = undefined;
  if (p.lastEvent !== undefined) {
    lastMsg = (
      <p style={{marginBottom: '-10px', color: '#9E9E9E'}}>
        <small>Last event {distanceInWordsToNow(p.lastEvent.endUTC)} ago</small>
      </p>
    );
  }
  return (
    <div>
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Resource</TableCell>
            <TableCell>Status</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {
            p.status.map((s, i) => (
              <TableRow key={i}>
                <TableCell>{s.name}</TableCell>
                <TableCell>{s.status}</TableCell>
              </TableRow>
            ))
          }
        </TableBody>
      </Table>
      {lastMsg}
    </div>
  );
}

export const lastEvent = createSelector(
  (s: Store) => s.Events.last_30_days_of_events,
  e => e.last()
);

function buildStatus(status: Readonly<api.ICurrentStatus>): ReadonlyArray<Status> {
  const palletLocation = new Map<string, string>();

  for (let [name, pal] of Object.entries(status.pallets)) {
    switch (pal.currentPalletLocation.loc) {
      case api.PalletLocationEnum.Buffer:
        palletLocation.set(name, 'Buffer #' + pal.currentPalletLocation.num);
        break;

      case api.PalletLocationEnum.Cart:
        palletLocation.set(name, 'Cart');
        break;

      default:
        palletLocation.set(name, pal.currentPalletLocation.group + ' #' + pal.currentPalletLocation.num);
        break;
    }
  }

  const byStation = new Map<string, string>();
  let loc: string | undefined = undefined;

  for (let m of status.material) {
    switch (m.action.type) {
      case api.ActionType.Machining:
        loc = palletLocation.get(m.location.pallet || '');
        if (loc !== undefined) {
          byStation.set(loc, 'Machining ' + m.partName + '-' + m.process.toString());
        }
        break;

      case api.ActionType.Loading:
        loc = palletLocation.get(m.location.pallet || '');
        if (loc !== undefined) {
          loc = loc + ' Load';
          if (byStation.has(loc)) {
            byStation.set(loc, byStation.get(loc) + ', ' + m.partName + '-' + m.process.toString());
          } else {
            byStation.set(loc, 'Loading ' + m.partName + '-' + m.process.toString());
          }
        }
        break;

      case api.ActionType.Unloading:
        loc = palletLocation.get(m.location.pallet || '');
        if (loc !== undefined) {
          loc = loc + ' Unload';
          if (byStation.has(loc)) {
            byStation.set(loc, byStation.get(loc) + ', ' + m.partName + '-' + m.process.toString());
          } else {
            byStation.set(loc, 'Unloading ' + m.partName + '-' + m.process.toString());
          }
        }
        break;

      default: break;
    }
  }

  const statSeq = im.Seq.Keyed(byStation)
    .map((st, name) => ({name, status: st}))
    .sortBy((val, s) => s)
    .valueSeq();

  const palSeq = im.Seq.Keyed(palletLocation)
    .map((palLoc, pal) => ({name: 'Pallet ' + pal, status: palLoc}))
    .sortBy(val => val.name)
    .valueSeq();

  return statSeq.concat(palSeq).toArray();
}

export const statusSelector = createSelector(
  (s: Store) => s.Jobs.current_status,
  buildStatus
);

export default connect(
  (s: Store) => {
    return {
        lastEvent: lastEvent(s),
        status: statusSelector(s),
    };
  }
)(MachinePalletStatus);