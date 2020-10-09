/* Copyright (c) 2020, John Lenz

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
import ChevronRightIcon from "@material-ui/icons/ChevronRight";
import ImportExport from "@material-ui/icons/ImportExport";
import { createStyles, withStyles, WithStyles } from "@material-ui/core/styles";
import { copyLogEntriesToClipboard } from "../data/results.cycles";
import { duration } from "moment";

const logStyles = createStyles({
  machine: {
    color: "#1565C0",
  },
  loadStation: {
    color: "#795548",
  },
  pallet: {
    color: "#00695C",
  },
  queue: {
    color: "#6A1B9A",
  },
  inspectionNotSignaled: {
    color: "#4527A0",
  },
  inspectionSignaled: {
    color: "red",
  },
});

export interface LogEntryProps extends WithStyles<typeof logStyles> {
  entry: api.ILogEntry;
  detailLogCounter: number | null;
  setDetail: (counter: number | null) => void;
}

function logType(entry: api.ILogEntry): string {
  switch (entry.type) {
    case api.LogType.LoadUnloadCycle:
      if (entry.startofcycle) {
        return "Start " + entry.result.charAt(0).toUpperCase() + entry.result.substring(1).toLowerCase();
      } else {
        return "End " + entry.result.charAt(0).toUpperCase() + entry.result.substring(1).toLowerCase();
      }

    case api.LogType.MachineCycle:
      if (entry.startofcycle) {
        return "Start Cycle";
      } else {
        return "End Cycle";
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

    case api.LogType.PalletInStocker:
      return "Stocker";

    case api.LogType.PalletOnRotaryInbound:
      return "Rotary";

    case api.LogType.SignalQuarantine:
      return "Quarantine";

    default:
      return "Message";
  }
}

function displayMat(mats: ReadonlyArray<api.ILogMaterial>) {
  let mat = "";
  mats.forEach((m) => {
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

function displayQueueMat(mats: ReadonlyArray<api.ILogMaterial>) {
  if (mats.length > 1) {
    return `${mats[0].part} x${mats.length}`;
  } else if (mats.length === 1) {
    return mats[0].part;
  } else {
    return "";
  }
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
          <span className={props.classes.machine}>
            {entry.loc} {entry.locnum.toString()}
          </span>
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

    case api.LogType.Inspection: {
      const inspName = (entry.details || {}).InspectionType || "";
      const inspected = entry.result.toLowerCase() === "true" || entry.result === "1";
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
    }

    case api.LogType.InspectionForce: {
      const forceInspName = entry.program;
      const forced = entry.result.toLowerCase() === "true" || entry.result === "1";
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
          {displayQueueMat(entry.material)} added to queue <span className={props.classes.queue}>{entry.loc}</span>
        </span>
      );

    case api.LogType.RemoveFromQueue:
      return (
        <span>
          {displayQueueMat(entry.material)} removed from queue <span className={props.classes.queue}>{entry.loc}</span>
        </span>
      );

    case api.LogType.PalletInStocker:
      if (entry.startofcycle) {
        return (
          <span>
            <span className={props.classes.pallet}>Pallet {entry.pal}</span> arrived at stocker {entry.locnum}
          </span>
        );
      } else {
        return (
          <span>
            <span className={props.classes.pallet}>Pallet {entry.pal}</span> departed stocker {entry.locnum}
          </span>
        );
      }

    case api.LogType.PalletOnRotaryInbound:
      if (entry.startofcycle) {
        return (
          <span>
            <span className={props.classes.pallet}>Pallet {entry.pal}</span> arrived at{" "}
            <span className={props.classes.machine}>
              {entry.loc} {entry.locnum.toString()}
            </span>
          </span>
        );
      } else if (entry.result == "RotateIntoWorktable") {
        return (
          <span>
            <span className={props.classes.pallet}>Pallet {entry.pal}</span> rotated into{" "}
            <span className={props.classes.machine}>
              {entry.loc} {entry.locnum.toString()}
            </span>{" "}
            worktable
          </span>
        );
      } else {
        return (
          <span>
            <span className={props.classes.pallet}>Pallet {entry.pal}</span> left{" "}
            <span className={props.classes.machine}>
              {entry.loc} {entry.locnum.toString()}
            </span>
          </span>
        );
      }

    case api.LogType.SignalQuarantine:
      return <span>{displayMat(entry.material)} signaled for quarantine after unload</span>;

    default:
      return <span>{entry.result}</span>;
  }
}

interface LogDetail {
  readonly name: string;
  readonly value: string;
}

function detailsForEntry(e: api.ILogEntry): ReadonlyArray<LogDetail> {
  const details = [];
  if (e.details && e.details.operator) {
    details.push({
      name: "Operator",
      value: e.details.operator,
    });
  }
  if (e.details && e.details.note) {
    details.push({
      name: "Note",
      value: e.details.note,
    });
  }
  if (e.tools) {
    for (const [toolName, use] of LazySeq.ofObject(e.tools)) {
      if (use.toolUseDuringCycle && use.toolUseDuringCycle !== "") {
        let msg = duration(use.toolUseDuringCycle).asMinutes().toFixed(1) + " min used during cycle.";

        if (
          use.totalToolUseAtEndOfCycle &&
          use.configuredToolLife &&
          use.totalToolUseAtEndOfCycle !== "" &&
          use.configuredToolLife !== ""
        ) {
          const total = duration(use.totalToolUseAtEndOfCycle);
          const life = duration(use.configuredToolLife);
          const pct = total.asSeconds() / life.asSeconds();
          msg += ` Total use at end of cycle: ${total.asMinutes().toFixed(1)}/${life.asMinutes().toFixed(1)} min (${(
            pct * 100
          ).toFixed(0)}%).`;
        }

        details.push({
          name: toolName,
          value: msg,
        });
      }
    }
  }
  return details;
}

export const LogEntry = React.memo(
  withStyles(logStyles)((props: LogEntryProps) => {
    const details = detailsForEntry(props.entry);

    return (
      <>
        <TableRow>
          <TableCell size="small">
            <DateTimeDisplay date={props.entry.endUTC} formatStr={"MMM d, yy"} />
          </TableCell>
          <TableCell size="small">
            <DateTimeDisplay date={props.entry.endUTC} formatStr={"hh:mm aa"} />
          </TableCell>
          <TableCell size="small">{logType(props.entry)}</TableCell>
          <TableCell size="small">{display(props)}</TableCell>
          <TableCell padding="checkbox">
            {details.length > 0 ? (
              <IconButton
                style={{
                  transition: "all ease 200ms",
                  transform: props.entry.counter === props.detailLogCounter ? "rotate(90deg)" : "none",
                }}
                onClick={(event) => {
                  props.setDetail(props.entry.counter === props.detailLogCounter ? null : props.entry.counter);
                  event.stopPropagation();
                }}
                size="small"
              >
                <ChevronRightIcon fontSize="inherit" />
              </IconButton>
            ) : undefined}
          </TableCell>
        </TableRow>
        {details.length > 0 && props.entry.counter === props.detailLogCounter ? (
          <TableRow>
            <TableCell colSpan={5}>
              <ul>
                {details.map((d, idx) => (
                  <li key={idx}>
                    {d.name}: {d.value}
                  </li>
                ))}
              </ul>
            </TableCell>
          </TableRow>
        ) : undefined}
      </>
    );
  })
);

export interface LogEntriesProps {
  entries: Iterable<Readonly<api.ILogEntry>>;
  copyToClipboard?: boolean;
}

export const LogEntries = React.memo(function LogEntriesF(props: LogEntriesProps) {
  const [curDetail, setDetail] = React.useState<number | null>(null);
  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableCell>Date</TableCell>
          <TableCell>Time</TableCell>
          <TableCell>Type</TableCell>
          <TableCell>Details</TableCell>
          <TableCell padding="checkbox">
            {props.copyToClipboard ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  onClick={() => copyLogEntriesToClipboard(props.entries)}
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : undefined}
          </TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {LazySeq.ofIterable(props.entries).map((e, idx) => (
          <LogEntry key={idx} entry={e} detailLogCounter={curDetail} setDetail={setDetail} />
        ))}
      </TableBody>
    </Table>
  );
});
