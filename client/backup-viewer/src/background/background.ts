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

import Database from "better-sqlite3";
import { ipcRenderer } from "electron";
import { handlers } from "./handlers";

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

let db: Database.Database | undefined = undefined;
let error: string | undefined = undefined;
let port: MessagePort | undefined = undefined;

async function openFile(): Promise<boolean> {
  const path = await ipcRenderer.invoke("open-insight-file");
  if (path) {
    try {
      db = new Database(path, { readonly: true, fileMustExist: true });
    } catch (e) {
      console.log(e);
      error = "The file is not a FMS Insight database";
      throw error;
    }
    let verRow: any;
    try {
      verRow = db.prepare("SELECT ver from version").get();
    } catch (e) {
      console.log(e);
      error = "The file is not a FMS Insight database";
      throw error;
    }
    if (verRow.ver < 24) {
      error =
        "The FMS Insight database is from a newer version of FMS Insight.  Please update to the latest version.";
      throw error;
    }
    try {
      db.prepare("SELECT Counter from stations").get();
    } catch (e) {
      console.log(e);
      error = "The file is not a FMS Insight Log database.";
      throw error;
    }
    return true;
  } else {
    return false;
  }
}

ipcRenderer.once("communication-port", (evt) => {
  port = evt.ports[0];
  const portC = port;

  port.onmessage = (msg) => {
    const req: Request = msg.data;

    if (req.name === "open-insight-file") {
      openFile()
        .then((opened) => portC.postMessage({ id: req.id, response: opened }))
        .catch((e) => portC.postMessage({ id: req.id, error: e.toString() }));
    } else {
      const handler = handlers[req.name];
      if (error !== undefined) {
        const resp: Response = { id: req.id, error };
        portC.postMessage(resp);
      }

      if (db && handler) {
        try {
          const r = handler(db, req.payload);
          const resp: Response = { id: req.id, response: r };
          portC.postMessage(resp);
        } catch (e: any) {
          const resp: Response = { id: req.id, error: e.toString() };
          portC.postMessage(resp);
        }
      } else {
        const resp: Response = { id: req.id, error: "No handler registered" };
        portC.postMessage(resp);
      }
    }
  };
});
