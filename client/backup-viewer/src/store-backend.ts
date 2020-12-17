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
import * as api from "../../insight/src/data/api";
import { IpcRenderer } from "electron";
import { RendererToBackground } from "./ipc";

declare global {
  interface Window {
    electronIpc: IpcRenderer;
    bmsVersion: string;
  }
}
const ToBackground = new RendererToBackground(window.electronIpc);

export const ServerBackend = {
  fMSInformation() {
    return Promise.resolve({
      name: "FMS Insight Backup Viewer",
      version: window.bmsVersion,
      requireScanAtWash: false,
      requireWorkorderBeforeAllowWashComplete: false,
      additionalLogServers: [],
    });
  },
};

export const JobsBackend = {
  history(): Promise<Readonly<api.IHistoricData>> {
    return Promise.resolve({ jobs: {}, stationUse: [] });
  },
  currentStatus(): Promise<Readonly<api.ICurrentStatus>> {
    return Promise.resolve({
      jobs: {},
      pallets: {},
      material: [],
      alarms: [],
      queues: {},
      timeOfCurrentStatusUTC: new Date(),
    });
  },
  mostRecentUnfilledWorkordersForPart(): Promise<
    ReadonlyArray<Readonly<api.IPartWorkorder>>
  > {
    return Promise.resolve([]);
  },
  setJobComment(): Promise<void> {
    // do nothing
    return Promise.resolve();
  },

  removeMaterialFromAllQueues(): Promise<void> {
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
  addUnprocessedMaterialToQueue(): Promise<
    Readonly<api.IInProcessMaterial> | undefined
  > {
    // do nothing
    return Promise.resolve(undefined);
  },
  addUnallocatedCastingToQueue(): Promise<
    ReadonlyArray<Readonly<api.IInProcessMaterial>>
  > {
    // do nothing
    return Promise.resolve([]);
  },
  addUnallocatedCastingToQueueByPart(): Promise<
    Readonly<api.IInProcessMaterial> | undefined
  > {
    // do nothing
    return Promise.resolve(undefined);
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

export const LogBackend = {
  async get(
    startUTC: Date,
    endUTC: Date
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await ToBackground.send("log-get", {
      startUTC,
      endUTC,
    });
    return entries.map(api.LogEntry.fromJS);
  },

  recent(
    _lastSeenCounter: number
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    return Promise.reject("not implemented");
  },
  async logForMaterial(
    materialID: number
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await ToBackground.send(
      "log-for-material",
      {
        materialID,
      }
    );
    return entries.map(api.LogEntry.fromJS);
  },
  async logForMaterials(
    materialIDs: ReadonlyArray<number>
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await ToBackground.send(
      "log-for-materials",
      {
        materialIDs,
      }
    );
    return entries.map(api.LogEntry.fromJS);
  },
  async logForSerial(
    serial: string
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await ToBackground.send(
      "log-for-serial",
      {
        serial,
      }
    );
    return entries.map(api.LogEntry.fromJS);
  },
  getWorkorders(): Promise<ReadonlyArray<Readonly<api.IWorkorderSummary>>> {
    return Promise.resolve([]);
  },

  setInspectionDecision(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  recordInspectionCompleted(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  recordWashCompleted(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  setWorkorder(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },
  setSerial(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },
  recordOperatorNotes(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },
};
