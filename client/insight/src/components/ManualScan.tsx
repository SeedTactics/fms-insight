/* Copyright (c) 2022, John Lenz

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
import { Dialog } from "@mui/material";
import { Button } from "@mui/material";
import { DialogActions } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { Search as SearchIcon } from "@mui/icons-material";
import { TextField } from "@mui/material";
import { useSetMaterialToShowInDialog } from "../cell-status/material-details.js";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";

export const ManualScanButton = React.memo(function ManualScan() {
  const [serial, setSerial] = React.useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = React.useState<boolean>(false);
  const setMatToShowDialog = useSetMaterialToShowInDialog();

  function close() {
    setDialogOpen(false);
    setSerial(null);
  }

  function open() {
    if (serial && serial !== "") {
      setMatToShowDialog({ type: "ManuallyEnteredSerial", serial });
      close();
    }
  }

  return (
    <>
      <Tooltip title="Enter Serial">
        <IconButton onClick={() => setDialogOpen(true)} size="large">
          <SearchIcon />
        </IconButton>
      </Tooltip>
      <Dialog open={dialogOpen} onClose={close} maxWidth="md">
        <DialogTitle>Enter a part&apos;s serial</DialogTitle>
        <DialogContent>
          <div style={{ minWidth: "20em" }}>
            <TextField
              sx={{ mt: "5px" }}
              label={serial === null || serial === "" ? "Serial" : "Serial (press enter)"}
              value={serial ?? ""}
              autoFocus
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
