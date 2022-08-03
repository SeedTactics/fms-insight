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

import { addSeconds } from "date-fns";
import * as api from "./api.js";
import { registerBackend } from "./backend.js";
import { LazySeq, mkCompareByProperties } from "@seedtactics/immutable-collections";

export type MockEvents = ReadonlyArray<object /* ILogEntry json */>;

export interface MockData {
  readonly curSt: object /* current status json */;
  readonly jobs: ReadonlyArray<object /* new jobs json */>;
  readonly tools: ReadonlyArray<object /* tool in machine json */>;
  readonly programs: ReadonlyArray<object /* program in cell controller json */>;
  readonly toolUse: { [evtCounter: string]: object /* tool use json */ };
}

interface TransformedMockData {
  readonly curSt: api.ICurrentStatus;
  readonly jobs: api.IHistoricData;
  readonly workorders: Map<string, ReadonlyArray<Readonly<api.IWorkorder>>>;
  readonly tools: ReadonlyArray<Readonly<api.IToolInMachine>>;
  readonly programs: ReadonlyArray<Readonly<api.IProgramInCellController>>;
}

function offsetJob(j: api.Job, offsetSeconds: number) {
  j.routeStartUTC = addSeconds(j.routeStartUTC, offsetSeconds);
  j.routeEndUTC = addSeconds(j.routeEndUTC, offsetSeconds);
  for (const proc of j.procsAndPaths) {
    for (const path of proc.paths) {
      path.simulatedStartingUTC = addSeconds(path.simulatedStartingUTC, offsetSeconds);
      for (const prod of path.simulatedProduction || []) {
        prod.timeUTC = addSeconds(prod.timeUTC, offsetSeconds);
      }
    }
  }
}

function transformTime(offsetSeconds: number, mockD: MockData): TransformedMockData {
  const status = api.CurrentStatus.fromJS(mockD.curSt);
  status.timeOfCurrentStatusUTC = addSeconds(status.timeOfCurrentStatusUTC, offsetSeconds);
  for (const j of Object.values(status.jobs)) {
    offsetJob(j, offsetSeconds);
  }
  for (const m of status.material) {
    // for some reason, status-mock.json uses numbers instead of strings for pallets
    // and strings instead of numbers for face
    if (m.location.pallet) {
      m.location.pallet = m.location.pallet.toString();
    }
    if (m.location.face && typeof m.location.face === "string") {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      m.location.face = parseInt(m.location.face, 10);
    }
  }

  const allNewJobs = mockD.jobs.map(api.NewJobs.fromJS);
  const historicJobs: { [key: string]: api.HistoricJob } = {};
  for (const newJ of allNewJobs) {
    for (const j of newJ.jobs) {
      offsetJob(j, offsetSeconds);
      historicJobs[j.unique] = new api.HistoricJob({
        ...j,
        copiedToSystem: false,
        scheduleId: newJ.scheduleId,
      });
    }
    for (const s of newJ.stationUse || []) {
      s.startUTC = addSeconds(s.startUTC, offsetSeconds);
      s.endUTC = addSeconds(s.endUTC, offsetSeconds);
    }
    for (const w of newJ.currentUnfilledWorkorders || []) {
      w.dueDate = addSeconds(w.dueDate, offsetSeconds);
    }
  }
  const historic: api.IHistoricData = {
    jobs: historicJobs,
    stationUse: LazySeq.of(allNewJobs)
      .flatMap((j) => j.stationUse || [])
      .toMutableArray(),
  };

  return {
    curSt: status,
    jobs: historic,
    workorders: new Map<string, ReadonlyArray<Readonly<api.IWorkorder>>>(),
    tools: mockD.tools.map(api.ToolInMachine.fromJS),
    programs: mockD.programs.map(api.ProgramInCellController.fromJS),
  };
}

async function loadEventsJson(
  offsetSeconds: number,
  mockD: Promise<MockData>,
  evts: Promise<MockEvents>
): Promise<Readonly<api.ILogEntry>[]> {
  const toolUse = (await mockD).toolUse;

  return LazySeq.of(await evts)
    .map((evtJson) => {
      const tools = toolUse[(evtJson as { counter: number }).counter.toString()];
      const e = api.LogEntry.fromJS(tools ? { ...evtJson, tools } : evtJson);
      e.endUTC = addSeconds(e.endUTC, offsetSeconds);
      return e;
    })
    .filter((e) => {
      if (e.type === api.LogType.InspectionResult) {
        // filter out a couple inspection results so they are uninspected
        // and display as uninspected on the station monitor screen
        const mid = e.material[0].id;
        if (mid === 2993 || mid === 2974) {
          return false;
        } else {
          return true;
        }
      } else {
        return true;
      }
    })
    .toMutableArray()
    .sort(
      mkCompareByProperties(
        (e) => e.endUTC.getTime(),
        (e) => e.counter
      )
    );
}

export function registerMockBackend(
  offsetSeconds: number,
  mockD: Promise<MockData>,
  mockEvts: Promise<MockEvents>
): void {
  const data = mockD.then((d) => transformTime(offsetSeconds, d));
  const events = loadEventsJson(offsetSeconds, mockD, mockEvts);

  const fmsB = {
    fMSInformation() {
      return Promise.resolve({
        name: "mock",
        version: "1.0.0",
        requireScanAtWash: false,
        requireWorkorderBeforeAllowWashComplete: false,
        additionalLogServers: [],
        usingLabelPrinterForSerials: false,
      });
    },
    printLabel() {
      return Promise.resolve();
    },
  };

  const jobsB = {
    history(_startUTC: Date, _endUTC: Date): Promise<Readonly<api.IHistoricData>> {
      return data.then((d) => d.jobs);
    },
    currentStatus(): Promise<Readonly<api.ICurrentStatus>> {
      return data.then((d) => d.curSt);
    },
    mostRecentUnfilledWorkordersForPart(part: string): Promise<ReadonlyArray<Readonly<api.IWorkorder>>> {
      return data.then((d) => d.workorders.get(part) || []);
    },
    setJobComment(_uniq: string, _comment: string): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    removeMaterialFromAllQueues(_materialId: number, _operName: string | undefined): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    bulkRemoveMaterialFromQueues(): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    setMaterialInQueue(): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    addUnprocessedMaterialToQueue(): Promise<Readonly<api.IInProcessMaterial> | undefined> {
      // do nothing
      return Promise.resolve(undefined);
    },
    addUnallocatedCastingToQueue(): Promise<ReadonlyArray<Readonly<api.IInProcessMaterial>>> {
      // do nothing
      return Promise.resolve([]);
    },
    signalMaterialForQuarantine(): Promise<void> {
      return Promise.resolve();
    },
    swapMaterialOnPallet(): Promise<void> {
      return Promise.resolve();
    },
    invalidatePalletCycle(): Promise<void> {
      return Promise.resolve();
    },
  };

  const serialsToMatId = data.then(() =>
    events.then((evts) =>
      LazySeq.of(evts)
        .filter((e) => e.type === api.LogType.PartMark)
        .flatMap((e) => e.material.map((m) => [e.result, m.id] as [string, number]))
        .toRMap(
          (x) => x,
          (id1, id2) => id2
        )
    )
  );

  const logB = {
    get(startUTC: Date, endUTC: Date): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      return data.then(() =>
        events.then((evts) => evts.filter((e) => e.endUTC >= startUTC && e.endUTC <= endUTC))
      );
    },
    recent(_lastSeenCounter: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      // no recent events, everything is static
      return Promise.resolve([]);
    },
    logForMaterial(materialID: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      return data.then(() =>
        events.then((evts) => evts.filter((e) => LazySeq.of(e.material).anyMatch((m) => m.id === materialID)))
      );
    },
    logForMaterials(materialIDs: ReadonlyArray<number>): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      const matIds = new Set(materialIDs);
      return data.then(() =>
        events.then((evts) => evts.filter((e) => LazySeq.of(e.material).anyMatch((m) => matIds.has(m.id))))
      );
    },
    logForSerial(serial: string): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      return serialsToMatId.then((s) => {
        const mId = s.get(serial);
        if (mId !== undefined) {
          return this.logForMaterial(mId);
        } else {
          return Promise.resolve([]);
        }
      });
    },
    getWorkorders(_ids: string[]): Promise<ReadonlyArray<Readonly<api.IWorkorderSummary>>> {
      // no workorder summaries
      return Promise.resolve([]);
    },

    setInspectionDecision(
      materialID: number,
      inspType: string,
      process: number,
      inspect: boolean,
      jobUnique?: string,
      partName?: string
    ): Promise<Readonly<api.ILogEntry>> {
      const mat = new api.LogMaterial({
        id: materialID,
        uniq: jobUnique || "",
        part: partName || "",
        proc: process,
        numproc: 1,
        face: "1",
      });
      const evt = {
        counter: 0,
        material: [mat],
        pal: "",
        type: api.LogType.Inspection,
        startofcycle: false,
        endUTC: new Date(),
        loc: "Inspection",
        locnum: 1,
        result: inspect.toString(),
        program: "",
        elapsed: "00:00:00",
        active: "00:00:00",
        details: {
          InspectionType: inspType,
        },
      };
      return data.then(() =>
        events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    recordInspectionCompleted(
      insp: api.NewInspectionCompleted,
      jobUnique?: string,
      partName?: string
    ): Promise<Readonly<api.ILogEntry>> {
      const mat = new api.LogMaterial({
        id: insp.materialID,
        uniq: jobUnique || "",
        part: partName || "",
        proc: insp.process,
        numproc: 1,
        face: "1",
      });
      const evt: api.ILogEntry = {
        counter: 0,
        material: [mat],
        pal: "",
        type: api.LogType.InspectionResult,
        startofcycle: false,
        endUTC: new Date(),
        loc: "InspectionComplete",
        locnum: insp.inspectionLocationNum,
        result: insp.success.toString(),
        program: insp.inspectionType ?? "",
        elapsed: insp.elapsed,
        active: insp.active,
        details: insp.extraData,
      };
      return data.then(() =>
        events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    recordWashCompleted(
      wash: api.NewWash,
      jobUnique?: string,
      partName?: string
    ): Promise<Readonly<api.ILogEntry>> {
      const mat = new api.LogMaterial({
        id: wash.materialID,
        uniq: jobUnique || "",
        part: partName || "",
        proc: wash.process,
        numproc: 1,
        face: "1",
      });
      const evt: api.ILogEntry = {
        counter: 0,
        material: [mat],
        pal: "",
        type: api.LogType.Wash,
        startofcycle: false,
        endUTC: new Date(),
        loc: "Wash",
        locnum: wash.washLocationNum,
        result: "",
        program: "",
        elapsed: wash.elapsed,
        active: wash.active,
        details: wash.extraData,
      };
      return data.then(() =>
        events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    setWorkorder(
      materialID: number,
      process: number,
      workorder: string,
      jobUnique?: string,
      partName?: string
    ): Promise<Readonly<api.ILogEntry>> {
      const mat = new api.LogMaterial({
        id: materialID,
        uniq: jobUnique || "",
        part: partName || "",
        proc: process,
        numproc: 1,
        face: "1",
      });
      const evt: api.ILogEntry = {
        counter: 0,
        material: [mat],
        pal: "",
        type: api.LogType.OrderAssignment,
        startofcycle: false,
        endUTC: new Date(),
        loc: "OrderAssignment",
        locnum: 1,
        result: workorder,
        program: "",
        elapsed: "00:00:00",
        active: "00:00:00",
      };
      return data.then(() =>
        events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    recordOperatorNotes(materialID: number, process: number, operatorName: string | null, notes: string) {
      const mat = new api.LogMaterial({
        id: materialID,
        uniq: "",
        part: "",
        proc: process,
        numproc: 1,
        face: "",
      });
      const evt: api.ILogEntry = {
        counter: 0,
        material: [mat],
        pal: "",
        type: api.LogType.GeneralMessage,
        startofcycle: false,
        endUTC: new Date(),
        loc: "Message",
        locnum: 1,
        result: "Operator Notes",
        program: "OperatorNotes",
        elapsed: "00:00:00",
        active: "00:00:00",
        details: {
          operator: operatorName || "",
          note: notes,
        },
      };
      return data.then(() =>
        events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
  };

  const machineB = {
    getToolsInMachines() {
      return data.then((d) => d.tools ?? []);
    },
    getProgramsInCellController() {
      return data.then((d) => d.programs ?? []);
    },
    getProgramRevisionContent(program: string) {
      return Promise.resolve("GCODE for " + program + " would be here");
    },
    getLatestProgramRevisionContent(program: string) {
      return Promise.resolve("GCODE for " + program + " would be here");
    },
    getProgramRevisionsInDescendingOrderOfRevision() {
      return Promise.resolve([]);
    },
  };

  registerBackend(logB, jobsB, fmsB, machineB);
}
