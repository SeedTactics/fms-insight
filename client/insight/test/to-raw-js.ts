/* oxlint-disable typescript/no-unsafe-argument */
/* oxlint-disable typescript/no-unsafe-call */
/* oxlint-disable typescript/no-unsafe-member-access */
/* oxlint-disable typescript/no-explicit-any */
/* oxlint-disable typescript/no-unsafe-return */

import {
  HashSet,
  HashMap,
  OrderedMap,
  LazySeq,
  OrderedSet,
} from "@seedtactics/immutable-collections";

export function toRawJs(val: any): any {
  if (val instanceof Date) {
    return val;
  } else if (Array.isArray(val)) {
    return val.map(toRawJs);
  } else if (val instanceof Map) {
    return new Map([...val].map(([k, v]) => [k, toRawJs(v)]));
  } else if (val instanceof Set) {
    return val;
  } else if (val instanceof HashSet) {
    return LazySeq.of(val).map(toRawJs).toRArray();
  } else if (val instanceof HashMap) {
    return val.toLazySeq().toRMap(([k, v]) => [k.toString(), toRawJs(v)]);
  } else if (val instanceof OrderedMap) {
    return val.toAscLazySeq().toRMap(([k, v]) => [k.toString(), toRawJs(v)]);
  } else if (val instanceof OrderedSet) {
    return val.toAscLazySeq().map(toRawJs).toRArray();
  } else if (val === undefined || val === null) {
    return val;
  } else if (typeof val === "object") {
    return Object.fromEntries(Object.entries(val).map(([k, v]) => [k, toRawJs(v)]));
  } else {
    return val;
  }
}
