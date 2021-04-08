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

import { Database } from "sqlite";

let dbP: Promise<Database> = Promise.reject("No opened file yet");
export function setDatabase(p: Promise<Database>) {
  dbP = p;
}
export const handlers: { [key: string]: (x: any) => Promise<any> } = {};

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

async function convertRowToLog(db: Database, row: any): Promise<any> {
  const cmd =
    "SELECT stations_mat.MaterialID, UniqueStr, Process, PartName, NumProcesses, Face, Serial, Workorder " +
    " FROM stations_mat " +
    " LEFT OUTER JOIN matdetails ON stations_mat.MaterialID = matdetails.MaterialID " +
    " WHERE stations_mat.Counter = $cntr " +
    " ORDER BY stations_mat.Counter ASC";
  const matRows = await db.all(cmd, { $cntr: row.Counter });
  const details: { [key: string]: string } = {};
  const tools: { [key: string]: Readonly<any> } = {};
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
          ToolUseDuringCycle: parseTimespan(row.UseInCycle),
          TotalToolUseAtEndOfCycle: parseTimespan(row.UseAtEndOfCycle),
          ConfiguredToolLife: parseTimespan(row.ToolLife),
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

handlers["log-get"] = async (a: {
  startUTC: Date;
  endUTC: Date;
}): Promise<ReadonlyArray<Readonly<any>>> => {
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
};

handlers["log-for-material"] = async (a: {
  materialID: number;
}): Promise<ReadonlyArray<Readonly<any>>> => {
  const db = await dbP;
  const rows = await db.all(
    "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
      " FROM stations WHERE Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) ORDER BY Counter ASC",
    {
      $mat: a.materialID,
    }
  );
  return await Promise.all(rows.map((r) => convertRowToLog(db, r)));
};

handlers["log-for-materials"] = async (a: {
  materialIDs: Iterable<number>;
}): Promise<ReadonlyArray<Readonly<any>>> => {
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
};

handlers["log-for-serial"] = async (a: {
  serial: string;
}): Promise<ReadonlyArray<Readonly<any>>> => {
  const db = await dbP;
  const rows = await db.all(
    "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
      " FROM stations WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.Serial = $ser) ORDER BY Counter ASC",
    {
      $ser: a.serial,
    }
  );
  return await Promise.all(rows.map((r) => convertRowToLog(db, r)));
};

async function loadProcsAndPaths(
  db: Database,
  uniq: string
): Promise<ReadonlyArray<{ paths: ReadonlyArray<any> }>> {
  const procsAndPaths: Array<{ paths: Array<any> }> = [];

  db.each(
    "SELECT Process, StartingUTC, PartsPerPallet, PathGroup, SimAverageFlowTime, InputQueue, OutputQueue, LoadTime, UnloadTime, Fixture, Face, Casting FROM pathdata WHERE UniqueStr = $uniq ORDER BY Process,Path",
    { $uniq: uniq },
    (_, row) => {
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
  );

  db.each(
    "SELECT Process, Path, Pallet FROM pallets WHERE UniqueStr = $uniq",
    { $uniq: uniq },
    (_, row) => {
      const proc: number = row.Process;
      const path: number = row.Path;
      if (
        proc <= procsAndPaths.length &&
        path <= procsAndPaths[proc - 1].paths.length
      ) {
        procsAndPaths[proc - 1].paths[path - 1].Pallets.push(row.Pallet);
      }
    }
  );

  db.each(
    "SELECT Process, Path, StatGroup, ExpectedCycleTime, Program, ProgramRevision FROM stops WHERE UniqueStr = $uniq ORDER BY RouteNum",
    { $uniq: uniq },
    (_, row) => {
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
  );

  db.each(
    "SELECT Process, Path, RouteNum, StatNum FROM stops_stations WHERE UniqueStr = $uniq",
    { $uniq: uniq },
    (_, row) => {
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
  );

  db.each(
    "SELECT Process, Path, RouteNum, Tool, ExpectedUse FROM tools WHERE UniqueStr = $uniq",
    { $uniq: uniq },
    (_, row) => {
      const proc: number = row.Process;
      const path: number = row.Path;
      const route: number = row.RouteNum;
      if (
        proc <= procsAndPaths.length &&
        path <= procsAndPaths[proc - 1].paths.length &&
        route <= procsAndPaths[proc - 1].paths[path - 1].Stops.length
      ) {
        procsAndPaths[proc - 1].paths[path - 1].Stops[route - 1].Tools[
          row.Tool
        ] = parseTimespan(row.ExpectedUse);
      }
    }
  );

  db.each(
    "SELECT Process, Path, StatNum, Load FROM loadunload WHERE UniqueStr = $uniq",
    { $uniq: uniq },
    (_, row) => {
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
  );

  db.each(
    "SELECT Process, Path, TimeUTC, Quantity FROM simulated_production WHERE UniqueStr = $uniq ORDER BY Process,Path,TimeUTC",
    { $uniq: uniq },
    (_, row) => {
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
  );

  db.each(
    "SELECT Process, Path, InspType, Counter, MaxVal, TimeInterval, RandomFreq, ExpectedTime FROM path_inspections WHERE UniqueStr = $uniq",
    { $uniq: uniq },
    (_, row) => {
      const proc: number = row.Process;
      if (proc > procsAndPaths.length) return;

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
  );

  return procsAndPaths;
}

async function loadJob(db: Database, row: any): Promise<any> {
  var uniq = { $uniq: row.UniqueStr };
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
    CyclesOnFirstProcess: (
      await db.all(
        "SELECT PlanQty FROM planqty WHERE UniqueStr = $uniq ORDER BY Path",
        uniq
      )
    ).map((r) => r.PlanQty),
    Bookings: (
      await db.all(
        "SELECT BookingId FROM scheduled_bookings WHERE UniqueStr = $uniq",
        uniq
      )
    ).map((r) => r.BookingId),
    HoldEntireJob: undefined, // TODO: hold
    Decrements: (
      await db.all(
        "SELECT DecrementId,Proc1Path,TimeUTC,Quantity FROM job_decrements WHERE JobUnique = $uniq",
        uniq
      )
    ).map((r) => ({
      DecrementId: r.DecrementId,
      Proc1Path: r.Proc1Path,
      TimeUTC: parseDateFromTicks(r.TimeUTC),
      Quantity: r.Quantity,
    })),
    ProcsAndPaths: await loadProcsAndPaths(db, row.UniqueStr),
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

handlers["job-history"] = async (a: {
  startUTC: Date;
  endUTC: Date;
}): Promise<Readonly<any>> => {
  const db = await dbP;

  const jobRows = await db.all(
    "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual " +
      " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start",
    {
      $start: dateToTicks(new Date(a.startUTC)),
      $end: dateToTicks(new Date(a.endUTC)),
    }
  );

  const jobs: { [uniq: string]: any } = {};
  for (let i = 0; i < jobRows.length; i++) {
    const row = jobRows[i];
    jobs[row.UniqueStr] = await loadJob(db, row);
  }

  const stationUse = (
    await db.all(
      "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime FROM sim_station_use " +
        " WHERE EndUTC >= $start AND StartUTC <= $end",
      {
        $start: dateToTicks(new Date(a.startUTC)),
        $end: dateToTicks(new Date(a.endUTC)),
      }
    )
  ).map(convertSimStatUse);

  return { jobs, stationUse };
};
