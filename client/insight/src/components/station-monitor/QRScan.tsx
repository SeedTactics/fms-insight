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

import * as React from 'react';
import QrReader from 'react-qr-reader';
import { Dialog, Button, DialogActions, DialogContent, DialogTitle } from '@material-ui/core';
import { connect } from '../../store/store';
import * as guiState from '../../data/gui-state';
import { openMaterialBySerial } from '../../data/material-details';

export interface QrScanProps {
  readonly dialogOpen: boolean;
  readonly onClose: () => void;
  readonly onScan: (s: string) => void;
}

export function SerialScanner(props: QrScanProps) {
  function onScan(s: string | undefined | null): void {
    if (s === undefined || s == null || s === "") { return; }
    props.onScan(s);
  }
  return (
    <Dialog
      open={props.dialogOpen}
      onClose={props.onClose}
      maxWidth="md"
    >
      <DialogTitle>
        Scan a part's serial
      </DialogTitle>
      <DialogContent>
        <div style={{minWidth: "20em"}}>
          {props.dialogOpen ? <QrReader onScan={onScan} onError={console.log}/> : undefined}
        </div>
      </DialogContent>
      <DialogActions>
        <Button onClick={props.onClose} color="secondary">
          Cancel
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export default connect(
  s => ({
    dialogOpen: s.Gui.scan_qr_dialog_open,
  }),
  {
    onClose: () => ({ type: guiState.ActionType.SetScanQrCodeDialog, open: false}),
    onScan: openMaterialBySerial
  },
)(SerialScanner);
