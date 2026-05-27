/* Copyright (c) 2026, John Lenz

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

import { act } from "react";
import { Provider, createStore } from "jotai";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it, vi } from "vitest";

import { MaterialDialog } from "./Material.js";
import { materialDialogOpen } from "../../cell-status/material-details.js";
import { ApiException } from "../../network/api.js";
import { registerBackend, type FmsAPI, type JobAPI, type LogAPI, type MachineAPI } from "../../network/backend.js";

const barcode = "1111222|SO100100|10000";
const barcodeError = "Serial 1111222 is assigned to sales order SO100100 line 10000 which is not currently scheduled";

function unexpectedCall(name: string): never {
  throw new Error(`Unexpected backend call in MaterialDialog test: ${name}`);
}

const logBackend: LogAPI = {
  get() {
    return unexpectedCall("LogAPI.get");
  },
  recent() {
    return unexpectedCall("LogAPI.recent");
  },
  logForMaterial() {
    return unexpectedCall("LogAPI.logForMaterial");
  },
  logForMaterials() {
    return unexpectedCall("LogAPI.logForMaterials");
  },
  logForSerial() {
    return unexpectedCall("LogAPI.logForSerial");
  },
  materialForSerial() {
    return unexpectedCall("LogAPI.materialForSerial");
  },
  setInspectionDecision() {
    return unexpectedCall("LogAPI.setInspectionDecision");
  },
  recordInspectionCompleted() {
    return unexpectedCall("LogAPI.recordInspectionCompleted");
  },
  recordCloseoutCompleted() {
    return unexpectedCall("LogAPI.recordCloseoutCompleted");
  },
  setWorkorder() {
    return unexpectedCall("LogAPI.setWorkorder");
  },
  recordOperatorNotes() {
    return unexpectedCall("LogAPI.recordOperatorNotes");
  },
  recordWorkorderComment() {
    return unexpectedCall("LogAPI.recordWorkorderComment");
  },
  getActiveWorkorder() {
    return unexpectedCall("LogAPI.getActiveWorkorder");
  },
  cancelRebooking() {
    return unexpectedCall("LogAPI.cancelRebooking");
  },
  requestRebooking() {
    return unexpectedCall("LogAPI.requestRebooking");
  },
};

const jobBackend: JobAPI = {
  history() {
    return unexpectedCall("JobAPI.history");
  },
  recent() {
    return unexpectedCall("JobAPI.recent");
  },
  currentStatus() {
    return unexpectedCall("JobAPI.currentStatus");
  },
  setJobComment() {
    return unexpectedCall("JobAPI.setJobComment");
  },
  removeMaterialFromAllQueues() {
    return unexpectedCall("JobAPI.removeMaterialFromAllQueues");
  },
  bulkRemoveMaterialFromQueues() {
    return unexpectedCall("JobAPI.bulkRemoveMaterialFromQueues");
  },
  setMaterialInQueue() {
    return unexpectedCall("JobAPI.setMaterialInQueue");
  },
  addUnprocessedMaterialToQueue() {
    return unexpectedCall("JobAPI.addUnprocessedMaterialToQueue");
  },
  addUnallocatedCastingToQueue() {
    return unexpectedCall("JobAPI.addUnallocatedCastingToQueue");
  },
  signalMaterialForQuarantine() {
    return unexpectedCall("JobAPI.signalMaterialForQuarantine");
  },
  swapMaterialOnPallet() {
    return unexpectedCall("JobAPI.swapMaterialOnPallet");
  },
  invalidatePalletCycle() {
    return unexpectedCall("JobAPI.invalidatePalletCycle");
  },
  unscheduledRebookings() {
    return unexpectedCall("JobAPI.unscheduledRebookings");
  },
};

const machineBackend: MachineAPI = {
  getToolsInMachines() {
    return unexpectedCall("MachineAPI.getToolsInMachines");
  },
  getProgramsInCellController() {
    return unexpectedCall("MachineAPI.getProgramsInCellController");
  },
  getProgramRevisionContent() {
    return unexpectedCall("MachineAPI.getProgramRevisionContent");
  },
  getLatestProgramRevisionContent() {
    return unexpectedCall("MachineAPI.getLatestProgramRevisionContent");
  },
  getProgramRevisionsInDescendingOrderOfRevision() {
    return unexpectedCall("MachineAPI.getProgramRevisionsInDescendingOrderOfRevision");
  },
};

const fmsBackend: FmsAPI = {
  fMSInformation() {
    return unexpectedCall("FmsAPI.fMSInformation");
  },
  printLabel() {
    return unexpectedCall("FmsAPI.printLabel");
  },
  parseBarcode() {
    return Promise.reject(
      new ApiException("An unexpected server error occurred.", 400, barcodeError, {}, null),
    );
  },
  enableVerboseLoggingForFiveMinutes() {
    return unexpectedCall("FmsAPI.enableVerboseLoggingForFiveMinutes");
  },
};

async function flushUi() {
  for (let i = 0; i < 3; i += 1) {
    await act(async () => {
      await Promise.resolve();
    });
  }
}

afterEach(() => {
  document.body.innerHTML = "";
});

it("shows barcode parse errors inside the material dialog", async () => {
  const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
  registerBackend(logBackend, jobBackend, fmsBackend, machineBackend);

  const store = createStore();
  store.set(materialDialogOpen, { type: "Barcode", barcode, toQueue: null });

  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);

  try {
    await act(async () => {
      root.render(
        <Provider store={store}>
          <MaterialDialog />
        </Provider>,
      );
    });

    await flushUi();

    expect(document.body.textContent).toContain(barcode);
    expect(document.body.textContent).toContain(barcodeError);
    expect(document.body.textContent).not.toContain("An unexpected server error occurred.");
  } finally {
    await act(async () => {
      root.unmount();
    });
    consoleError.mockRestore();
  }
});
