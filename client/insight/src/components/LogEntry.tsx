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
import * as React from "react";
import * as api from "../data/api";
import Table from "@material-ui/core/Table";
import TableBody from "@material-ui/core/TableBody";
import TableCell from "@material-ui/core/TableCell";
import TableHead from "@material-ui/core/TableHead";
import TableRow from "@material-ui/core/TableRow";
import DateTimeDisplay from "./DateTimeDisplay";
import { LazySeq } from "../data/lazyseq";
import Tooltip from "@material-ui/core/Tooltip";
import IconButton from "@material-ui/core/IconButton";
import ImportExport from "@material-ui/icons/ImportExport";
import { copyLogEntriesToClipboard } from "../data/events.clipboard";
import { createStyles, withStyles, WithStyles } from "@material-ui/core/styles";

const logStyles = createStyles({
  machine: {
    color: "#1565C0"
  },
  loadStation: {
    color: "#795548"
  },
  pallet: {
    color: "#00695C"
  },
  queue: {
    color: "#6A1B9A"
  },
  inspectionNotSignaled: {
    color: "#4527A0"
  },
  inspectionSignaled: {
    color: "red"
  }
});

export interface LogEntryProps extends WithStyles<typeof logStyles> {
  entry: api.ILogEntry;
}

function logType(entry: api.ILogEntry): string {
  switch (entry.type) {
    case api.LogType.LoadUnloadCycle:
      if (entry.result === "LOAD") {
        if (entry.startofcycle) {
          return "Start Load";
        } else {
          return "End Load";
        }
      } else {
        if (entry.startofcycle) {
          return "Start Unload";
        } else {
          return "End Unload";
        }
      }

    case api.LogType.MachineCycle:
      if (entry.startofcycle) {
        return "Cycle Start";
      } else {
        return "Cycle End";
      }

    case api.LogType.PartMark:
      return "Serial";

    case api.LogType.OrderAssignment:
      return "Workorder";

    case api.LogType.Inspection:
    case api.LogType.InspectionForce:
      return "Signal";

    case api.LogType.PalletCycle:
      return "Pallet Cycle";

    case api.LogType.FinalizeWorkorder:
      return "Finalize Workorder";

    case api.LogType.Wash:
      return "Wash";

    case api.LogType.InspectionResult:
      return "Inspection";

    case api.LogType.AddToQueue:
      return "Queue";

    case api.LogType.RemoveFromQueue:
      return "Queue";

    default:
      return "Message";
  }
}

function displayMat(mats: ReadonlyArray<api.ILogMaterial>) {
  let mat = "";
  mats.forEach(m => {
    if (mat.length > 0) {
      mat += ", ";
    }
    if (m.numproc > 1) {
      mat += m.part + "-" + m.proc.toString();
    } else {
      mat += m.part;
    }
  });
  return mat;
}

function display(props: LogEntryProps): JSX.Element {
  const entry = props.entry;
  switch (entry.type) {
    case api.LogType.LoadUnloadCycle:
      return (
        <span>
          {displayMat(entry.material)} on <span className={props.classes.pallet}>pallet {entry.pal}</span> at{" "}
          <span className={props.classes.loadStation}>station {entry.locnum.toString()}</span>
        </span>
      );

    case api.LogType.MachineCycle:
      return (
        <span>
          {displayMat(entry.material)} on <span className={props.classes.pallet}>pallet {entry.pal}</span> at{" "}
          <span className={props.classes.machine}>machine {entry.locnum.toString()}</span>
          {entry.program && entry.program !== "" ? <span> with program {entry.program}</span> : undefined}
        </span>
      );

    case api.LogType.PartMark:
      return (
        <span>
          {displayMat(entry.material)} marked with {entry.result}
        </span>
      );

    case api.LogType.OrderAssignment:
      return (
        <span>
          {displayMat(entry.material)} assigned to workorder {entry.result}
        </span>
      );

    case api.LogType.PalletCycle:
      return <span>Pallet {entry.pal} completed route</span>;

    case api.LogType.Inspection:
      const inspName = (entry.details || {}).InspectionType || "";
      let inspected = entry.result.toLowerCase() === "true" || entry.result === "1";
      if (inspected) {
        return (
          <span>
            {displayMat(entry.material)} signaled for inspection{" "}
            <span className={props.classes.inspectionSignaled}>{inspName}</span>
          </span>
        );
      } else {
        return (
          <span>
            {displayMat(entry.material)} skipped inspection{" "}
            <span className={props.classes.inspectionNotSignaled}>{inspName}</span>
          </span>
        );
      }

    case api.LogType.InspectionForce:
      const forceInspName = entry.program;
      let forced = entry.result.toLowerCase() === "true" || entry.result === "1";
      if (forced) {
        return (
          <span>
            {displayMat(entry.material)} declared for inspection{" "}
            <span className={props.classes.inspectionSignaled}>{forceInspName}</span>
          </span>
        );
      } else {
        return (
          <span>
            {displayMat(entry.material)} passed over for inspection{" "}
            <span className={props.classes.inspectionNotSignaled}>{forceInspName}</span>
          </span>
        );
      }

    case api.LogType.FinalizeWorkorder:
      return <span>Finalize workorder {entry.result}</span>;

    case api.LogType.InspectionResult:
      if (entry.result.toLowerCase() === "false") {
        return <span className={props.classes.inspectionSignaled}>{entry.program} Failed</span>;
      } else {
        return <span className={props.classes.inspectionSignaled}>{entry.program} Succeeded</span>;
      }

    case api.LogType.Wash:
      return <span>Wash Completed</span>;

    case api.LogType.AddToQueue:
      return (
        <span>
          {displayMat(entry.material)} added to queue <span className={props.classes.queue}>{entry.loc}</span>
        </span>
      );

    case api.LogType.RemoveFromQueue:
      return (
        <span>
          {displayMat(entry.material)} removed from queue <span className={props.classes.queue}>{entry.loc}</span>
        </span>
      );

    default:
      return <span>{entry.result}</span>;
  }
}

export const LogEntry = React.memo(
  withStyles(logStyles)((props: LogEntryProps) => {
    return (
      <TableRow>
        <TableCell padding="dense">
          <DateTimeDisplay date={props.entry.endUTC} formatStr={"MMM D, YY"} />
        </TableCell>
        <TableCell padding="dense">
          <DateTimeDisplay date={props.entry.endUTC} formatStr={"hh:mm A"} />
        </TableCell>
        <TableCell padding="dense">{logType(props.entry)}</TableCell>
        <TableCell padding="dense">{display(props)}</TableCell>
      </TableRow>
    );
  })
);

export interface LogEntriesProps {
  entries: Iterable<Readonly<api.ILogEntry>>;
  copyToClipboard?: boolean;
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
            <TableCell>
              Details
              {this.props.copyToClipboard ? (
                <div style={{ float: "right" }}>
                  <Tooltip title="Copy to Clipboard">
                    <IconButton
                      onClick={() => copyLogEntriesToClipboard(this.props.entries)}
                      style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                    >
                      <ImportExport />
                    </IconButton>
                  </Tooltip>
                </div>
              ) : (
                undefined
              )}
            </TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {LazySeq.ofIterable(this.props.entries).map((e, idx) => (
            <LogEntry key={idx} entry={e} />
          ))}
        </TableBody>
      </Table>
    );
  }
}
