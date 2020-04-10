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

import * as React from "react";
import { Dialog, Button, DialogActions, DialogContent, DialogTitle, TextField } from "@material-ui/core";
import { connect } from "../store/store";
import * as guiState from "../data/gui-state";
import { openMaterialBySerial } from "../data/material-details";

interface ManualScanProps {
  readonly dialogOpen: boolean;
  readonly onClose: () => void;
  readonly onScan: (s: string) => void;
}

interface ManualScanState {
  serial: string;
}

class ManualScan extends React.PureComponent<ManualScanProps, ManualScanState> {
  state = { serial: "" };

  render() {
    return (
      <Dialog open={this.props.dialogOpen} onClose={this.props.onClose} maxWidth="md">
        <DialogTitle>Enter a part&apos;s serial</DialogTitle>
        <DialogContent>
          <div style={{ minWidth: "20em" }}>
            <TextField
              label={this.state.serial === "" ? "Serial" : "Serial (press enter)"}
              value={this.state.serial}
              onChange={(e) => this.setState({ serial: e.target.value })}
              onKeyPress={(e) => {
                if (e.key === "Enter" && this.state.serial && this.state.serial !== "") {
                  e.preventDefault();
                  this.props.onScan(this.state.serial);
                }
              }}
            />
          </div>
        </DialogContent>
        <DialogActions>
          <Button
            onClick={() => this.props.onScan(this.state.serial)}
            disabled={this.state.serial === ""}
            color="secondary"
          >
            Open
          </Button>
          <Button onClick={this.props.onClose} color="secondary">
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    );
  }
}

export default connect(
  (s) => ({
    dialogOpen: s.Gui.manual_serial_entry_dialog_open,
  }),
  {
    onClose: () => ({
      type: guiState.ActionType.SetManualSerialEntryDialog,
      open: false,
    }),
    onScan: (s: string) => [
      ...openMaterialBySerial(s, true),
      {
        type: guiState.ActionType.SetAddMatToQueueName,
        queue: undefined,
      },
      {
        type: guiState.ActionType.SetManualSerialEntryDialog,
        open: false,
      },
    ],
  }
)(ManualScan);
