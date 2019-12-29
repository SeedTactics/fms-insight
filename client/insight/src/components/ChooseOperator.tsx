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
import Select from "@material-ui/core/Select";
import MenuItem from "@material-ui/core/MenuItem";
import ListItemText from "@material-ui/core/ListItemText";
import ListItemSecondaryAction from "@material-ui/core/ListItemSecondaryAction";
import IconButton from "@material-ui/core/IconButton";
import DeleteIcon from "@material-ui/icons/Delete";
import Dialog from "@material-ui/core/Dialog";
import DialogTitle from "@material-ui/core/DialogTitle";
import TextField from "@material-ui/core/TextField";
import DialogContent from "@material-ui/core/DialogContent";
import DialogActions from "@material-ui/core/DialogActions";
import Button from "@material-ui/core/Button";
import { User } from "oidc-client";
import { HashSet } from "prelude-ts";

import { connect, DispatchAction, mkAC } from "../store/store";
import * as operators from "../data/operators";

interface OperatorSelectProps {
  readonly currentUser: User | null;
  readonly operators: HashSet<string>;
  readonly currentOperator: string | null;
  readonly setOperator: DispatchAction<operators.ActionType.SetOperator>;
  readonly removeOperator: DispatchAction<operators.ActionType.RemoveOperator>;
}

const NewOper = "__FMS_INSIGHT_NEW_OPERATOR__" as const;

export const OperatorSelect = React.memo(function OperatorSelectF(props: OperatorSelectProps) {
  const [newOperOpen, setNewOperOpen] = React.useState(false);
  const [newOperName, setNewOperName] = React.useState("");

  function changeOper(evt: React.ChangeEvent<{ value: unknown }>) {
    if (evt.target.value === NewOper) {
      setNewOperOpen(true);
    } else {
      props.setOperator({ operator: evt.target.value });
    }
  }

  if (props.currentUser) {
    return <div>{props.currentUser.profile.name || props.currentUser.profile.sub}</div>;
  }
  return (
    <>
      {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
      <Select value={props.currentOperator} onChange={changeOper} renderValue={((x: any) => x) as any}>
        {props.operators.toArray({ sortOn: x => x }).map((oper, idx) => (
          <MenuItem key={idx} value={oper}>
            <ListItemText primary={oper} />
            <ListItemSecondaryAction>
              <IconButton edge="end" onClick={() => props.removeOperator({ operator: oper })}>
                <DeleteIcon />
              </IconButton>
            </ListItemSecondaryAction>
          </MenuItem>
        ))}
        <MenuItem key="new" value={NewOper}>
          <em>Create New Operator</em>
        </MenuItem>
      </Select>
      <Dialog
        open={newOperOpen}
        onClose={() => {
          setNewOperOpen(false);
          setNewOperName("");
        }}
      >
        <DialogTitle>Create New Operator</DialogTitle>
        <DialogContent>
          <TextField
            value={newOperName}
            onChange={evt => setNewOperName(evt.target.value)}
            label="New Name"
            variant="outlined"
            autoFocus
            onKeyUp={evt => {
              if (evt.keyCode === 13) {
                props.setOperator({ operator: newOperName });
                setNewOperName("");
                setNewOperOpen(false);
              }
            }}
          />
        </DialogContent>
        <DialogActions>
          <Button
            disabled={newOperName === ""}
            onClick={() => {
              props.setOperator({ operator: newOperName });
              setNewOperName("");
              setNewOperOpen(false);
            }}
          >
            Create {newOperName}
          </Button>
          <Button
            onClick={() => {
              setNewOperName("");
              setNewOperOpen(false);
            }}
          >
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
});

export default connect(
  st => ({
    operators: st.Operators.operators,
    currentOperator: st.Operators.current || null,
    currentUser: st.ServerSettings.user || null
  }),
  {
    setOperator: mkAC(operators.ActionType.SetOperator),
    removeOperator: mkAC(operators.ActionType.RemoveOperator)
  }
)(OperatorSelect);
