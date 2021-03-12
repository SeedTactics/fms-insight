let e = require("electron");
window.electronIpc = e.ipcRenderer;
window.bmsVersion = e.app ? e.app.getVersion() : "1.2.3";
