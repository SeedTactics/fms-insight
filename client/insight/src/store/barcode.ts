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

import { AppActionBeforeMiddleware } from "./store";
import { openMaterialBySerial } from "../data/material-details";
import * as guiState from '../data/gui-state';

enum ScanMode {
  LoadMaterialDetails,
  AddToQueue,
}

export function initBarcodeListener(
  dispatch: (a: AppActionBeforeMiddleware | AppActionBeforeMiddleware[]) => void
): void {
  let timeout: NodeJS.Timer | undefined;
  let scanActive: boolean = false;
  let scannedTxt: string = "";
  let scanMode: ScanMode = ScanMode.LoadMaterialDetails;

  function cancelDetection() {
    scannedTxt = "";
    scanActive = false;
  }

  function startDetection(m: ScanMode) {
    scanMode = m;
    scannedTxt = "";
    scanActive = true;
    timeout = setTimeout(cancelDetection, 10 * 1000);
  }

  function success() {
    if (timeout) {
      clearTimeout(timeout);
      timeout = undefined;
    }
    scanActive = false;

    switch (scanMode) {
      case ScanMode.LoadMaterialDetails:
        dispatch(openMaterialBySerial(scannedTxt));
        break;
      case ScanMode.AddToQueue:
        dispatch([
          openMaterialBySerial(scannedTxt),
          {
            type: guiState.ActionType.SetAddMatToQueueDialog,
            queue: undefined,
            st: guiState.AddMatToQueueDialogState.DialogOpenToAddMaterial,
          }
        ]);
    }
  }

  function onKeyDown(k: KeyboardEvent) {
    if (k.keyCode === 112) { // F1
      startDetection(ScanMode.LoadMaterialDetails);
      k.stopPropagation();
      k.preventDefault();
    } else if (k.keyCode === 113) { // F2
      startDetection(ScanMode.AddToQueue);
      k.stopPropagation();
      k.preventDefault();
    } else if (scanActive && k.keyCode === 13) { // Enter
      success();
      k.stopPropagation();
      k.preventDefault();
    } else if (scanActive && k.key && k.key.length === 1) {
      if (/[a-zA-Z0-9-_]/.test(k.key)) {
        scannedTxt += k.key;
        k.stopPropagation();
        k.preventDefault();
      }
    }
  }

  document.addEventListener("keydown", onKeyDown);
}