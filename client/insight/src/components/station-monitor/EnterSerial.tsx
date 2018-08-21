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

import * as React from "react";
import Button from "@material-ui/core/Button";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogTitle from "@material-ui/core/DialogTitle";
import TextField from "@material-ui/core/TextField";

import { MaterialDetailTitle } from "./Material";
import { Store, connect, mkAC, AppActionBeforeMiddleware, DispatchAction } from "../../store/store";
import * as matDetails from "../../data/material-details";
import * as guiState from "../../data/gui-state";

interface ManualSerialEntryProps {
  readonly mat: matDetails.MaterialDetail;
  readonly assignSerial: (data: matDetails.AssignSerialData) => void;
}

interface ManualSerialEntryState {
  readonly serial: string;
}

class ManualSerialEntry extends React.PureComponent<ManualSerialEntryProps, ManualSerialEntryState> {
  state = { serial: "" };

  render() {
    return (
      <TextField
        label={this.state.serial === "" ? "Serial" : "Serial (press enter)"}
        value={this.state.serial}
        onChange={e => this.setState({ serial: e.target.value })}
        onKeyPress={e => {
          if (e.key === "Enter" && this.state.serial && this.state.serial !== "") {
            e.preventDefault();
            this.props.assignSerial({
              mat: this.props.mat,
              serial: this.state.serial
            });
          }
        }}
      />
    );
  }
}

interface EnterSerialProps {
  readonly mats: matDetails.MaterialDetail | null;
  readonly onClose: DispatchAction<guiState.ActionType.SetSerialDialogOpen>;
  readonly assignSerial: (data: matDetails.AssignSerialData) => void;
}

function EnterSerialDialog(props: EnterSerialProps) {
  let body: JSX.Element | undefined;

  if (props.mats === null) {
    body = <p>None</p>;
  } else {
    const mat = props.mats;
    if (mat === null) {
      body = <p>None</p>;
    } else {
      body = (
        <>
          <DialogTitle disableTypography>
            <MaterialDetailTitle partName={mat.partName} serial={mat.serial} />
          </DialogTitle>
          <DialogContent>
            <ManualSerialEntry mat={mat} assignSerial={props.assignSerial} />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => props.onClose({ open: false })} color="primary">
              Cancel
            </Button>
          </DialogActions>
        </>
      );
    }
  }
  return (
    <Dialog open={props.mats !== null} onClose={() => props.onClose({ open: false })} maxWidth="md">
      {body}
    </Dialog>
  );
}

export default connect(
  (st: Store) => ({
    mats: st.Gui.serial_dialog_open ? st.MaterialDetails.material : null
  }),
  {
    onClose: mkAC(guiState.ActionType.SetSerialDialogOpen),
    assignSerial: (data: matDetails.AssignSerialData) =>
      [
        matDetails.assignSerial(data),
        { type: guiState.ActionType.SetSerialDialogOpen, open: false }
      ] as AppActionBeforeMiddleware
  }
)(EnterSerialDialog);
