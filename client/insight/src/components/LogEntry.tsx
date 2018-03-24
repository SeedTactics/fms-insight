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
import * as api from '../data/api';
import { format } from 'date-fns';
import Table, { TableBody, TableCell, TableHead, TableRow } from 'material-ui/Table';

export interface LogEntryProps {
    entry: api.ILogEntry;
}

function logType(entry: api.ILogEntry): string {
  switch (entry.type) {
    case api.LogType.LoadUnloadCycle:
      if (entry.result === 'LOAD') {
        if (entry.startofcycle) {
          return 'Start Load';
        } else {
          return 'End Load';
        }
      } else {
        if (entry.startofcycle) {
          return 'Start Unload';
        } else {
          return 'End Unload';
        }
      }

    case api.LogType.MachineCycle:
      if (entry.startofcycle) {
        return 'Cycle Start';
      } else {
        return 'Cycle End';
      }

    case api.LogType.PartMark:
      return 'Serial';

    case api.LogType.OrderAssignment:
      return 'Workorder';

    case api.LogType.Inspection:
      return 'Signal';

    case api.LogType.PalletCycle:
      return 'Pallet Cycle';

    case api.LogType.FinalizeWorkorder:
      return 'Finalize Workorder';

    case api.LogType.Wash:
      return 'Wash';

    case api.LogType.InspectionResult:
      return 'Inspection';

    default:
      return 'Message';

  }
}

function displayMat(mats: ReadonlyArray<api.ILogMaterial>) {
  let mat = '';
  mats.forEach(m => {
    if (mat.length > 0) { mat += ', '; }
    if (m.numproc > 1) {
      mat += m.part + '[' + m.proc.toString() + ']';
    } else {
      mat += m.part;
    }
  });
  return mat;
}

function display(entry: api.ILogEntry): string {
  switch (entry.type) {
    case api.LogType.LoadUnloadCycle:
      return displayMat(entry.material) +
        ' on pallet ' + entry.pal +
        ' at station ' + entry.locnum.toString();

    case api.LogType.MachineCycle:
      let msg;
      msg = displayMat(entry.material) +
        ' on pallet ' + entry.pal +
        ' at machine ' + entry.locnum.toString();
      if (entry.program && entry.program !== '') {
        msg += ' with program ' + entry.program;
      }
      return msg;

    case api.LogType.PartMark:
      return displayMat(entry.material) +
        ' marked with ' + entry.result;

    case api.LogType.OrderAssignment:
      return displayMat(entry.material) +
        ' assigned to workorder ' + entry.result;

    case api.LogType.PalletCycle:
      return 'Pallet ' + entry.pal + ' completed route';

    case api.LogType.Inspection:
      let infos = (entry.program).split(',');
      let inspName = 'unknown';
      if (infos.length >= 2 && infos[1] && infos[1] !== '') {
        inspName = infos[1];
      }
      let inspected = entry.result === 'true' || entry.result === 'True';
      if (inspected) {
        return displayMat(entry.material) +
          ' signaled for inspection ' + inspName;
      } else {
        return displayMat(entry.material) +
          ' skipped inspection ' + inspName;
      }

    case api.LogType.FinalizeWorkorder:
      return 'Finalize workorder ' + entry.result;

    case api.LogType.InspectionResult:
      if (entry.result.toLowerCase() === "false") {
        return entry.program + ' Failed';
      } else {
        return entry.program + ' Succeeded';
      }

    case api.LogType.Wash:
      return 'Wash Completed';

    default: return entry.result;
  }
}

export class LogEntry extends React.PureComponent<LogEntryProps> {
  render() {
    return (
      <TableRow>
        <TableCell padding="dense">{format(this.props.entry.endUTC, "MMM D, YY")}</TableCell>
        <TableCell padding="dense">{format(this.props.entry.endUTC, "hh:mm A")}</TableCell>
        <TableCell padding="dense">{logType(this.props.entry)}</TableCell>
        <TableCell padding="dense">
          {display(this.props.entry)}
        </TableCell>
      </TableRow>
    );
  }
}

export interface LogEntriesProps {
  entries: ReadonlyArray<Readonly<api.ILogEntry>>;
}

export class LogEntries extends React.PureComponent<LogEntriesProps> {
  render() {
    return (
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Date</TableCell>
            <TableCell>Time</TableCell>
            <TableCell>Type</TableCell>
            <TableCell>Details</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {
            this.props.entries.map(e => (
              <LogEntry key={e.counter} entry={e}/>
            ))
          }
        </TableBody>
      </Table>
    );
  }
}
