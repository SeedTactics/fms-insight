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

import { memo, useState } from "react";
import { Select, SelectChangeEvent } from "@mui/material";
import { MenuItem } from "@mui/material";
import { ListItemText } from "@mui/material";
import { ListItemSecondaryAction } from "@mui/material";
import { IconButton } from "@mui/material";
import { Delete as DeleteIcon } from "@mui/icons-material";
import { Dialog } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { TextField } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogActions } from "@mui/material";
import { Button } from "@mui/material";

import { allOperators, currentOperator } from "../data/operators.js";
import { fmsInformation } from "../network/server-settings.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { useAtom, useAtomValue } from "jotai";

const NewOper = "__FMS_INSIGHT_NEW_OPERATOR__";

export const OperatorSelect = memo(function OperatorSelectF() {
  const fmsInfo = useAtomValue(fmsInformation);
  const [operator, setOperator] = useAtom(currentOperator);
  const [allOpers, setAllOpers] = useAtom(allOperators);

  const [newOperOpen, setNewOperOpen] = useState(false);
  const [newOperName, setNewOperName] = useState("");

  function changeOper(evt: SelectChangeEvent<string>) {
    if (evt.target.value === NewOper) {
      setNewOperOpen(true);
    } else {
      setOperator(evt.target.value);
    }
  }

  function removeOperator(oper: string) {
    setAllOpers((s) => s.filter((o) => o !== oper));
  }

  function addOperator() {
    if (newOperName !== "") {
      setAllOpers((s) => [...s.filter((o) => o !== newOperName), newOperName]);
      setOperator(newOperName);
      setNewOperName("");
      setNewOperOpen(false);
    }
  }

  if (fmsInfo.user) {
    return <div>{fmsInfo.user.profile.name || fmsInfo.user.profile.sub}</div>;
  }
  return (
    <>
      <Select value={operator || ""} onChange={changeOper} variant="standard" renderValue={(x) => x}>
        {LazySeq.of(allOpers)
          .sortBy((x) => x)
          .map((oper, idx) => (
            <MenuItem key={idx} value={oper}>
              <ListItemText primary={oper} />
              <ListItemSecondaryAction>
                <IconButton edge="end" onClick={() => removeOperator(oper)} size="large">
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
            onChange={(evt) => setNewOperName(evt.target.value)}
            label="New Name"
            variant="outlined"
            autoFocus
            sx={{ mt: "0.5em" }}
            onKeyUp={(evt) => {
              if (evt.keyCode === 13) {
                addOperator();
              }
            }}
          />
        </DialogContent>
        <DialogActions>
          <Button disabled={newOperName === ""} onClick={addOperator}>
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
