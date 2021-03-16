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
const {
  app,
  BrowserWindow,
  Menu,
  ipcMain,
  shell,
  dialog,
  MessageChannelMain,
} = require("electron");
const path = require("path");

app.on("web-contents-created", (_, contents) => {
  contents.on("will-navigate", (event, url) => {
    if (url.startsWith("https://fms-insight.seedtactics.com")) {
      shell.openExternal(url);
    }
    event.preventDefault();
  });
  contents.setWindowOpenHandler((details) => {
    if (details.url.startsWith("https://fms-insight.seedtactics.com")) {
      shell.openExternal(url);
    }
    return { action: "deny" };
  });
});

app.on("ready", () => {
  const background = new BrowserWindow({
    show: false,
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false,
    },
  });
  background.loadFile("build/background/background.html");

  Menu.setApplicationMenu(null);

  const mainWindow = new BrowserWindow({
    height: 600,
    width: 800,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: false,
      preload: path.join(app.getAppPath(), "src", "preload.js"),
    },
  });
  mainWindow.maximize();

  mainWindow.webContents.on("before-input-event", (e, i) => {
    if (i.type === "keyDown" && i.key === "F12") {
      mainWindow.webContents.openDevTools({ mode: "detach" });
      background.webContents.openDevTools({ mode: "detach" });
    }
    if (i.type === "keyDown" && i.key === "F5") {
      mainWindow.reload();
      background.reload();
    }
  });

  mainWindow.loadFile("build/app/renderer.html");

  mainWindow.on("closed", () => {
    app.quit();
  });

  const { port1, port2 } = new MessageChannelMain();

  mainWindow.webContents.postMessage("communication-port", null, [port1]);
  background.webContents.postMessage("communication-port", null, [port2]);

  ipcMain.on("open-insight-file", () => {
    dialog
      .showOpenDialog(mainWindow, {
        title: "Open Backup Database File",
        properties: ["openFile"],
      })
      .then((paths) => {
        if (!paths.canceled && paths.filePaths.length > 0) {
          background.webContents.send("open-file", paths.filePaths[0]);
          mainWindow.webContents.send("insight-file-opened");
        }
      });
  });
});
