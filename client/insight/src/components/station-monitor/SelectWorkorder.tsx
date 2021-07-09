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
import { Button } from "@material-ui/core";
import { List } from "@material-ui/core";
import { ListItem } from "@material-ui/core";
import { ListItemText } from "@material-ui/core";
import { ListItemIcon } from "@material-ui/core";
import { Dialog } from "@material-ui/core";
import { DialogActions } from "@material-ui/core";
import { DialogContent } from "@material-ui/core";
import { DialogTitle } from "@material-ui/core";
import CheckmarkIcon from "@material-ui/icons/Check";
import ShoppingBasketIcon from "@material-ui/icons/ShoppingBasket";
import { TextField } from "@material-ui/core";
import { useRecoilState, useRecoilValue, useSetRecoilState } from "recoil";

import * as matDetails from "../../data/material-details";
import { DisplayLoadingAndErrorCard } from "../ErrorsAndLoading";

function workorderComplete(w: matDetails.WorkorderPlanAndSummary): string {
  let completed = 0;
  if (w.summary) {
    completed = w.summary.completedQty;
  }
  return (
    "Due " + w.plan.dueDate.toDateString() + "; Completed " + completed.toString() + " of " + w.plan.quantity.toString()
  );
}

function WorkorderIcon({ work }: { work: matDetails.WorkorderPlanAndSummary }) {
  let completed = 0;
  if (work.summary) {
    completed = work.summary.completedQty;
  }
  if (work.plan.quantity <= completed) {
    return <CheckmarkIcon />;
  } else {
    return <ShoppingBasketIcon />;
  }
}

function ManualWorkorderEntry() {
  const [workorder, setWorkorder] = React.useState<string | null>(null);
  const mat = useRecoilValue(matDetails.materialDetail);
  const [assignWorkorder] = matDetails.useAssignWorkorder();
  const setWorkDialogOpen = useSetRecoilState(matDetails.loadWorkordersForMaterialInDialog);
  return (
    <TextField
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
  const mat = useRecoilValue(matDetails.materialDetail);
  const workorders = useRecoilValue(matDetails.possibleWorkordersForMaterialInDialog);
  const setWorkDialogOpen = useSetRecoilState(matDetails.loadWorkordersForMaterialInDialog);
  const [assignWorkorder] = matDetails.useAssignWorkorder();
  return (
    <List>
      {workorders.map((w) => (
        <ListItem
          key={w.plan.workorderId}
          button
          onClick={() => {
            if (mat) {
              assignWorkorder(mat, w.plan.workorderId);
            }
            setWorkDialogOpen(false);
          }}
        >
          <ListItemIcon>
            <WorkorderIcon work={w} />
          </ListItemIcon>
          <ListItemText primary={w.plan.workorderId} secondary={workorderComplete(w)} />
        </ListItem>
      ))}
    </List>
  );
}

export const SelectWorkorderDialog = React.memo(function SelectWorkorderDialog() {
  const [workDialogOpen, setWorkDialogOpen] = useRecoilState(matDetails.loadWorkordersForMaterialInDialog);
  let body: JSX.Element | undefined;

  if (workDialogOpen === false) {
    body = <p>None</p>;
  } else {
    body = (
      <>
        <DialogTitle>Select Workorder</DialogTitle>
        <DialogContent>
          <ManualWorkorderEntry />
          <DisplayLoadingAndErrorCard>
            <WorkorderList />
          </DisplayLoadingAndErrorCard>
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
