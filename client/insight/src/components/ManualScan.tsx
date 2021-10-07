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
import { Dialog } from "@material-ui/core";
import { Button } from "@material-ui/core";
import { DialogActions } from "@material-ui/core";
import { DialogContent } from "@material-ui/core";
import { DialogTitle } from "@material-ui/core";
import SearchIcon from "@material-ui/icons/Search";
import { TextField } from "@material-ui/core";
import { materialToShowInDialog } from "../cell-status/material-details";
import { useSetRecoilState } from "recoil";
import { Tooltip } from "@material-ui/core";
import { IconButton } from "@material-ui/core";

export const ManualScanButton = React.memo(function ManualScan() {
  const [serial, setSerial] = React.useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = React.useState<boolean>(false);
  const setMatToShowDialog = useSetRecoilState(materialToShowInDialog);

  function open() {
    if (serial && serial !== "") {
      setMatToShowDialog({ type: "Serial", serial });
      setDialogOpen(false);
      setSerial(null);
    }
  }

  function close() {
    setDialogOpen(false);
    setSerial(null);
  }

  return (
    <>
      <Tooltip title="Enter Serial">
        <IconButton onClick={() => setDialogOpen(true)}>
          <SearchIcon />
        </IconButton>
      </Tooltip>
      <Dialog open={dialogOpen} onClose={close} maxWidth="md">
        <DialogTitle>Enter a part&apos;s serial</DialogTitle>
        <DialogContent>
          <div style={{ minWidth: "20em" }}>
            <TextField
              label={serial === null || serial === "" ? "Serial" : "Serial (press enter)"}
              value={serial ?? ""}
              onChange={(e) => setSerial(e.target.value)}
              onKeyPress={(e) => {
                if (e.key === "Enter" && serial && serial !== "") {
                  e.preventDefault();
                  open();
                }
              }}
            />
          </div>
        </DialogContent>
        <DialogActions>
          <Button onClick={open} disabled={serial === null || serial === ""} color="secondary">
            Open
          </Button>
          <Button onClick={close} color="secondary">
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
});
