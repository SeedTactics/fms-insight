/* Copyright (c) 2023, John Lenz

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

import { Button } from "@mui/material";
import { useState } from "react";
import { FmsServerBackend } from "../network/backend.js";

export function VerboseLoggingPage() {
  const [enabled, setEnabled] = useState(false);

  function enable() {
    setEnabled(true);
    void FmsServerBackend.enableVerboseLoggingForFiveMinutes();
    setTimeout(() => setEnabled(false), 5 * 60 * 1000);
  }

  return (
    <main style={{ marginTop: "4em" }}>
      <div style={{ maxWidth: "50em", marginLeft: "auto", marginRight: "auto", textAlign: "center" }}>
        <p
          style={{
            marginBottom: "1em",
          }}
        >
          Verbose logging generates a large amount of additional data in the FMS Insight Server debug log
          file.
        </p>
        <Button disabled={enabled} onClick={enable} variant="contained">
          {enabled ? (
            <span>Verbose Logging is Enabled</span>
          ) : (
            <span>Enable Verbose Logging For 5 minutes</span>
          )}
        </Button>
      </div>
    </main>
  );
}
