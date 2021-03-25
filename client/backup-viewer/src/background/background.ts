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

import { Database, open } from "sqlite";
import { ipcRenderer, IpcRendererEvent } from "electron";
import * as sqlite3 from "sqlite3";
import { handlers, setDatabase } from "./handlers";

type Request = {
  name: string;
  id: number;
  payload: any;
};

type Response = {
  id: number;
  response?: any;
  error?: string;
};

ipcRenderer.once("communication-port", (evt) => {
  let port = evt.ports[0];
  port.onmessage = (msg) => {
    const req: Request = msg.data;
    const handler = handlers[req.name];
    if (handler) {
      Promise.resolve()
        .then(() => handler(req.payload))
        .then((r) => {
          const resp: Response = { id: req.id, response: r };
          port.postMessage(resp);
        })
        .catch((e) => {
          const resp: Response = { id: req.id, error: e.toString() };
          port.postMessage(resp);
        });
    } else {
      const resp: Response = { id: req.id, error: "No handler registered" };
      port.postMessage(resp);
    }
  };
});

ipcRenderer.on("open-file", (_: IpcRendererEvent, path: string) => {
  setDatabase(
    (async function () {
      let db: Database;
      try {
        db = await open({ filename: path, driver: sqlite3.Database });
      } catch {
        throw "The file is not a FMS Insight database";
      }
      let verRow: any;
      try {
        verRow = await db.get("SELECT ver from version");
      } catch {
        throw "The file is not a FMS Insight database.";
      }
      if (verRow.ver < 24) {
        throw "The FMS Insight database is from a newer version of FMS Insight.  Please update to the latest version.";
      }
      try {
        await db.get("SELECT Counter from stations");
      } catch {
        throw "The file is not a FMS Insight Log database.";
      }
      return db;
    })()
  );
});
