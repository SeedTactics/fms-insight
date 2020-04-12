/* Copyright (c) 2019, John Lenz

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

import { BackgroundResponse } from "./ipc";
import { ipcRenderer, IpcRendererEvent } from "electron";
import * as sqlite from "sqlite";
import { ILogEntry, LogType, IToolUse } from "../../insight/.build/data/api";
import { duration } from "moment";

let dbP: Promise<sqlite.Database> = Promise.reject("No opened file yet");

ipcRenderer.on("open-file", (_: IpcRendererEvent, path: string) => {
  dbP = (async function () {
    let db: sqlite.Database | undefined;
    try {
      db = await sqlite.open(path);
    } catch {
      throw "The file is not a FMS Insight database";
    }
    let verRow: any;
    try {
      verRow = await db.get("SELECT ver from version");
    } catch {
      throw "The file is not a FMS Insight database.";
    }
    if (verRow.ver < 19) {
      throw "The FMS Insight database is from a newer version of FMS Insight.  Please update BackupViewer to the latest version.";
    }
    try {
      await db.get("SELECT Counter from stations");
    } catch {
      throw "The file is not a FMS Insight Log database.";
    }
    return db;
  })();
});

const b = new BackgroundResponse(ipcRenderer, {});

// 10000 ticks in a millisecond
// 621355968000000000 ticks between unix epoch and Jan 1, 0001
function dateToTicks(d: Date): number {
  return d.getTime() * 10000 + 621355968000000000;
}

function parseDateFromTicks(t: number): string {
  return new Date((t - 621355968000000000) / 10000).toISOString();
}

function parseTimespan(t: number | null): string {
  if (!t) return "";
  return duration(t / 10000).toISOString();
}

function convertLogType(t: number): LogType {
  switch (t) {
    case 1:
      return LogType.LoadUnloadCycle;
    case 2:
      return LogType.MachineCycle;
    case 6:
      return LogType.PartMark;
    case 7:
      return LogType.Inspection;
    case 10:
      return LogType.OrderAssignment;
    case 100:
      return LogType.GeneralMessage;
    case 101:
      return LogType.PalletCycle;
    case 102:
      return LogType.FinalizeWorkorder;
    case 103:
      return LogType.InspectionResult;
    case 104:
      return LogType.Wash;
    case 105:
      return LogType.AddToQueue;
    case 106:
      return LogType.RemoveFromQueue;
    case 107:
      return LogType.InspectionForce;
    default:
      return LogType.GeneralMessage;
  }
}

async function convertRowToLog(db: sqlite.Database, row: any): Promise<any> {
  const cmd =
    "SELECT stations_mat.MaterialID, UniqueStr, Process, PartName, NumProcesses, Face, Serial, Workorder " +
    " FROM stations_mat " +
    " LEFT OUTER JOIN matdetails ON stations_mat.MaterialID = matdetails.MaterialID " +
    " WHERE stations_mat.Counter = $cntr " +
    " ORDER BY stations_mat.Counter ASC";
  const matRows = await db.all(cmd, { $cntr: row.Counter });
  const details: { [key: string]: string } = {};
  const tools: { [key: string]: Readonly<IToolUse> } = {};
  await db.each(
    "SELECT Key, Value FROM program_details WHERE Counter = $cntr",
    { $cntr: row.Counter },
    (_err: any, row: any) => {
      if (row) {
        details[row.Key] = row.Value;
      }
    }
  );
  await db.each(
    "SELECT Tool, UseInCycle, UseAtEndOfCycle, ToolLife FROM station_tools WHERE Counter = $cntr",
    { $cntr: row.Counter },
    (_err: any, row: any) => {
      if (row) {
        tools[row.Tool] = {
          toolUseDuringCycle: parseTimespan(row.UseInCycle),
          totalToolUseAtEndOfCycle: parseTimespan(row.UseAtEndOfCycle),
          configuredToolLife: parseTimespan(row.ToolLife),
        };
      }
    }
  );

  return {
    counter: row.Counter,
    material: matRows.map((m) => ({
      id: m.MaterialID,
      uniq: m.UniqueStr,
      part: m.PartName,
      proc: m.Process,
      numproc: m.NumProcesses,
      face: m.Face,
      serial: m.Serial,
      workorder: m.Workorder,
    })),
    type: convertLogType(row.StationLoc),
    startofcycle: row.Start > 0,
    endUTC: parseDateFromTicks(row.TimeUTC),
    loc: row.StationName,
    locnum: row.StationNum,
    pal: row.Pallet,
    program: row.Program,
    result: row.Result,
    elapsed: parseTimespan(row.Elapsed),
    active: parseTimespan(row.ActiveTime),
    details: details,
    tools: tools,
  };
}

b.register(
  "log-get",
  async (a: {
    startUTC: Date;
    endUTC: Date;
  }): Promise<ReadonlyArray<Readonly<ILogEntry>>> => {
    const db = await dbP;
    const rows = await db.all(
      "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
        " FROM stations WHERE TimeUTC >= $start AND TimeUTC <= $end ORDER BY Counter ASC",
      {
        $start: dateToTicks(new Date(a.startUTC)),
        $end: dateToTicks(new Date(a.endUTC)),
      }
    );
    return await Promise.all(rows.map((r) => convertRowToLog(db, r)));
  }
);

b.register(
  "log-for-material",
  async (a: {
    materialID: number;
  }): Promise<ReadonlyArray<Readonly<ILogEntry>>> => {
    const db = await dbP;
    const rows = await db.all(
      "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
        " FROM stations WHERE Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) ORDER BY Counter ASC",
      {
        $mat: a.materialID,
      }
    );
    return await Promise.all(rows.map((r) => convertRowToLog(db, r)));
  }
);

b.register(
  "log-for-materials",
  async (a: {
    materialIDs: Iterable<number>;
  }): Promise<ReadonlyArray<Readonly<ILogEntry>>> => {
    const db = await dbP;
    const rows: any[] = [];
    for (const matId of a.materialIDs) {
      rows.push(
        ...(await db.all(
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
            " FROM stations WHERE Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) ORDER BY Counter ASC",
          {
            $mat: matId,
          }
        ))
      );
    }
    return await Promise.all(rows.map((r) => convertRowToLog(db, r)));
  }
);

b.register(
  "log-for-serial",
  async (a: {
    serial: string;
  }): Promise<ReadonlyArray<Readonly<ILogEntry>>> => {
    const db = await dbP;
    const rows = await db.all(
      "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
        " FROM stations WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.Serial = $ser) ORDER BY Counter ASC",
      {
        $ser: a.serial,
      }
    );
    return await Promise.all(rows.map((r) => convertRowToLog(db, r)));
  }
);
