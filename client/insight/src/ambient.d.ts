/* eslint-disable */

declare module "*.svg" {
  const empty = "";
  export default empty;
}

declare module "react-timeago" {
  import * as React from "react";

  export interface TimeAgoProps {
    readonly date: Date | string;
  }

  export default class TimeAgo extends React.Component<TimeAgoProps> {}
}
declare module "jdenticon" {
  // https://github.com/dmester/jdenticon/pull/52
  export function toSvg(hash: any, size: number, cfg?: any): string;
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
