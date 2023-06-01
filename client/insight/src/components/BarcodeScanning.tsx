/* Copyright (c) 2022, John Lenz

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

import * as React from "react";
import { materialDialogOpen } from "../cell-status/material-details.js";
import { useSetAtom } from "jotai";

export const BarcodeListener = React.memo(function BarcodeListener(): null {
  const setBarcode = useSetAtom(materialDialogOpen);
  React.useEffect(() => {
    let timeout: number | undefined;
    let scanActive = false;
    let scannedTxt = "";
    let lastBangTime: number | null = null;

    function cancelDetection() {
      scannedTxt = "";
      scanActive = false;
    }

    function startDetection() {
      scannedTxt = "";
      scanActive = true;
      timeout = window.setTimeout(cancelDetection, 3 * 1000);
    }

    function success() {
      if (timeout) {
        clearTimeout(timeout);
        timeout = undefined;
      }
      scanActive = false;

      setBarcode({ type: "Barcode", barcode: scannedTxt });
    }

    function onKeyDown(k: KeyboardEvent) {
      if (k.key === "!") {
        lastBangTime = Date.now();
      } else if (k.code === "F1" && !scanActive) {
        startDetection();
        k.stopPropagation();
        k.preventDefault();
      } else if (k.key === "*" && !scanActive && lastBangTime !== null && Date.now() - lastBangTime < 1000) {
        startDetection();
        k.stopPropagation();
        k.preventDefault();
      } else if (scanActive && k.code === "Enter") {
        success();
        k.stopPropagation();
        k.preventDefault();
      } else if (scanActive && k.key && k.key.length === 1) {
        if (/[a-zA-Z0-9-_,;]/.test(k.key)) {
          scannedTxt += k.key;
          k.stopPropagation();
          k.preventDefault();
        }
      }
    }

    document.addEventListener("keydown", onKeyDown);

    return () => document.removeEventListener("keydown", onKeyDown);
  }, [setBarcode]);

  return null;
});
