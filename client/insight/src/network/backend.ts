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

import * as api from "./api.js";
import { User } from "oidc-client-ts";

export interface JobAPI {
  history(startUTC: Date, endUTC: Date): Promise<Readonly<api.IHistoricData>>;
  filteredHistory(
    startUTC: Date,
    endUTC: Date,
    alreadyKnownSchIds: string[]
  ): Promise<Readonly<api.IHistoricData>>;
  currentStatus(): Promise<Readonly<api.ICurrentStatus>>;
  mostRecentUnfilledWorkordersForPart(part: string): Promise<ReadonlyArray<Readonly<api.IActiveWorkorder>>>;
  setJobComment(unique: string, comment: string): Promise<void>;

  removeMaterialFromAllQueues(materialId: number, operatorName: string | undefined): Promise<void>;
  bulkRemoveMaterialFromQueues(
    operatorName: string | null,
    materialIds: ReadonlyArray<number> | null
  ): Promise<void>;
  setMaterialInQueue(
    materialId: number,
    operatorName: string | null,
    queue: api.QueuePosition
  ): Promise<void>;
  addUnprocessedMaterialToQueue(
    jobUnique: string,
    lastCompletedProcess: number,
    queue: string,
    pos: number,
    operatorName: string | null,
    serial: string
  ): Promise<Readonly<api.IInProcessMaterial> | undefined>;

  addUnallocatedCastingToQueue(
    castingName: string,
    queue: string,
    qty: number,
    operatrorName: string | null,
    serials: string[]
  ): Promise<ReadonlyArray<Readonly<api.IInProcessMaterial>>>;
  signalMaterialForQuarantine(
    materialId: number,
    operName: string | null,
    reason: string | undefined
  ): Promise<void>;
  swapMaterialOnPallet(
    materialId: number,
    operName: string | null,
    mat: Readonly<api.IMatToPutOnPallet>
  ): Promise<void>;
  invalidatePalletCycle(
    materialId: number,
    putMatInQueue: string | null,
    operName: string | null,
    process: number
  ): Promise<void>;
}

export interface FmsAPI {
  fMSInformation(): Promise<Readonly<api.IFMSInfo>>;
  printLabel(materialId: number, process: number): Promise<void>;
  parseBarcode(barcode: string | null): Promise<Readonly<api.IMaterialDetails>>;
  enableVerboseLoggingForFiveMinutes(): Promise<void>;
}

export interface LogAPI {
  get(startUTC: Date, endUTC: Date): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  recent(
    lastSeenCounter: number,
    expectedEndUTCofLastSeen: Date | null | undefined
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  logForMaterial(materialID: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  logForMaterials(materialIDs: ReadonlyArray<number> | null): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  logForSerial(serial: string): Promise<ReadonlyArray<Readonly<api.ILogEntry>>>;
  materialForSerial(serial: string | null): Promise<ReadonlyArray<Readonly<api.IMaterialDetails>>>;

  setInspectionDecision(
    materialID: number,
    inspType: string,
    process: number,
    inspect: boolean,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  recordInspectionCompleted(
    insp: api.NewInspectionCompleted,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  recordCloseoutCompleted(
    insp: api.NewCloseout,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  setWorkorder(
    materialID: number,
    process: number,
    workorder: string,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>>;
  recordOperatorNotes(
    materialID: number,
    process: number,
    operatorName: string | null,
    notes: string
  ): Promise<Readonly<api.ILogEntry>>;
  recordWorkorderComment(
    workorder: string,
    operName: string | null | undefined,
    comment: string
  ): Promise<Readonly<api.ILogEntry>>;
}

export interface MachineAPI {
  getToolsInMachines(): Promise<ReadonlyArray<Readonly<api.IToolInMachine>>>;
  getProgramsInCellController(): Promise<ReadonlyArray<Readonly<api.IProgramInCellController>>>;
  getProgramRevisionContent(program: string, revision: number): Promise<string>;
  getLatestProgramRevisionContent(program: string): Promise<string>;
  getProgramRevisionsInDescendingOrderOfRevision(
    programName: string | null,
    count: number,
    revisionToStart: number | undefined
  ): Promise<ReadonlyArray<Readonly<api.IProgramRevision>>>;
}

export let FmsServerBackend: FmsAPI;
export let JobsBackend: JobAPI;
export let LogBackend: LogAPI;
export let MachineBackend: MachineAPI;
let otherLogServers: ReadonlyArray<string> = [];
export let OtherLogBackends: ReadonlyArray<LogAPI> = [];

export function registerNetworkBackend(): void {
  LogBackend = new api.LogClient();
  MachineBackend = new api.MachinesClient();
  JobsBackend = new api.JobsClient();
  FmsServerBackend = new api.FmsClient();
}

export function setOtherLogBackends(servers: ReadonlyArray<string>): void {
  otherLogServers = servers;
  OtherLogBackends = servers.map((s) => new api.LogClient(s));
}

export function registerBackend(log: LogAPI, job: JobAPI, fms: FmsAPI, machine: MachineAPI): void {
  LogBackend = log;
  JobsBackend = job;
  FmsServerBackend = fms;
  MachineBackend = machine;
}

export function setUserToken(u: User): void {
  const token = u.access_token;
  function fetch(url: RequestInfo, init?: RequestInit) {
    return window.fetch(
      url,
      init
        ? { ...init, headers: { ...init.headers, Authorization: "Bearer " + token } }
        : { headers: { Authorization: "Bearer " + token } }
    );
  }
  FmsServerBackend = new api.FmsClient(undefined, { fetch });
  JobsBackend = new api.JobsClient(undefined, { fetch });
  LogBackend = new api.LogClient(undefined, { fetch });
  OtherLogBackends = otherLogServers.map((s) => new api.LogClient(s, { fetch }));
}

export function instructionUrl(
  partName: string,
  type: string,
  matId: number,
  pallet: string | null,
  proc: number | null,
  operator: string | null
): string {
  return (
    "/api/v1/fms/find-instructions/" +
    encodeURIComponent(partName) +
    "?type=" +
    encodeURIComponent(type) +
    ("&materialID=" + matId.toString()) +
    (proc ? "&process=" + proc.toString() : "") +
    (operator !== null ? "&operatorName=" + encodeURIComponent(operator) : "") +
    (pallet ? "&pallet=" + encodeURIComponent(pallet) : "")
  );
}
