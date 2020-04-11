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

import * as api from "./api";
import { User } from "oidc-client";
import { LazySeq } from "./lazyseq";

export interface JobAPI {
  history(startUTC: Date, endUTC: Date): Promise<Readonly<api.IHistoricData>>;
  currentStatus(): Promise<Readonly<api.ICurrentStatus>>;
  mostRecentUnfilledWorkordersForPart(part: string): Promise<ReadonlyArray<Readonly<api.IPartWorkorder>>>;
  setJobComment(unique: string, comment: string): Promise<void>;

  removeMaterialFromAllQueues(materialId: number): Promise<void>;
  setMaterialInQueue(materialId: number, queue: api.QueuePosition): Promise<void>;
  addUnprocessedMaterialToQueue(
    jobUnique: string,
    lastCompletedProcess: number,
    pathGroup: number,
    queue: string,
    pos: number,
    serial: string
  ): Promise<void>;

  addUnallocatedCastingToQueue(
    castingName: string,
    queue: string,
    pos: number,
    serials: string[],
    qty: number
  ): Promise<void>;
  addUnallocatedCastingToQueueByPart(partName: string, queue: string, pos: number, serial: string): Promise<void>;
}

export interface FmsAPI {
  fMSInformation(): Promise<Readonly<api.IFMSInfo>>;
  printLabel(materialId: number, process: number, loadStation: number): Promise<void>;
}

export interface LogAPI {
  get(startUTC: Date, endUTC: Date): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  recent(lastSeenCounter: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  logForMaterial(materialID: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  logForSerial(serial: string): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  getWorkorders(ids: string[]): Promise<ReadonlyArray<Readonly<api.IWorkorderSummary>>>;

  setInspectionDecision(
    materialID: number,
    inspType: string,
    inspect: boolean,
    process: number,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  recordInspectionCompleted(
    insp: api.NewInspectionCompleted,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  recordWashCompleted(insp: api.NewWash, jobUnique?: string, partName?: string): Promise<Readonly<api.ILogEntry>>;
  setWorkorder(
    materialID: number,
    workorder: string,
    process: number,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  setSerial(
    materialID: number,
    serial: string,
    process: number,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  recordOperatorNotes(
    materialID: number,
    notes: string,
    process: number,
    operatorName: string | null
  ): Promise<Readonly<api.ILogEntry>>;
}

export const BackendHost = process.env.NODE_ENV === "production" ? undefined : "localhost:5000";
const BackendUrl = BackendHost ? "http://" + BackendHost : undefined;

export let FmsServerBackend: FmsAPI = new api.FmsClient(BackendUrl);
export let JobsBackend: JobAPI = new api.JobsClient(BackendUrl);
export let LogBackend: LogAPI = new api.LogClient(BackendUrl);
let otherLogServers: ReadonlyArray<string> = [];
export let OtherLogBackends: ReadonlyArray<LogAPI> = [];

export function setOtherLogBackends(servers: ReadonlyArray<string>) {
  otherLogServers = servers;
  OtherLogBackends = servers.map((s) => new api.LogClient(s));
}

export function setUserToken(u: User) {
  const token = u.access_token || u.id_token;
  function fetch(url: RequestInfo, init?: RequestInit) {
    return window.fetch(
      url,
      init
        ? { ...init, headers: { ...init.headers, Authorization: "Bearer " + token } }
        : { headers: { Authorization: "Bearer " + token } }
    );
  }
  FmsServerBackend = new api.FmsClient(BackendUrl, { fetch });
  JobsBackend = new api.JobsClient(BackendUrl, { fetch });
  LogBackend = new api.LogClient(BackendUrl, { fetch });
  OtherLogBackends = otherLogServers.map((s) => new api.LogClient(s, { fetch }));
}

export interface MockData {
  readonly curSt: Readonly<api.ICurrentStatus>;
  readonly jobs: Readonly<api.IHistoricData>;
  readonly workorders: Map<string, ReadonlyArray<Readonly<api.IPartWorkorder>>>;
  readonly events: Promise<Readonly<api.ILogEntry>[]>;
}

function initMockBackend(data: Promise<MockData>) {
  FmsServerBackend = {
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

  JobsBackend = {
    history(_startUTC: Date, _endUTC: Date): Promise<Readonly<api.IHistoricData>> {
      return data.then((d) => d.jobs);
    },
    currentStatus(): Promise<Readonly<api.ICurrentStatus>> {
      return data.then((d) => d.curSt);
    },
    mostRecentUnfilledWorkordersForPart(part: string): Promise<ReadonlyArray<Readonly<api.IPartWorkorder>>> {
      return data.then((d) => d.workorders.get(part) || []);
    },
    setJobComment(_uniq: string, _comment: string): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    removeMaterialFromAllQueues(_materialId: number): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    setMaterialInQueue(_materialId: number, _queue: api.QueuePosition): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    addUnprocessedMaterialToQueue(
      _jobUnique: string,
      _lastCompletedProcess: number,
      _pathGroup: number,
      _queue: string,
      _pos: number,
      _serial: string
    ): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    addUnallocatedCastingToQueue(
      _casting: string,
      _queue: string,
      _pos: number,
      _serials: string[],
      _qty: number
    ): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
    addUnallocatedCastingToQueueByPart(
      _partName: string,
      _queue: string,
      _pos: number,
      _serial: string
    ): Promise<void> {
      // do nothing
      return Promise.resolve();
    },
  };

  const serialsToMatId = data.then((d) =>
    d.events.then((evts) =>
      LazySeq.ofIterable(evts)
        .filter((e) => e.type === api.LogType.PartMark)
        .flatMap((e) => e.material.map((m) => [e.result, m.id] as [string, number]))
        .toMap(
          (x) => x,
          (id1, id2) => id2
        )
    )
  );

  LogBackend = {
    get(startUTC: Date, endUTC: Date): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      return data.then((d) => d.events.then((evts) => evts.filter((e) => e.endUTC >= startUTC && e.endUTC <= endUTC)));
    },
    recent(_lastSeenCounter: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      // no recent events, everything is static
      return Promise.resolve([]);
    },
    logForMaterial(materialID: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      return data.then((d) =>
        d.events.then((evts) => evts.filter((e) => LazySeq.ofIterable(e.material).anyMatch((m) => m.id === materialID)))
      );
    },
    logForSerial(serial: string): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
      return serialsToMatId.then((s) => {
        const mId = s.get(serial);
        if (mId.isSome()) {
          return this.logForMaterial(mId.get());
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
      inspect: boolean,
      process: number,
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
      return data.then((d) =>
        d.events.then((evts) => {
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
        program: insp.inspectionType,
        elapsed: insp.elapsed,
        active: insp.active,
        details: insp.extraData,
      };
      return data.then((d) =>
        d.events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    recordWashCompleted(wash: api.NewWash, jobUnique?: string, partName?: string): Promise<Readonly<api.ILogEntry>> {
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
      return data.then((d) =>
        d.events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    setWorkorder(
      materialID: number,
      workorder: string,
      process: number,
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
      return data.then((d) =>
        d.events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    setSerial(
      materialID: number,
      serial: string,
      process: number,
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
        type: api.LogType.PartMark,
        startofcycle: false,
        endUTC: new Date(),
        loc: "Mark",
        locnum: 1,
        result: serial,
        program: "",
        elapsed: "00:00:00",
        active: "00:00:00",
      };
      return data.then((d) =>
        d.events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
    recordOperatorNotes(materialID: number, notes: string, process: number, operatorName: string | null) {
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
      return data.then((d) =>
        d.events.then((evts) => {
          evts.push(evt);
          return evt;
        })
      );
    },
  };
}

export function registerMockBackend() {
  const mockDataPromise = new Promise<MockData>(function (resolve: (d: MockData) => void) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).FMS_INSIGHT_RESOLVE_MOCK_DATA = resolve;
  });
  initMockBackend(mockDataPromise);
}

export function registerBackend(log: LogAPI, job: JobAPI, fms: FmsAPI) {
  LogBackend = log;
  JobsBackend = job;
  FmsServerBackend = fms;
}
