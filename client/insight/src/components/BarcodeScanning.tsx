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

import { memo, useEffect, useState } from "react";
import { materialDialogOpen } from "../cell-status/material-details.js";
import { atom, useAtom, useSetAtom } from "jotai";
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  TextField,
  Tooltip,
} from "@mui/material";
import { CameraAlt } from "@mui/icons-material";
import { IDetectedBarcode, Scanner } from "@yudiel/react-qr-scanner";

export const BarcodeListener = memo(function BarcodeListener(): null {
  const setBarcode = useSetAtom(materialDialogOpen);
  useEffect(() => {
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

      setBarcode({ type: "Barcode", barcode: scannedTxt, toQueue: null });
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

    document.addEventListener("keydown", onKeyDown, { capture: true });

    return () => document.removeEventListener("keydown", onKeyDown);
  }, [setBarcode]);

  return null;
});

export const QRScanButton = memo(function QRScanButton() {
  const [dialogOpen, setDialogOpen] = useState(false);
  const setBarcode = useSetAtom(materialDialogOpen);
  const [manual, setManual] = useState("");

  if (window.location.protocol !== "https:" && window.location.hostname !== "localhost") return null;

  function onScan(results: ReadonlyArray<IDetectedBarcode>): void {
    if (results.length > 0 && results[0].rawValue !== null && results[0].rawValue !== "") {
      setBarcode({ type: "Barcode", barcode: results[0].rawValue, toQueue: null });
      setDialogOpen(false);
      setManual("");
    }
  }

  function onManual(): void {
    if (manual !== "") {
      setBarcode({ type: "Barcode", barcode: manual, toQueue: null });
    }
    setDialogOpen(false);
    setManual("");
  }

  return (
    <>
      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)}>
        <DialogTitle>Scan a QR Code</DialogTitle>
        <DialogContent>
          <Box width="15em" height="15em">
            {dialogOpen ? <Scanner onScan={onScan} /> : undefined}
          </Box>
          <Box marginTop="1em">
            <TextField
              label="Manual Entry"
              value={manual}
              variant="outlined"
              fullWidth
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  onManual();
                }
              }}
              onChange={(e) => setManual(e.target.value)}
            />
          </Box>
        </DialogContent>
        <DialogActions>
          {manual !== "" ? <Button onClick={onManual}>Submit</Button> : undefined}
          <Button onClick={() => setDialogOpen(false)}>Cancel</Button>
        </DialogActions>
      </Dialog>
      <Tooltip title="Scan QR Code">
        <IconButton onClick={() => setDialogOpen(true)} size="large">
          <CameraAlt />
        </IconButton>
      </Tooltip>
    </>
  );
});

// A version of the above dialog that in addition passes along which queue the material
// is being added to.
export const scanBarcodeToAddToQueueDialog = atom<string | null>(null);

export const AddByBarcodeDialog = memo(function AddByBarcodeDialog() {
  const [queue, setQueue] = useAtom(scanBarcodeToAddToQueueDialog);
  const [manual, setManual] = useState<string>("");
  const setBarcode = useSetAtom(materialDialogOpen);

  function onScan(results: ReadonlyArray<IDetectedBarcode>): void {
    if (queue !== null && results.length > 0 && results[0].rawValue !== null && results[0].rawValue !== "") {
      setBarcode({ type: "Barcode", barcode: results[0].rawValue, toQueue: queue });
      setQueue(null);
      setManual("");
    }
  }

  function onManual(): void {
    if (manual !== "" && queue !== null) {
      setBarcode({ type: "Barcode", barcode: manual, toQueue: queue });
    }
    setQueue(null);
    setManual("");
  }

  return (
    <Dialog open={queue !== null} onClose={() => setQueue(null)} maxWidth="md">
      <DialogTitle>Scan a Barcode To Add To {queue}</DialogTitle>
      <DialogContent>
        {window.location.protocol === "https:" || window.location.hostname === "localhost" ? (
          <Box width="15em" height="15em">
            {queue !== null ? <Scanner onScan={onScan} /> : undefined}
          </Box>
        ) : undefined}
        <Box marginTop="1em">
          <TextField
            label="Manual Entry"
            value={manual}
            variant="outlined"
            fullWidth
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                onManual();
              }
            }}
            onChange={(e) => setManual(e.target.value)}
          />
        </Box>
      </DialogContent>
      <DialogActions>
        {manual !== "" ? <Button onClick={onManual}>Submit</Button> : undefined}
        <Button onClick={() => setQueue(null)}>Cancel</Button>
      </DialogActions>
    </Dialog>
  );
});
