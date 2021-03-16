let e = require("electron");
window.bmsVersion = e.app ? e.app.getVersion() : "1.2.3";

let port = null;

e.ipcRenderer.on("communication-port", (evt) => {
  port = evt.ports[0];

  port.onmessage = (evt) => {
    window.postMessage(
      {
        direction: "message-response-from-background",
        ipcMessage: evt.data,
      },
      "*"
    );
  };
});

window.addEventListener("message", (event) => {
  if (
    event.data &&
    typeof event.data === "object" &&
    event.data.direction === "message-from-render-to-background" &&
    event.data.ipcMessage
  ) {
    port.postMessage(event.data.ipcMessage);
  } else if (event.data === "open-insight-file") {
    e.ipcRenderer.send("open-insight-file");
  }
});

e.ipcRenderer.on("insight-file-opened", () => {
  window.postMessage("insight-file-opened", "*");
});
