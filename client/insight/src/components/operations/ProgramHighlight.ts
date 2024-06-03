/* eslint-disable @typescript-eslint/no-unsafe-argument */
/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/no-unsafe-call */
import hljs from "highlight.js/lib/core";
import gcode from "highlight.js/lib/languages/gcode";
hljs.registerLanguage("gcode", gcode);

self.onmessage = function (event: MessageEvent) {
  (self.postMessage as any)(hljs.highlight(event.data, { language: "gcode" }).value);
};
