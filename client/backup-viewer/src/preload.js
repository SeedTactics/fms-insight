let e = require("electron");

e.ipcRenderer.once("insight-background-communication-port", (evt) => {
  window.postMessage("insight-background-communication-port", "*", evt.ports);
});
