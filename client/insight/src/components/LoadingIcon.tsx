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
import { Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, IconButton } from "@mui/material";
import ErrorIcon from "@mui/icons-material/Error";

import { Tooltip } from "@mui/material";
import { errorLoadingLast30, websocketReconnecting } from "../network/websocket";
import { useRecoilValue } from "recoil";
import { errorLoadingBackupViewer, loadingBackupViewer } from "../network/backend-backupviewer";
import { errorLoadingSpecificMonthData, loadingSpecificMonthData } from "../network/load-specific-month";

export const LoadingIcon = React.memo(function LoadingIcon() {
  const websocketLoading = useRecoilValue(websocketReconnecting);
  const backupLoading = useRecoilValue(loadingBackupViewer);
  const specificMonthLoading = useRecoilValue(loadingSpecificMonthData);

  const last30Error = useRecoilValue(errorLoadingLast30);
  const backupViewerError = useRecoilValue(errorLoadingBackupViewer);
  const specificMonthError = useRecoilValue(errorLoadingSpecificMonthData);

  const [dialogOpen, setDialogOpen] = React.useState(false);

  return (
    <>
      {last30Error != null || backupViewerError != null || specificMonthError != null ? (
        <Tooltip title="Error">
          <IconButton onClick={() => setDialogOpen(true)} size="large">
            <ErrorIcon />
          </IconButton>
        </Tooltip>
      ) : undefined}
      {websocketLoading || backupLoading || specificMonthLoading ? (
        <Tooltip title="Loading">
          <CircularProgress data-testid="loading-icon" color="secondary" />
        </Tooltip>
      ) : undefined}
      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)}>
        <DialogTitle>Error</DialogTitle>
        <DialogContent>
          {last30Error !== null ? <p>{last30Error}</p> : undefined}
          {backupViewerError !== null ? <p>{backupViewerError}</p> : undefined}
          {specificMonthError !== null ? <p>{specificMonthError}</p> : undefined}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDialogOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </>
  );
});
