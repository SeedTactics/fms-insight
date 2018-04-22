
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
import Button from 'material-ui/Button';
import List, { ListItem, ListItemText, ListItemIcon } from 'material-ui/List';
import Dialog, {
  DialogActions,
  DialogContent,
  DialogTitle,
} from 'material-ui/Dialog';
import CheckmarkIcon from '@material-ui/icons/Check';
import ShoppingBasketIcon from '@material-ui/icons/ShoppingBasket';

import { MaterialDetailTitle } from './Material';
import { Store, connect, mkAC, AppActionBeforeMiddleware } from '../../store/store';
import * as matDetails from '../../data/material-details';
import * as guiState from '../../data/gui-state';
import { CircularProgress } from 'material-ui/Progress';

function workorderComplete(w: matDetails.WorkorderPlanAndSummary): string {
  let completed = 0;
  if (w.summary) {
    completed = w.summary.completedQty;
  }
  return "Due " + w.plan.dueDate.toDateString() +
         "; Completed " + completed.toString() + " of " + w.plan.quantity.toString();
}

function WorkorderIcon({work}: {work: matDetails.WorkorderPlanAndSummary}) {
  let completed = 0;
  if (work.summary) {
    completed = work.summary.completedQty;
  }
  if (work.plan.quantity <= completed) {
    return <CheckmarkIcon/>;
  } else {
    return <ShoppingBasketIcon/>;
  }
}

export interface SelectWorkorderProps {
  readonly mats: matDetails.MaterialDetail | null;
  readonly onClose: () => void;
  readonly assignWorkorder: (data: matDetails.AssignWorkorderData) => void;
}

export function SelectWorkorderDialog(props: SelectWorkorderProps) {
  let body: JSX.Element | undefined;

  if (props.mats === null) {
    body = <p>None</p>;
  } else {
    const mat = props.mats;
    if (mat === null) {
      body = <p>None</p>;
    } else {
      const workList = (
        <List>
          {mat.workorders.map(w => (
            <ListItem
              key={w.plan.workorderId}
              button
              onClick={() => props.assignWorkorder({mat, workorder: w.plan.workorderId})}
            >
              <ListItemIcon>
                <WorkorderIcon work={w}/>
              </ListItemIcon>
              <ListItemText
                primary={w.plan.workorderId}
                secondary={workorderComplete(w)}
              />
            </ListItem>
          ))}
        </List>
      );

      body = (
        <>
          <DialogTitle disableTypography>
            <MaterialDetailTitle partName={mat.partName} serial={mat.serial}/>
          </DialogTitle>
          <DialogContent>
            {
              mat.loading_workorders ? <CircularProgress/> : workList
            }
          </DialogContent>
          <DialogActions>
            <Button onClick={props.onClose} color="primary">
              Close
            </Button>
          </DialogActions>
        </>
      );
    }
  }
  return (
    <Dialog
      open={props.mats !== null}
      onClose={props.onClose}
      maxWidth="md"
    >
      {body}
    </Dialog>
  );
}

export default connect(
  (st: Store) => ({
    mats: st.Gui.workorder_dialog_open ? st.MaterialDetails.material : null,
  }),
  {
    onClose: mkAC(guiState.ActionType.SetWorkorderDialogOpen),
    assignWorkorder: (data: matDetails.AssignWorkorderData) => [
      matDetails.assignWorkorder(data),
      { type: guiState.ActionType.SetWorkorderDialogOpen, open: false, }
    ] as AppActionBeforeMiddleware,
  }
)(SelectWorkorderDialog);