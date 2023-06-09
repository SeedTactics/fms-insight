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
import { Button } from "@mui/material";
import { List } from "@mui/material";
import { ListItem } from "@mui/material";
import { ListItemText } from "@mui/material";
import { ListItemIcon } from "@mui/material";
import { Dialog } from "@mui/material";
import { DialogActions } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { Search as SearchIcon } from "@mui/icons-material";
import { TextField } from "@mui/material";

import * as matDetails from "../../cell-status/material-details.js";
import { last30InspectionTypes } from "../../cell-status/names.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { DisplayLoadingAndError } from "../ErrorsAndLoading.js";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";

const selectInspTypeDialogOpen = atom<boolean>(false);

function ManualInspTypeEntry(): JSX.Element {
  const [inspType, setInspType] = React.useState<string | null>(null);
  const mat = useAtomValue(matDetails.materialInDialogInfo);
  const [forceInsp] = matDetails.useForceInspection();
  const close = useSetAtom(selectInspTypeDialogOpen);

  return (
    <TextField
      sx={{ mt: "5px" }}
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
          close(false);
        }
      }}
    />
  );
}

function InspectionList() {
  const mat = useAtomValue(matDetails.materialInDialogInfo);
  const [forceInsp] = matDetails.useForceInspection();
  const inspTypes = useAtomValue(last30InspectionTypes);
  const sortedInspTypes = LazySeq.of(inspTypes).sortBy((x) => x);
  const close = useSetAtom(selectInspTypeDialogOpen);

  if (mat === null) return null;
  return (
    <List>
      {sortedInspTypes.map((iType) => (
        <ListItem
          key={iType}
          button
          onClick={() => {
            forceInsp({ mat, inspType: iType, inspect: true });
            close(false);
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
}

export function SignalInspectionButton() {
  const setForceInspOpen = useSetAtom(selectInspTypeDialogOpen);
  const curMat = useAtomValue(matDetails.inProcessMaterialInDialog);
  if (curMat === null || curMat.materialID < 0) return null;
  return (
    <Button color="primary" onClick={() => setForceInspOpen(true)}>
      Signal Inspection
    </Button>
  );
}

export const SelectInspTypeDialog = React.memo(function SelectInspTypeDialog() {
  const [dialogOpen, setDialogOpen] = useAtom(selectInspTypeDialogOpen);

  let body: JSX.Element | undefined;
  if (dialogOpen === false) {
    body = <p>None</p>;
  } else {
    body = (
      <>
        <DialogTitle>Select Inspection Type</DialogTitle>
        <DialogContent>
          <DisplayLoadingAndError>
            <ManualInspTypeEntry />
            <InspectionList />
          </DisplayLoadingAndError>
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
});
