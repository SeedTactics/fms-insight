let e = require("electron");
window.electronIpc = e.ipcRenderer;
window.bmsVersion = e.app.getVersion();
