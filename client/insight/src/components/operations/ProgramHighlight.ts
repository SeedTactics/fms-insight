/// <reference lib="webworker" />
import hljs from "highlight.js/lib/core";
import gcode from "highlight.js/lib/languages/gcode";
hljs.registerLanguage("gcode", gcode);

self.addEventListener("message", (event: MessageEvent<string>) => {
  self.postMessage(hljs.highlight(event.data, { language: "gcode" }).value, []);
});
