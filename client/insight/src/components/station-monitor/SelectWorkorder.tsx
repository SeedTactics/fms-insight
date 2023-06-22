/* Copyright (c) 2021, John Lenz

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
import { Button, ListItemButton } from "@mui/material";
import { List } from "@mui/material";
import { ListItem } from "@mui/material";
import { ListItemText } from "@mui/material";
import { ListItemIcon } from "@mui/material";
import { Dialog } from "@mui/material";
import { DialogActions } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { TextField } from "@mui/material";

import { Check as CheckmarkIcon, ShoppingBasket as ShoppingBasketIcon } from "@mui/icons-material";

import * as matDetails from "../../cell-status/material-details.js";
import { DisplayLoadingAndError } from "../ErrorsAndLoading.js";
import { IActiveWorkorder } from "../../network/api.js";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";

export const selectWorkorderDialogOpen = atom<boolean>(false);

function workorderComplete(w: IActiveWorkorder) {
  let comment = "";
  if (w.comments && w.comments.length > 0) {
    comment = "; " + w.comments[w.comments.length - 1].comment;
  }
  return `Due ${w.dueDate.toLocaleDateString()}; Completed ${w.completedQuantity} of ${
    w.plannedQuantity
  }${comment}`;
}

function WorkorderIcon({ work }: { work: IActiveWorkorder }) {
  if (work.plannedQuantity <= work.completedQuantity) {
    return <CheckmarkIcon />;
  } else {
    return <ShoppingBasketIcon />;
  }
}

function ManualWorkorderEntry() {
  const [workorder, setWorkorder] = React.useState<string | null>(null);
  const mat = useAtomValue(matDetails.materialInDialogInfo);
  const [assignWorkorder] = matDetails.useAssignWorkorder();
  const setWorkDialogOpen = useSetAtom(selectWorkorderDialogOpen);
  return (
    <TextField
      sx={{ mt: "5px" }}
      label={workorder === null || workorder === "" ? "Workorder" : "Workorder (press enter)"}
      value={workorder ?? ""}
      onChange={(e) => setWorkorder(e.target.value)}
      onKeyPress={(e) => {
        if (e.key === "Enter" && mat && workorder && workorder !== "") {
          e.preventDefault();
          assignWorkorder(mat, workorder);
          setWorkDialogOpen(false);
        }
      }}
    />
  );
}

function WorkorderList() {
  const mat = useAtomValue(matDetails.materialInDialogInfo);
  const workorders = useAtomValue(matDetails.possibleWorkordersForMaterialInDialog);
  const setWorkDialogOpen = useSetAtom(selectWorkorderDialogOpen);
  const [assignWorkorder] = matDetails.useAssignWorkorder();
  return (
    <List>
      {workorders.map((w) => (
        <ListItem key={w.workorderId}>
          <ListItemButton
            onClick={() => {
              if (mat) {
                assignWorkorder(mat, w.workorderId);
              }
              setWorkDialogOpen(false);
            }}
          >
            <ListItemIcon>
              <WorkorderIcon work={w} />
            </ListItemIcon>
            <ListItemText primary={w.workorderId} secondary={workorderComplete(w)} />
          </ListItemButton>
        </ListItem>
      ))}
    </List>
  );
}

export const SelectWorkorderDialog = React.memo(function SelectWorkorderDialog() {
  const [workDialogOpen, setWorkDialogOpen] = useAtom(selectWorkorderDialogOpen);

  let body: JSX.Element | undefined;
  if (workDialogOpen === false) {
    body = <p>None</p>;
  } else {
    body = (
      <>
        <DialogTitle>Select Workorder</DialogTitle>
        <DialogContent>
          <DisplayLoadingAndError>
            <ManualWorkorderEntry />
            <WorkorderList />
          </DisplayLoadingAndError>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setWorkDialogOpen(false)} color="primary">
            Cancel
          </Button>
        </DialogActions>
      </>
    );
  }
  return (
    <Dialog open={workDialogOpen} onClose={() => setWorkDialogOpen(false)} maxWidth="md">
      {body}
    </Dialog>
  );
});
