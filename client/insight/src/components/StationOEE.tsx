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

import { Store } from '../data/store';

export interface Props {
  station_active_hours_past_week: im.Map<string, number>;
  system_active_hours_per_week: number;
}

export function OEECard(p: Props) {
  let sorted = p.station_active_hours_past_week.sortBy((v, k) => k);
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
            sorted.toSeq().map((hours, stat) => (
              <TableRow key={stat}>
                <TableCell>{stat}</TableCell>
                <TableCell>{numerable(hours / p.system_active_hours_per_week).format('0.0%')}</TableCell>
              </TableRow>
            )).valueSeq()
          }
        </TableBody>
      </Table>
    </div>
  );
}

export default connect(
  (s: Store) => ({
    station_active_hours_past_week: s.Events.station_active_hours_past_week,
    system_active_hours_per_week: s.Events.system_active_hours_per_week,
  })
)(OEECard);