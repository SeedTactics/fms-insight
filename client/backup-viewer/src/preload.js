let e = require("electron");
window.bmsVersion = e.app ? e.app.getVersion() : "1.2.3";

window.addEventListener("message", (event) => {
  if (event.source === window && event.data === "open-insight-file") {
    e.ipcRenderer.send("open-insight-file");
  }
});

e.ipcRenderer.on("insight-file-opened", (evt) => {
  window.postMessage("insight-file-opened", "*", evt.ports);
});
