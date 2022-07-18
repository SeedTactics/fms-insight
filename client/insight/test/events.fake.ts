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

import {
  LogMaterial,
  ILogEntry,
  LogType,
  LocType,
  MaterialProcessActualPath,
  Stop,
  InProcessMaterial,
  InProcessMaterialLocation,
  InProcessMaterialAction,
  ToolUse,
  ActionType,
} from "../src/network/api.js";
import { faker } from "@faker-js/faker";
import { durationToSeconds } from "../src/util/parseISODuration.js";
import { addSeconds, addMinutes } from "date-fns";

faker.seed(0x6f79);

export function fakeMaterial(part?: string, proc?: number): LogMaterial {
  return new LogMaterial({
    id: faker.datatype.number(),
    uniq: "uniq" + faker.random.alphaNumeric(),
    part: part || "part" + faker.random.alphaNumeric(),
    proc: proc || faker.datatype.number({ max: 4 }),
    numproc: faker.datatype.number({ max: 4 }),
    face: "face" + faker.random.alphaNumeric(),
    serial: "serial" + faker.random.alphaNumeric(),
    workorder: "work" + faker.random.alphaNumeric(),
  });
}

export function fakeInProcMaterial(matId: number, queue?: string, queuePos?: number): InProcessMaterial {
  return new InProcessMaterial({
    materialID: matId,
    jobUnique: "uniq" + faker.random.alphaNumeric(),
    partName: "part" + faker.random.alphaNumeric(),
    path: faker.datatype.number({ max: 100 }),
    process: faker.datatype.number({ max: 100 }),
    signaledInspections: [],
    location:
      queue && queuePos
        ? new InProcessMaterialLocation({
            type: LocType.InQueue,
            currentQueue: queue,
            queuePosition: queuePos,
          })
        : new InProcessMaterialLocation({
            type: LocType.Free,
          }),
    action: new InProcessMaterialAction({
      type: ActionType.Waiting,
    }),
  });
}

function addStartAndEnd(es: ILogEntry[], e: ILogEntry): void {
  const elapsed = durationToSeconds(e.elapsed);
  const startTime = addSeconds(e.endUTC, -elapsed);
  const start = {
    ...e,
    counter: e.counter - 1,
    startofcycle: true,
    endUTC: startTime,
    result: "",
    program: "",
  };
  es.push(start);
  es.push(e);
}

export function fakeInspSignal(
  mat?: LogMaterial,
  inspType?: string,
  now?: Date,
  counter?: number
): ILogEntry {
  mat = mat || fakeMaterial();
  inspType = inspType || "MyInspType";
  now = now || new Date(2017, 9, 5);
  counter = counter || 100;
  const path = [
    new MaterialProcessActualPath({
      materialID: mat.id,
      process: 1,
      pallet: "6",
      loadStation: 1,
      stops: [new Stop({ stationName: "MC", stationNum: 4 })],
      unloadStation: 2,
    }).toJSON(),
  ];
  return {
    counter: counter,
    material: [mat],
    pal: "",
    type: LogType.Inspection,
    startofcycle: false,
    endUTC: now,
    loc: "Inspection",
    locnum: 1,
    result: "True",
    program: "theprogramshouldbeignored",
    elapsed: "PT0S",
    active: "PT0S",
    details: {
      InspectionType: inspType,
      ActualPath: JSON.stringify(path),
    },
  };
}

export function fakeInspForce(mat?: LogMaterial, inspType?: string, now?: Date, counter?: number): ILogEntry {
  mat = mat || fakeMaterial();
  inspType = inspType || "MyInspType";
  now = now || new Date(2017, 9, 5);
  counter = counter || 100;
  return {
    counter: counter,
    material: [mat],
    pal: "",
    type: LogType.InspectionForce,
    startofcycle: false,
    endUTC: now,
    loc: "Inspection",
    locnum: 1,
    result: "True",
    program: inspType,
    elapsed: "PT0S",
    active: "PT0S",
  };
}

export function fakeInspComplete(
  mat?: LogMaterial,
  inspType?: string,
  now?: Date,
  success?: boolean,
  counter?: number
): ILogEntry {
  mat = mat || fakeMaterial();
  inspType = inspType || "MyInspType";
  now = now || new Date(2017, 9, 5);
  success = success || true;
  counter = counter || 100;
  return {
    counter,
    material: [mat],
    pal: "",
    type: LogType.InspectionResult,
    startofcycle: false,
    endUTC: now,
    loc: "InspectionComplete",
    locnum: 1,
    result: success.toString(),
    program: inspType,
    elapsed: "PT0S",
    active: "PT0S",
  };
}

export function fakeCycle({
  time,
  machineTime,
  part,
  proc,
  pallet,
  noInspections,
  includeTools,
  counter,
}: {
  time: Date;
  machineTime: number;
  part?: string;
  proc?: number;
  pallet?: string;
  noInspections?: boolean;
  includeTools?: boolean;
  counter: number;
}): ReadonlyArray<ILogEntry> {
  const pal = pallet || "pal" + faker.random.alphaNumeric();
  const material = [fakeMaterial(part, proc)];

  time = addMinutes(time, 5);

  const es: ILogEntry[] = [];

  addStartAndEnd(es, {
    counter,
    material,
    pal,
    type: LogType.LoadUnloadCycle,
    startofcycle: false,
    endUTC: time,
    loc: "L/U",
    locnum: 1,
    result: "LOAD",
    program: "LOAD",
    elapsed: "PT6M",
    active: "PT6M",
  });

  counter += 2;
  time = addMinutes(time, 5);

  addStartAndEnd(es, {
    counter,
    material,
    pal,
    type: LogType.PalletInStocker,
    startofcycle: false,
    loc: "Stocker",
    locnum: 4,
    endUTC: time,
    result: "WaitForMachine",
    program: "Arrive",
    elapsed: "PT4M",
    active: "PT0S",
  });

  counter += 2;
  time = addMinutes(time, machineTime + 3);

  const elapsed = "PT" + machineTime.toString() + "M";
  addStartAndEnd(es, {
    counter,
    material,
    pal,
    type: LogType.MachineCycle,
    startofcycle: false,
    endUTC: time,
    loc: "MC",
    locnum: 1,
    result: "",
    program: "prog" + faker.random.alphaNumeric(),
    elapsed: elapsed,
    active: elapsed,
  });

  if (includeTools) {
    es[es.length - 1].tools = fakeToolUsage();
  }

  counter += 2;
  time = addMinutes(time, 10);

  addStartAndEnd(es, {
    counter,
    material,
    pal,
    type: LogType.LoadUnloadCycle,
    startofcycle: false,
    endUTC: time,
    loc: "L/U",
    locnum: 2,
    result: "UNLOAD",
    program: "UNLOAD",
    elapsed: "PT3M",
    active: "PT3M",
  });

  if (!noInspections) {
    time = addMinutes(time, 5);
    counter += 1;
    if (faker.datatype.boolean() === true) {
      es.push(fakeInspForce(material[0], "Insp1", time, counter));
      time = addSeconds(time, 5);
      counter += 1;
    }
    es.push(fakeInspSignal(material[0], "Insp1", time, counter));
  }

  es.push({
    counter: counter + 1,
    pal,
    material: [],
    type: LogType.PalletCycle,
    startofcycle: false,
    endUTC: time,
    loc: "L/U",
    locnum: 2,
    result: "PalletCycle",
    program: "",
    elapsed: "PT44M",
    active: "PT-1M",
  });

  if (!noInspections) {
    time = addMinutes(time, 7);
    counter += 1;
    es.push(fakeInspComplete(material[0], "Insp1", time, true, counter));
  }

  return es;
}

export function fakeSerial(mat?: LogMaterial, serial?: string): ILogEntry {
  mat = mat || fakeMaterial();
  serial = serial || "serial1234";
  return {
    counter: 100,
    material: [mat],
    pal: faker.random.alphaNumeric(),
    type: LogType.PartMark,
    startofcycle: false,
    endUTC: new Date(2017, 9, 5),
    loc: "Mark",
    locnum: 1,
    result: serial,
    program: "",
    elapsed: "PT0S",
    active: "PT0S",
  };
}

export function fakeWashComplete(mat?: LogMaterial): ILogEntry {
  mat = mat || fakeMaterial();
  return {
    counter: 100,
    material: [mat],
    pal: faker.random.alphaNumeric(),
    type: LogType.Wash,
    startofcycle: false,
    endUTC: new Date(2017, 9, 5),
    loc: "Wash",
    locnum: 1,
    result: "",
    program: "",
    elapsed: "PT0S",
    active: "PT0S",
  };
}

export function fakeWorkorderAssign(mat?: LogMaterial, workorder?: string): ILogEntry {
  mat = mat || fakeMaterial();
  workorder = workorder || "work12345";
  return {
    counter: 100,
    material: [mat],
    pal: faker.random.alphaNumeric(),
    type: LogType.OrderAssignment,
    startofcycle: false,
    endUTC: new Date(2017, 9, 5),
    loc: "OrderAssignment",
    locnum: 1,
    result: workorder,
    program: "",
    elapsed: "PT0S",
    active: "PT0S",
  };
}

export function fakeAddToQueue(queue?: string, mat?: LogMaterial): ILogEntry {
  mat = mat || fakeMaterial();
  return {
    counter: 100,
    material: [mat],
    pal: "",
    type: LogType.AddToQueue,
    startofcycle: false,
    endUTC: new Date(2017, 9, 5),
    loc: queue ?? "thequeue",
    locnum: 1,
    result: "",
    program: "",
    elapsed: "PT0S",
    active: "PT0S",
  };
}

export function fakeRemoveFromQueue(queue?: string, mat?: LogMaterial): ILogEntry {
  mat = mat || fakeMaterial();
  return {
    counter: 100,
    material: [mat],
    pal: "",
    type: LogType.RemoveFromQueue,
    startofcycle: false,
    endUTC: new Date(2017, 9, 5),
    loc: queue ?? "thequeue",
    locnum: 1,
    result: "",
    program: "",
    elapsed: "PT0S",
    active: "PT0S",
  };
}

export function fakeToolUsage(): { [tool: string]: ToolUse } {
  const use = faker.datatype.number({ min: 5, max: 30 });
  return {
    [faker.random.alphaNumeric()]: new ToolUse({
      toolUseDuringCycle: "PT" + use.toString() + "S",
      totalToolUseAtEndOfCycle: "PT" + (use * 2).toString() + "S",
    }),
    [faker.random.alphaNumeric()]: new ToolUse({
      toolUseDuringCycle: "PT" + (use + 5).toString() + "S",
      totalToolUseAtEndOfCycle: "PT" + (use + 20).toString() + "S",
      toolChangeOccurred: true,
    }),
  };
}
