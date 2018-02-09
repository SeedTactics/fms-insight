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
import Table, { TableBody, TableCell, TableHead, TableRow } from 'material-ui/Table';
import * as im from 'immutable';
import * as numerable from 'numeral';
import { createSelector } from 'reselect';

import { Store } from '../data/store';
import { StationInUse } from '../data/events';

export interface StationHours {
    readonly station: string;
    readonly hours: number;
}

export interface Props {
  station_active_hours_past_week: im.Seq<number, StationHours>;
  system_active_hours_per_week: number;
}

export function StationOEE(p: Props) {
  return (
    <div>
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Station</TableCell>
            <TableCell>OEE</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {
            p.station_active_hours_past_week.map(stat => (
              <TableRow key={stat.station}>
                <TableCell>{stat.station}</TableCell>
                <TableCell>{numerable(stat.hours / p.system_active_hours_per_week).format('0.0%')}</TableCell>
              </TableRow>
            ))
          }
        </TableBody>
      </Table>
    </div>
  );
}

export function stationHoursInLastWeek(use: im.List<StationInUse>): im.Seq<number, StationHours> {
    const m = new Map<string, number>();
    use.forEach(s => {
        m.set(s.station, (m.get(s.station) || 0) + s.hours);
    });
    return im.Seq(m)
        .map((v, k) => ({
            station: v[0],
            hours: v[1]
        }))
        .sortBy(e => e.station);
}

const oeeSelector = createSelector(
  (s: Store) => s.Events.last_week_of_hours,
  stationHoursInLastWeek
);

export default connect(
  (s: Store) => ({
    station_active_hours_past_week: oeeSelector(s),
    system_active_hours_per_week: s.Events.system_active_hours_per_week,
  })
)(StationOEE);