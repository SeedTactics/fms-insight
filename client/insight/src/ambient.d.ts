/* eslint-disable */

declare module "*.svg" {
  const empty = "";
  export default empty;
}

declare module "highlight.js" {
  export function registerLanguage(lang: string, cfg: object): void;
  export function highlight(str: string, cfg: object): { value: string };
}

declare module "highlight.js/lib/core" {
  export function registerLanguage(lang: string, cfg: object): void;
  export function highlight(str: string, cfg: object): { value: string };
}

declare module "highlight.js/lib/languages/gcode" {
  const gcode: object;
  export default gcode;
}
