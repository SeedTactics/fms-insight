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
import { Store, connect } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { atom, useRecoilState, useRecoilValue } from "recoil";

interface ManualInpTypeEntryProps {
  readonly close: () => void;
}

function ManualInspTypeEntry(props: ManualInpTypeEntryProps) {
  const [inspType, setInspType] = React.useState<string | null>(null);
  const mat = useRecoilValue(matDetails.materialDetail);
  const [forceInsp] = matDetails.useForceInspection();
  return (
    <TextField
      label={inspType === "" || inspType === null ? "Inspection Type" : "Inspection Type (press enter)"}
      value={inspType ?? ""}
      onChange={(e) => setInspType(e.target.value)}
      onKeyPress={(e) => {
        if (e.key === "Enter" && mat && inspType && inspType !== "") {
          e.preventDefault();
          forceInsp({
            mat: mat,
            inspType: inspType,
            inspect: true,
          });
          props.close();
        }
      }}
    />
  );
}

export const selectInspTypeDialogOpen = atom<boolean>({
  key: "select-insp-dialog-open",
  default: false,
});

interface SelectInspTypeProps {
  readonly inspTypes: HashSet<string>;
}

function SelectInspTypeDialog(props: SelectInspTypeProps) {
  const mat = useRecoilValue(matDetails.materialDetail);
  const [forceInsp] = matDetails.useForceInspection();
  const [dialogOpen, setDialogOpen] = useRecoilState(selectInspTypeDialogOpen);

  let body: JSX.Element | undefined;

  if (mat === null) {
    body = <p>None</p>;
  } else {
    const inspList = (
      <List>
        {props.inspTypes.toArray({ sortOn: (x) => x }).map((iType) => (
          <ListItem
            key={iType}
            button
            onClick={() => {
              forceInsp({ mat, inspType: iType, inspect: true });
              setDialogOpen(false);
            }}
          >
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
          <ManualInspTypeEntry close={() => setDialogOpen(false)} />
          {inspList}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDialogOpen(false)} color="primary">
            Cancel
          </Button>
        </DialogActions>
      </>
    );
  }
  return (
    <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)} maxWidth="md">
      {body}
    </Dialog>
  );
}

export default connect((st: Store) => ({
  inspTypes: st.Events.last30.mat_summary.inspTypes,
}))(SelectInspTypeDialog);
