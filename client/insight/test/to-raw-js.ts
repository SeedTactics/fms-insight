/* eslint-disable @typescript-eslint/no-unsafe-argument */
/* eslint-disable @typescript-eslint/no-unsafe-call */
/* eslint-disable @typescript-eslint/no-unsafe-member-access */
/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/no-unsafe-return */

import { HashSet } from "../src/util/iset";
import { LazySeq } from "../src/util/lazyseq";

export function toRawJs(val: any): any {
  if (val instanceof Date) {
    return val;
  } else if (val instanceof Array) {
    return val.map(toRawJs);
  } else if (val instanceof Map) {
    return new Map([...val].map(([k, v]) => [k, toRawJs(v)]));
  } else if (val instanceof Set) {
    return val;
  } else if (val instanceof HashSet) {
    return LazySeq.ofIterable(val).map(toRawJs).toRArray();
  } else if (typeof val === "object" && typeof val.toLazySeq === "function") {
    // should be instanceof IMap
    // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
    const lseq: LazySeq<any> = val.toLazySeq();
    return lseq.toRMap(([k, v]) => [k.toString(), toRawJs(v)]);
  } else if (typeof val === "object") {
    return Object.fromEntries(Object.entries(val).map(([k, v]) => [k, toRawJs(v)]));
  } else {
    return val;
  }
}
