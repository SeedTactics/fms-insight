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

import { initStore } from "../../../insight/src/store/store";
import { registerBackend } from "../../../insight/src/data/backend";
import { render } from "../../../insight/src/renderer";
import { loadLast30Days } from "../../../insight/src/data/events";
import { LogBackend, JobsBackend, ServerBackend } from "./store-backend";
import { AppProps } from "../../../insight/src/components/App";
import * as routes from "../../../insight/src/data/routes";

window.history.pushState(null, "", routes.RouteLocation.Backup_InitialOpen);
registerBackend(LogBackend, JobsBackend, ServerBackend);

const store = initStore();

const props: AppProps = {
  demo: false,
  backupViewerOnRequestOpenFile: () => {
    window.electronIpc.send("open-file");
  },
};

window.electronIpc.on("file-opened", () => {
  window.history.pushState(null, "", routes.RouteLocation.Backup_Efficiency);
  store.dispatch(loadLast30Days());
});

render(props, store, document.getElementById("root"));
