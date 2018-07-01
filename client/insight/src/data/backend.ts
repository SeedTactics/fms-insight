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

import * as api from './api';

export interface JobAPI {
  history(startUTC: Date, endUTC: Date): Promise<api.HistoricData>;
  currentStatus(): Promise<api.CurrentStatus>;
  mostRecentUnfilledWorkordersForPart(part: string): Promise<api.PartWorkorder[]>;

  removeMaterialFromAllQueues(materialId: number): Promise<void>;
  setMaterialInQueue(materialId: number, queue: api.QueuePosition): Promise<void>;
  addUnprocessedMaterialToQueue(
    jobUnique: string, lastCompletedProcess: number, queue: string, pos: number, serial: string
  ): Promise<void>;
}

export const JobsBackend: JobAPI = new api.JobsClient();

export interface ServerAPI {
  fMSInformation(): Promise<api.FMSInfo>;
}

export const ServerBackend: ServerAPI = new api.ServerClient();

export interface LogAPI {
  get(startUTC: Date, endUTC: Date): Promise<api.LogEntry[]>;
  recent(lastSeenCounter: number): Promise<api.LogEntry[]>;
  logForMaterial(materialID: number): Promise<api.LogEntry[]>;
  logForSerial(serial: string): Promise<api.LogEntry[]>;
  getWorkorders(ids: string[]): Promise<api.WorkorderSummary[]>;

  setInspectionDecision(inspType: string, mat: api.LogMaterial, inspect: boolean): Promise<api.LogEntry>;
  recordInspectionCompleted(insp: api.NewInspectionCompleted): Promise<api.LogEntry>;
  recordWashCompleted(insp: api.NewWash): Promise<api.LogEntry>;
  setWorkorder(workorder: string, mat: api.LogMaterial): Promise<api.LogEntry>;
  setSerial(serial: string, mat: api.LogMaterial): Promise<api.LogEntry>;
}

export const LogBackend: LogAPI = new api.LogClient();