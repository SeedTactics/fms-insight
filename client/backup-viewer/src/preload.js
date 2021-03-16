let e = require("electron");
window.bmsVersion = e.app ? e.app.getVersion() : "1.2.3";

const windowLoaded = new Promise((resolve) => {
  window.onload = resolve;
});

e.ipcRenderer.once("communication-port", async (evt) => {
  await windowLoaded;
  window.postMessage("background-port", "*", evt.ports);
});

window.addEventListener("message", (event) => {
  if (event.data === "open-insight-file") {
    e.ipcRenderer.send("open-insight-file");
  }
});

e.ipcRenderer.on("insight-file-opened", () => {
  window.postMessage("insight-file-opened", "*");
});
