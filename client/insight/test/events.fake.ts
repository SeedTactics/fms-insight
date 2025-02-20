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
  IToolUse,
} from "../src/network/api.js";
import { faker } from "@faker-js/faker";
import { durationToSeconds } from "../src/util/parseISODuration.js";
import { addSeconds, addMinutes } from "date-fns";
import { LazySeq } from "@seedtactics/immutable-collections";

faker.seed(0x6f79);

export function fakeMaterial(part?: string, proc?: number): LogMaterial {
  return new LogMaterial({
    id: faker.number.int(),
    uniq: "uniq" + faker.string.alphanumeric(),
    part: part || "part" + faker.string.alphanumeric(),
    proc: proc || faker.number.int({ max: 4 }),
    numproc: faker.number.int({ max: 4 }),
    face: faker.number.int({ max: 10 }),
    serial: "serial" + faker.string.alphanumeric(),
    workorder: "work" + faker.string.alphanumeric(),
  });
}

export function fakeInProcMaterial(matId: number, queue?: string, queuePos?: number): InProcessMaterial {
  return new InProcessMaterial({
    materialID: matId,
    jobUnique: "uniq" + faker.string.alphanumeric(),
    partName: "part" + faker.string.alphanumeric(),
    path: faker.number.int({ max: 100 }),
    process: faker.number.int({ max: 100 }),
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
    tooluse: undefined,
  };
  es.push(start);
  es.push(e);
}

export function fakeInspSignal(
  mat?: LogMaterial,
  inspType?: string,
  now?: Date,
  counter?: number,
): ILogEntry {
  mat = mat || fakeMaterial();
  inspType = inspType || "MyInspType";
  now = now || new Date(2017, 9, 5);
  counter = counter || 100;
  const path = [
    new MaterialProcessActualPath({
      materialID: mat.id,
      process: 1,
      pallet: 6,
      loadStation: 1,
      stops: [new Stop({ stationName: "MC", stationNum: 4 })],
      unloadStation: 2,
    }).toJSON(),
  ];
  return {
    counter: counter,
    material: [mat],
    pal: 0,
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
    pal: 0,
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
  counter?: number,
): ILogEntry {
  mat = mat || fakeMaterial();
  inspType = inspType || "MyInspType";
  now = now || new Date(2017, 9, 5);
  success = success || true;
  counter = counter || 100;
  return {
    counter,
    material: [mat],
    pal: 0,
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

export function fakeMachineCycle({
  counter,
  numMats,
  part,
  proc,
  pal,
  program,
  mcName,
  mcNum,
  time,
  elapsedMin,
  activeMin,
  tooluse,
}: {
  counter: number;
  numMats?: number;
  part: string;
  proc: number;
  pal?: number;
  program: string;
  mcName?: string;
  mcNum?: number;
  time: Date;
  elapsedMin: number;
  activeMin?: number;
  tooluse?: ReadonlyArray<IToolUse>;
}): ReadonlyArray<ILogEntry> {
  const material = LazySeq.ofRange(0, numMats ?? 1)
    .map(() => fakeMaterial(part, proc))
    .toMutableArray();

  const es: Array<ILogEntry> = [];
  addStartAndEnd(es, {
    counter,
    material,
    pal: pal ?? faker.number.int(),
    type: LogType.MachineCycle,
    startofcycle: false,
    endUTC: time,
    loc: mcName ?? "MC",
    locnum: mcNum ?? 1,
    result: "",
    program,
    elapsed: `PT${elapsedMin}M`,
    active: activeMin ? `PT${activeMin}M` : "PT-1S",
    tooluse: tooluse?.map((t) => new ToolUse(t)),
  });
  return es;
}

export function fakeLoadOrUnload({
  counter,
  numMats,
  material,
  part,
  proc,
  pal,
  isLoad,
  lulNum,
  time,
  elapsedMin,
  activeMin,
}: {
  counter: number;
  numMats?: number;
  material?: LogMaterial[];
  part: string;
  proc: number;
  pal?: number;
  isLoad: boolean;
  time: Date;
  lulNum?: number;
  elapsedMin: number;
  activeMin?: number;
}): ReadonlyArray<ILogEntry> {
  material =
    material ??
    LazySeq.ofRange(0, numMats ?? 1)
      .map(() => fakeMaterial(part, proc))
      .toMutableArray();

  const es: Array<ILogEntry> = [];
  addStartAndEnd(es, {
    counter,
    material,
    pal: pal ?? faker.number.int(),
    type: LogType.LoadUnloadCycle,
    startofcycle: false,
    endUTC: time,
    loc: "L/U",
    locnum: lulNum ?? 1,
    result: isLoad ? "LOAD" : "UNLOAD",
    program: isLoad ? "LOAD" : "UNLOAD",
    elapsed: `PT${elapsedMin}M`,
    active: activeMin ? `PT${activeMin}M` : "PT-1S",
  });
  return es;
}

export function fakePalletBegin({
  counter,
  material,
  pal,
  time,
}: {
  counter: number;
  material: LogMaterial[];
  pal: number;
  time: Date;
}) {
  return {
    counter,
    material,
    pal: pal,
    type: LogType.PalletCycle,
    startofcycle: true,
    endUTC: time,
    loc: "Pallet Cycle",
    locnum: 1,
    result: "PalletCycle",
    program: "",
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
  program,
  activeTimeMins,
  counter,
}: {
  time: Date;
  machineTime: number;
  part?: string;
  proc?: number;
  pallet?: number;
  noInspections?: boolean;
  includeTools?: boolean;
  program?: string;
  activeTimeMins?: number;
  counter: number;
}): ReadonlyArray<ILogEntry> {
  const pal = pallet || faker.number.int();
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
    program: program ?? "prog" + faker.string.alphanumeric(),
    elapsed: elapsed,
    active: activeTimeMins ? `PT${activeTimeMins}M` : elapsed,
  });

  if (includeTools) {
    es[es.length - 1].tooluse = fakeToolUsage();
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
    loc: "Pallet Cycle",
    locnum: 1,
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
    pal: faker.number.int(),
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

export function fakeCloseoutComplete(mat?: LogMaterial): ILogEntry {
  mat = mat || fakeMaterial();
  return {
    counter: 100,
    material: [mat],
    pal: faker.number.int(),
    type: LogType.CloseOut,
    startofcycle: false,
    endUTC: new Date(2017, 9, 5),
    loc: "CloseOut",
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
    pal: faker.number.int(),
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
    pal: 0,
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
    pal: 0,
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

export function fakeToolUsage(): Array<ToolUse> {
  const use = faker.number.int({ min: 5, max: 30 });
  return [
    new ToolUse({
      tool: faker.string.alphanumeric(),
      pocket: faker.number.int(),
      toolUseDuringCycle: "PT" + use.toString() + "S",
      totalToolUseAtEndOfCycle: "PT" + (use * 2).toString() + "S",
    }),
    new ToolUse({
      tool: faker.string.alphanumeric(),
      pocket: faker.number.int(),
      toolUseDuringCycle: "PT" + (use + 5).toString() + "S",
      totalToolUseAtEndOfCycle: "PT" + (use + 20).toString() + "S",
      toolChangeOccurred: true,
    }),
  ];
}

export function fakeNormalToolUsage({
  tool,
  pocket,
  minsAtEnd,
}: {
  tool: string;
  pocket: number;
  minsAtEnd: number;
}): IToolUse {
  return {
    tool,
    pocket,
    toolUseDuringCycle: `PT${faker.number.int(10000)}S`,
    totalToolUseAtEndOfCycle: `PT${minsAtEnd}M`,
  };
}

export function fakeToolChangeBeforeCycle({
  tool,
  pocket,
  minsAtEnd,
}: {
  tool: string;
  pocket: number;
  minsAtEnd: number;
}): IToolUse {
  return {
    tool,
    pocket,
    toolUseDuringCycle: `PT${minsAtEnd}M`,
    totalToolUseAtEndOfCycle: `PT${minsAtEnd}M`,
  };
}

export function fakeToolChangeDuringCycle({
  tool,
  pocket,
  use,
  minsAtEnd,
  life,
}: {
  tool: string;
  pocket: number;
  use: number;
  minsAtEnd: number;
  life: number;
}): IToolUse {
  return {
    tool,
    pocket,
    toolUseDuringCycle: `PT${use}M`,
    totalToolUseAtEndOfCycle: `PT${minsAtEnd}M`,
    configuredToolLife: `PT${life}M`,
    toolChangeOccurred: true,
  };
}
