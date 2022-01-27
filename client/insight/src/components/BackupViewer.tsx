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
import { Button, CircularProgress, Stack } from "@mui/material";

import { useRequestOpenBackupFile } from "../network/backend-backupviewer";

export function BackupViewer(): JSX.Element {
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const requestOpenBackupFile = useRequestOpenBackupFile();

  function open() {
    setLoading(true);
    setError(null);
    requestOpenBackupFile()
      .catch(setError)
      .finally(() => setLoading(false));
  }

  return (
    <div style={{ textAlign: "center" }}>
      <h1 style={{ marginTop: "2em" }}>FMS Insight Backup Viewer</h1>
      <p style={{ marginTop: "2em", maxWidth: "50em", margin: "0 auto" }}>
        FMS Insight creates a database in the configured data directory (defaults to{" "}
        <code>c:\ProgramData\SeedTactics\FMSInsight</code>). The database should be periodically backed up and can then
        be opened directly by this program to view the data.
      </p>
      {error ? <p>{error}</p> : undefined}
      <Button disabled={loading} style={{ marginTop: "2em" }} variant="contained" color="primary" onClick={open}>
        <Stack direction="row" alignItems="center" spacing={2}>
          {loading ? <CircularProgress size={24} /> : undefined}
          Open File
        </Stack>
      </Button>
    </div>
  );
}
