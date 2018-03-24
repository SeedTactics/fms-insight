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
import { distanceInWordsToNow } from 'date-fns';

export interface Props {
    entry: api.ILogEntry;
}

/*function logType(entry: api.ILogEntry): string {
  switch (entry.type) {
    case api.LogEntryType.LoadUnloadCycle:
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

    case api.LogEntryType.MachineCycle:
      if (entry.startofcycle) {
        return 'Cycle Start';
      } else {
        return 'Cycle End';
      }

    case api.LogEntryType.PartMark:
      return 'Serial';

    case api.LogEntryType.OrderAssignment:
      return 'Workorder';

    case api.LogEntryType.Inspection:
      return 'Inspection';

    case api.LogEntryType.PalletCycle:
      return 'Pallet Cycle';

    case api.LogEntryType.FinalizeWorkorder:
      return 'Finalize Workorder';

    case api.LogEntryType.GeneralMessage:
      return 'Message';

    default: return 'Message';
  }
}*/

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
      let oper;
      if (entry.result === 'LOAD') {
        if (entry.startofcycle) {
          oper = 'Start load';
        } else {
          oper = 'End load';
        }
      } else {
        if (entry.startofcycle) {
          oper = 'Start unload';
        } else {
          oper = 'End unload';
        }
      }
      return oper + ' of ' + displayMat(entry.material) +
        ' on pallet ' + entry.pal +
        ' at station ' + entry.locnum.toString();

    case api.LogType.MachineCycle:
      let msg;
      if (entry.startofcycle) {
        msg = 'Cycle start';
      } else {
        msg = 'Cycle end';
      }
      msg += ' of ' + displayMat(entry.material) +
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
        return 'Inspection ' + entry.program + ' Failed';
      } else {
        return 'Inspection ' + entry.program + ' Succeeded';
      }

    default: return entry.result;
  }
}

export default function LogEntry({entry}: Props) {
  return (
    <span>
      <small>{display(entry) + ' (' + distanceInWordsToNow(entry.endUTC) + ' ago)'}</small>
    </span>
  );
}