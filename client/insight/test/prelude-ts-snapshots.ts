/* eslint-disable @typescript-eslint/no-unsafe-argument */
/* eslint-disable @typescript-eslint/no-unsafe-call */
/* eslint-disable @typescript-eslint/no-unsafe-member-access */
/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/no-unsafe-return */
import { Vector, HashMap, HashSet } from "prelude-ts";
import { List } from "list/methods";

export function ptsToJs(val: any): any {
  if (val instanceof Vector) {
    return val.toArray().map(ptsToJs);
  } else if (val instanceof List) {
    return val.toArray().map(ptsToJs);
  } else if (val instanceof HashMap) {
    return val.mapValues(ptsToJs).toJsMap((x) => (typeof x === "string" ? x : x.toString()));
  } else if (val instanceof HashSet) {
    return val.toArray().sort().map(ptsToJs);
  } else if (val instanceof Date) {
    return val;
  } else if (val instanceof Array) {
    return val.map(ptsToJs);
  } else if (val instanceof Map) {
    return new Map([...val].map(([k, v]) => [k, ptsToJs(v)]));
  } else if (typeof val === "object") {
    return Object.fromEntries(Object.entries(val).map(([k, v]) => [k, ptsToJs(v)]));
  } else {
    return val;
  }
}
