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
const {
  app,
  BrowserWindow,
  Menu,
  ipcMain,
  shell,
  dialog,
} = require("electron");

app.on("web-contents-created", (_, contents) => {
  contents.on("will-navigate", (event, url) => {
    if (url.startsWith("https://fms-insight.seedtactics.com")) {
      shell.openExternal(url);
    }
    event.preventDefault();
  });
  contents.on("new-window", (event, url) => {
    if (url.startsWith("https://fms-insight.seedtactics.com")) {
      shell.openExternal(url);
    }
    event.preventDefault();
  });
});

app.on("ready", () => {
  const background = new BrowserWindow({
    show: false,
    webPreferences: {
      nodeIntegration: true,
    },
  });
  background.loadFile("dist/background.html");
  //background.webContents.openDevTools();

  Menu.setApplicationMenu(null);

  const mainWindow = new BrowserWindow({
    height: 600,
    width: 800,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: false,
      preload: __dirname + "/preload.js",
    },
  });
  mainWindow.maximize();
  mainWindow.loadFile("dist/renderer.html");
  mainWindow.on("closed", () => {
    app.quit();
  });
  //mainWindow.webContents.openDevTools();

  ipcMain.on("to-background", (_, arg) => {
    background.webContents.send("background-request", arg);
  });
  ipcMain.on("background-response", (_, arg) => {
    mainWindow.webContents.send(
      "background-response-" + arg.id.toString(),
      arg
    );
  });
  ipcMain.on("open-file", (_, arg) => {
    dialog
      .showOpenDialog(mainWindow, {
        title: "Open Backup Database File",
        properties: ["openFile"],
      })
      .then((paths) => {
        if (!paths.canceled && paths.filePaths.length > 0) {
          background.webContents.send("open-file", paths.filePaths[0]);
          mainWindow.webContents.send("file-opened", paths.filePaths[0]);
        }
      });
  });
});
