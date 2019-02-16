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
  }
}
const ToBackground = new RendererToBackground(window.electronIpc);

export const JobsBackend = {
  history(startUTC: Date, endUTC: Date): Promise<Readonly<api.IHistoricData>> {
    return Promise.resolve({ jobs: {}, stationUse: [] });
  },
  currentStatus(): Promise<Readonly<api.ICurrentStatus>> {
    return Promise.resolve({
      jobs: {},
      pallets: {},
      material: [],
      alarms: [],
      queues: {},
      timeOfCurrentStatusUTC: new Date()
    });
  },
  mostRecentUnfilledWorkordersForPart(
    part: string
  ): Promise<ReadonlyArray<Readonly<api.IPartWorkorder>>> {
    return Promise.resolve([]);
  },

  removeMaterialFromAllQueues(materialId: number): Promise<void> {
    // do nothing
    return Promise.resolve();
  },
  setMaterialInQueue(
    materialId: number,
    queue: api.QueuePosition
  ): Promise<void> {
    // do nothing
    return Promise.resolve();
  },
  addUnprocessedMaterialToQueue(
    jobUnique: string,
    lastCompletedProcess: number,
    queue: string,
    pos: number,
    serial: string
  ): Promise<void> {
    // do nothing
    return Promise.resolve();
  }
};

export const LogBackend = {
  async get(
    startUTC: Date,
    endUTC: Date
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await ToBackground.send("log-get", {
      startUTC,
      endUTC
    });
    return entries.map(api.LogEntry.fromJS);
  },

  recent(
    lastSeenCounter: number
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    return Promise.resolve([]);
  },
  logForMaterial(
    materialID: number
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    return Promise.resolve([]);
  },
  logForSerial(
    serial: string
  ): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    return Promise.resolve([]);
  },
  getWorkorders(
    ids: string[]
  ): Promise<ReadonlyArray<Readonly<api.IWorkorderSummary>>> {
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
    return Promise.reject("Not implemented");
  },

  recordInspectionCompleted(
    insp: api.NewInspectionCompleted,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  recordWashCompleted(
    insp: api.NewWash,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  setWorkorder(
    materialID: number,
    workorder: string,
    process: number,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },
  setSerial(
    materialID: number,
    serial: string,
    process: number,
    jobUnique?: string,
    partName?: string
  ): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  }
};
