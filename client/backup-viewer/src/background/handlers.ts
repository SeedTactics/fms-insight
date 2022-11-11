/* Copyright (c) 2021, John Lenz

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

import type { Database } from "better-sqlite3";

export const handlers: {
  [key: string]: (db: Database, x: any) => any;
} = {};

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
  const milli = Math.round(t / 10000);
  return "PT" + (milli / 1000).toFixed(1) + "S";
}

function convertLogType(t: number): string {
  switch (t) {
    case 1:
      return "LoadUnloadCycle";
    case 2:
      return "MachineCycle";
    case 6:
      return "PartMark";
    case 7:
      return "Inspection";
    case 10:
      return "OrderAssignment";
    case 100:
      return "GeneralMessage";
    case 101:
      return "PalletCycle";
    case 102:
      return "FinalizeWorkorder";
    case 103:
      return "InspectionResult";
    case 104:
      return "Wash";
    case 105:
      return "AddToQueue";
    case 106:
      return "RemoveFromQueue";
    case 107:
      return "InspectionForce";
    case 108:
      return "PalletOnRotaryInbound";
    case 110:
      return "PalletInStocker";
    case 111:
      return "SignalQuarantine";
    case 112:
      return "InvalidateCycle";
    case 113:
      return "SwapMaterialOnPallet";
    default:
      return "GeneralMessage";
  }
}

function convertRowsToLog(
  db: Database,
  rows: ReadonlyArray<any>
): ReadonlyArray<any> {
  const statCmd = db.prepare(
    "SELECT stations_mat.MaterialID, UniqueStr, Process, PartName, NumProcesses, Face, Serial, Workorder " +
      " FROM stations_mat " +
      " LEFT OUTER JOIN matdetails ON stations_mat.MaterialID = matdetails.MaterialID " +
      " WHERE stations_mat.Counter = $cntr " +
      " ORDER BY stations_mat.Counter ASC"
  );

  const detailCmd = db.prepare(
    "SELECT Key, Value FROM program_details WHERE Counter = $cntr"
  );

  const toolCmd = db.prepare(
    "SELECT Tool, UseInCycle, UseAtEndOfCycle, ToolLife FROM station_tools WHERE Counter = $cntr"
  );

  const statRows = [];

  for (const statRow of rows) {
    const matRows = statCmd.all({ cntr: statRow.Counter });
    const details: { [key: string]: string } = {};
    const tools: { [key: string]: Readonly<any> } = {};
    for (const detail of detailCmd.iterate({ cntr: statRow.Counter })) {
      details[detail.Key] = detail.Value;
    }
    for (const tool of toolCmd.iterate({ cntr: statRow.Counter })) {
      tools[tool.Tool] = {
        ToolUseDuringCycle: parseTimespan(tool.UseInCycle),
        TotalToolUseAtEndOfCycle: parseTimespan(tool.UseAtEndOfCycle),
        ConfiguredToolLife: parseTimespan(tool.ToolLife),
      };
    }

    statRows.push({
      counter: statRow.Counter,
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
      type: convertLogType(statRow.StationLoc),
      startofcycle: statRow.Start > 0,
      endUTC: parseDateFromTicks(statRow.TimeUTC),
      loc: statRow.StationName,
      locnum: statRow.StationNum,
      pal: statRow.Pallet,
      program: statRow.Program,
      result: statRow.Result,
      elapsed: parseTimespan(statRow.Elapsed),
      active: parseTimespan(statRow.ActiveTime),
      details: details,
      tools: tools,
    });
  }

  return statRows;
}

handlers["log-get"] = (
  db: Database,
  a: {
    startUTC: Date;
    endUTC: Date;
  }
): ReadonlyArray<Readonly<any>> => {
  const rows = db
    .prepare(
      "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
        " FROM stations WHERE TimeUTC >= $start AND TimeUTC <= $end ORDER BY Counter ASC"
    )
    .all({
      start: dateToTicks(new Date(a.startUTC)),
      end: dateToTicks(new Date(a.endUTC)),
    });
  return convertRowsToLog(db, rows);
};

handlers["log-for-material"] = (
  db: Database,
  a: {
    materialID: number;
  }
): ReadonlyArray<Readonly<any>> => {
  const rows = db
    .prepare(
      "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
        " FROM stations WHERE Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) ORDER BY Counter ASC"
    )
    .all({
      mat: a.materialID,
    });
  return convertRowsToLog(db, rows);
};

handlers["log-for-materials"] = (
  db: Database,
  a: {
    materialIDs: Iterable<number>;
  }
): ReadonlyArray<Readonly<any>> => {
  const rows: any[] = [];
  const cmd = db.prepare(
    "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
      " FROM stations WHERE Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) ORDER BY Counter ASC"
  );
  for (const matId of a.materialIDs) {
    rows.push(
      ...cmd.all({
        mat: matId,
      })
    );
  }
  return convertRowsToLog(db, rows);
};

handlers["log-for-serial"] = (
  db: Database,
  a: {
    serial: string;
  }
): ReadonlyArray<Readonly<any>> => {
  const rows = db
    .prepare(
      "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
        " FROM stations WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.Serial = $ser) ORDER BY Counter ASC"
    )
    .all({
      ser: a.serial,
    });
  return convertRowsToLog(db, rows);
};

handlers["material-for-serial"] = (
  db: Database,
  a: { serial: string }
): ReadonlyArray<Readonly<any>> => {
  const mats = db
    .prepare(
      "SELECT MaterialID, UniqueStr, PartName, NumProcesses, Workorder FROM matdetails WHERE Serial = $ser"
    )
    .all({ ser: a.serial });

  const path = db.prepare(
    "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat"
  );

  return mats.map((mat) => {
    const paths = path.all({ mat: mat.MaterialID });
    return {
      MaterialID: mat.MaterialID,
      JobUnique: mat.UniqueStr,
      PartName: mat.PartName,
      NumProcesses: mat.NumProcesses,
      Workorder: mat.Workorder,
      Serial: a.serial,
      Paths: paths.reduce((obj, p) => {
        obj[p.Process] = p.Path;
        return obj;
      }, {}),
    };
  });
};

function loadProcsAndPaths(
  db: Database,
  uniq: string
): ReadonlyArray<{ paths: ReadonlyArray<any> }> {
  const procsAndPaths: Array<{ paths: Array<any> }> = [];

  const pathDataCmd = db.prepare(
    "SELECT Process, StartingUTC, PartsPerPallet, PathGroup, SimAverageFlowTime, InputQueue, OutputQueue, LoadTime, UnloadTime, Fixture, Face, Casting FROM pathdata WHERE UniqueStr = $uniq ORDER BY Process,Path"
  );
  for (const row of pathDataCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    let pathData: object = {
      PathGroup: row.PathGroup,
      Pallets: [],
      Fixture: row.Fixture,
      Face: row.Face,
      Load: [],
      ExpectedLoadTime: parseTimespan(row.LoadTime),
      Unload: [],
      ExpectedUnloadTime: parseTimespan(row.UnloadTime),
      Stops: [],
      SimulatedProduction: [],
      SimulatedStartingUTC: parseDateFromTicks(row.StartingUTC),
      SimulatedAverageFlowTime: parseTimespan(row.SimAverageFlowTime),
      HoldMachining: undefined, // TODO: hold
      HoldLoadUnload: undefined, // TODO: hold
      PartsPerPallet: row.PartsPerPallet,
      InputQueue: row.InputQueue,
      OutputQueue: row.OutputQueue,
      Inspections: [],
      Casting: row.Casting,
    };
    if (proc > procsAndPaths.length) {
      procsAndPaths.push({ paths: [pathData] });
    } else {
      procsAndPaths[proc - 1].paths.push(pathData);
    }
  }

  const palCmd = db.prepare(
    "SELECT Process, Path, Pallet FROM pallets WHERE UniqueStr = $uniq"
  );
  for (const row of palCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    const path: number = row.Path;
    if (
      proc <= procsAndPaths.length &&
      path <= procsAndPaths[proc - 1].paths.length
    ) {
      procsAndPaths[proc - 1].paths[path - 1].Pallets.push(row.Pallet);
    }
  }

  const stopCmd = db.prepare(
    "SELECT Process, Path, StatGroup, ExpectedCycleTime, Program, ProgramRevision FROM stops WHERE UniqueStr = $uniq ORDER BY RouteNum"
  );
  for (const row of stopCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    const path: number = row.Path;
    if (
      proc <= procsAndPaths.length &&
      path <= procsAndPaths[proc - 1].paths.length
    ) {
      procsAndPaths[proc - 1].paths[path - 1].Stops.push({
        StationGroup: row.StatGroup,
        StationNums: [],
        Tools: {},
        ExpectedCycleTime: parseTimespan(row.ExpectedCycleTime),
        Program: row.Program,
        ProgramRevision: row.ProgramRevision,
      });
    }
  }

  const statCmd = db.prepare(
    "SELECT Process, Path, RouteNum, StatNum FROM stops_stations WHERE UniqueStr = $uniq"
  );
  for (const row of statCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    const path: number = row.Path;
    const route: number = row.RouteNum;
    if (
      proc <= procsAndPaths.length &&
      path <= procsAndPaths[proc - 1].paths.length &&
      route < procsAndPaths[proc - 1].paths[path - 1].Stops.length
    ) {
      procsAndPaths[proc - 1].paths[path - 1].Stops[route].StationNums.push(
        row.StatNum
      );
    }
  }

  const toolCmd = db.prepare(
    "SELECT Process, Path, RouteNum, Tool, ExpectedUse FROM tools WHERE UniqueStr = $uniq"
  );
  for (const row of toolCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    const path: number = row.Path;
    const route: number = row.RouteNum;
    if (
      proc <= procsAndPaths.length &&
      path <= procsAndPaths[proc - 1].paths.length &&
      route <= procsAndPaths[proc - 1].paths[path - 1].Stops.length
    ) {
      procsAndPaths[proc - 1].paths[path - 1].Stops[route - 1].Tools[row.Tool] =
        parseTimespan(row.ExpectedUse);
    }
  }

  const loadUnloadCmd = db.prepare(
    "SELECT Process, Path, StatNum, Load FROM loadunload WHERE UniqueStr = $uniq"
  );
  for (const row of loadUnloadCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    const path: number = row.Path;
    if (
      proc <= procsAndPaths.length &&
      path <= procsAndPaths[proc - 1].paths.length
    ) {
      if (row.Load) {
        procsAndPaths[proc - 1].paths[path - 1].Load.push(row.StatNum);
      } else {
        procsAndPaths[proc - 1].paths[path - 1].Unload.push(row.StatNum);
      }
    }
  }

  const prodCmd = db.prepare(
    "SELECT Process, Path, TimeUTC, Quantity FROM simulated_production WHERE UniqueStr = $uniq ORDER BY Process,Path,TimeUTC"
  );
  for (const row of prodCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    const path: number = row.Path;
    if (
      proc <= procsAndPaths.length &&
      path <= procsAndPaths[proc - 1].paths.length
    ) {
      procsAndPaths[proc - 1].paths[path - 1].SimulatedProduction.push({
        TimeUTC: parseDateFromTicks(row.TimeUTC),
        Quantity: row.Quantity,
      });
    }
  }

  const inspCmd = db.prepare(
    "SELECT Process, Path, InspType, Counter, MaxVal, TimeInterval, RandomFreq, ExpectedTime FROM path_inspections WHERE UniqueStr = $uniq"
  );
  for (const row of inspCmd.iterate({ uniq })) {
    const proc: number = row.Process;
    if (proc > procsAndPaths.length) continue;

    const insp = {
      InspectionType: row.InspType,
      Counter: row.Counter,
      MaxVal: row.MaxVal,
      TimeInterval: parseTimespan(row.TimeInterval),
      RandomFreq: row.RandomFreq,
      ExpectedInspectionTime: parseTimespan(row.ExpectedInspectionTime),
    };

    const path: number = row.Path;
    if (path < 1) {
      // all paths
      for (let i = 0; i < procsAndPaths[proc - 1].paths.length; i++) {
        procsAndPaths[proc - 1].paths[i].Inspections.push(insp);
      }
    } else if (path <= procsAndPaths[proc - 1].paths.length) {
      procsAndPaths[proc - 1].paths[path - 1].Inspections.push(insp);
    }
  }

  return procsAndPaths;
}

function loadJob(db: Database, row: any): any {
  return {
    Unique: row.UniqueStr,
    PartName: row.Part,
    Comment: row.Comment,
    RouteStartUTC: parseDateFromTicks(row.StartUTC),
    RouteEndUTC: parseDateFromTicks(row.EndUTC),
    Archived: row.Archived,
    CopiedToSystem: row.CopiedToSystem,
    ScheduleId: row.ScheduleId,
    ManuallyCreated: row.Manual,
    CyclesOnFirstProcess: db
      .prepare(
        "SELECT PlanQty FROM planqty WHERE UniqueStr = $uniq ORDER BY Path"
      )
      .all({ uniq: row.UniqueStr })
      .map((r) => r.PlanQty),
    Bookings: db
      .prepare(
        "SELECT BookingId FROM scheduled_bookings WHERE UniqueStr = $uniq"
      )
      .all({ uniq: row.UniqueStr })
      .map((r) => r.BookingId),
    HoldEntireJob: undefined, // TODO: hold
    Decrements: db
      .prepare(
        "SELECT DecrementId,Proc1Path,TimeUTC,Quantity FROM job_decrements WHERE JobUnique = $uniq"
      )
      .all({ uniq: row.UniqueStr })
      .map((r) => ({
        DecrementId: r.DecrementId,
        Proc1Path: r.Proc1Path,
        TimeUTC: parseDateFromTicks(r.TimeUTC),
        Quantity: r.Quantity,
      })),
    ProcsAndPaths: loadProcsAndPaths(db, row.UniqueStr),
  };
}

function convertSimStatUse(row: any): any {
  return {
    ScheduleId: row.SimId,
    StationGroup: row.StationGroup,
    StationNum: row.StationNum,
    StartUTC: parseDateFromTicks(row.StartUTC),
    EndUTC: parseDateFromTicks(row.EndUTC),
    UtilizationTime: parseTimespan(row.UtilizationTime),
    PlannedDownTime: parseTimespan(row.PlanDownTime),
  };
}

handlers["job-history"] = (
  db: Database,
  a: {
    startUTC: Date;
    endUTC: Date;
  }
): Readonly<any> => {
  const jobRows = db
    .prepare(
      "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual " +
        " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start"
    )
    .all({
      start: dateToTicks(new Date(a.startUTC)),
      end: dateToTicks(new Date(a.endUTC)),
    });

  const jobs: { [uniq: string]: any } = {};
  for (let i = 0; i < jobRows.length; i++) {
    const row = jobRows[i];
    jobs[row.UniqueStr] = loadJob(db, row);
  }

  const stationUse = db
    .prepare(
      "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime FROM sim_station_use " +
        " WHERE EndUTC >= $start AND StartUTC <= $end"
    )
    .all({
      start: dateToTicks(new Date(a.startUTC)),
      end: dateToTicks(new Date(a.endUTC)),
    })
    .map(convertSimStatUse);

  return { jobs, stationUse };
};
