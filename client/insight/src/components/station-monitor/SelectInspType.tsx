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
import List from "@material-ui/core/List";
import ListItem from "@material-ui/core/ListItem";
import ListItemText from "@material-ui/core/ListItemText";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogTitle from "@material-ui/core/DialogTitle";
import SearchIcon from "@material-ui/icons/Search";
import TextField from "@material-ui/core/TextField";
import { HashSet } from "prelude-ts";

import { MaterialDetailTitle } from "./Material";
import { Store, connect, mkAC, AppActionBeforeMiddleware, DispatchAction } from "../../store/store";
import * as matDetails from "../../data/material-details";
import * as guiState from "../../data/gui-state";

interface ManualInspTypeEntryProps {
  readonly mat: matDetails.MaterialDetail;
  readonly forceInspection: (data: matDetails.ForceInspectionData) => void;
}

interface ManualInspTypeEntryState {
  readonly inspType: string;
}

class ManualInspTypeEntry extends React.PureComponent<ManualInspTypeEntryProps, ManualInspTypeEntryState> {
  state = { inspType: "" };

  render() {
    return (
      <TextField
        label={this.state.inspType === "" ? "Inspection Type" : "Inspection Type (press enter)"}
        value={this.state.inspType}
        onChange={(e) => this.setState({ inspType: e.target.value })}
        onKeyPress={(e) => {
          if (e.key === "Enter" && this.state.inspType && this.state.inspType !== "") {
            e.preventDefault();
            this.props.forceInspection({
              mat: this.props.mat,
              inspType: this.state.inspType,
              inspect: true,
            });
          }
        }}
      />
    );
  }
}

interface SelectInspTypeProps {
  readonly inspTypes: HashSet<string>;
  readonly mats: matDetails.MaterialDetail | null;
  readonly onClose: DispatchAction<guiState.ActionType.SetInspTypeDialogOpen>;
  readonly forceInspection: (data: matDetails.ForceInspectionData) => void;
}

function SelectInspTypeDialog(props: SelectInspTypeProps) {
  let body: JSX.Element | undefined;

  if (props.mats === null) {
    body = <p>None</p>;
  } else {
    const mat = props.mats;
    if (mat === null) {
      body = <p>None</p>;
    } else {
      const inspList = (
        <List>
          {props.inspTypes.toArray({ sortOn: (x) => x }).map((iType) => (
            <ListItem key={iType} button onClick={() => props.forceInspection({ mat, inspType: iType, inspect: true })}>
              <ListItemIcon>
                <SearchIcon />
              </ListItemIcon>
              <ListItemText primary={iType} />
            </ListItem>
          ))}
        </List>
      );

      body = (
        <>
          <DialogTitle disableTypography>
            <MaterialDetailTitle partName={mat.partName} serial={mat.serial} />
          </DialogTitle>
          <DialogContent>
            <ManualInspTypeEntry mat={mat} forceInspection={props.forceInspection} />
            {inspList}
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
    inspTypes: st.Events.last30.mat_summary.inspTypes,
    mats: st.Gui.insptype_dialog_open ? st.MaterialDetails.material : null,
  }),
  {
    onClose: mkAC(guiState.ActionType.SetInspTypeDialogOpen),
    forceInspection: (data: matDetails.ForceInspectionData) =>
      [
        matDetails.forceInspection(data),
        { type: guiState.ActionType.SetInspTypeDialogOpen, open: false },
      ] as AppActionBeforeMiddleware,
  }
)(SelectInspTypeDialog);
