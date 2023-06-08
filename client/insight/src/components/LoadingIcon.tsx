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
import {
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
} from "@mui/material";
import { Error as ErrorIcon } from "@mui/icons-material";

import { Tooltip } from "@mui/material";
import { errorLoadingLast30, websocketReconnecting } from "../network/websocket.js";
import { errorLoadingSpecificMonthData, loadingSpecificMonthData } from "../network/load-specific-month.js";
import { useAtomValue } from "jotai";

export const LoadingIcon = React.memo(function LoadingIcon() {
  const websocketLoading = useAtomValue(websocketReconnecting);
  const specificMonthLoading = useAtomValue(loadingSpecificMonthData);

  const last30Error = useAtomValue(errorLoadingLast30);
  const specificMonthError = useAtomValue(errorLoadingSpecificMonthData);

  const [dialogOpen, setDialogOpen] = React.useState(false);

  return (
    <>
      {last30Error != null || specificMonthError != null ? (
        <Tooltip title="Error">
          <IconButton onClick={() => setDialogOpen(true)} size="large">
            <ErrorIcon />
          </IconButton>
        </Tooltip>
      ) : undefined}
      {websocketLoading || specificMonthLoading ? (
        <Tooltip title="Loading">
          <CircularProgress data-testid="loading-icon" color="secondary" />
        </Tooltip>
      ) : undefined}
      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)}>
        <DialogTitle>Error</DialogTitle>
        <DialogContent>
          {last30Error !== null ? <p>{last30Error}</p> : undefined}
          {specificMonthError !== null ? <p>{specificMonthError}</p> : undefined}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDialogOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </>
  );
});
