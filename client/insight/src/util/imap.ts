import { LazySeq } from "./lazyseq";
import hamt from "hamt_plus";

export type HashKeyObj = {
  equals(other: unknown): boolean;
  hashPrimitives(): ReadonlyArray<HashKey | Date | null | undefined>;
};

export type HashKey = string | number | boolean | HashKeyObj;

export interface HashMap<K, V> {
  get(k: K): V | undefined;
  has(k: K): boolean;
  size: number;

  [Symbol.iterator](): Iterator<readonly [K, V]>;
  fold<T>(f: (acc: T, val: V, key: K) => T, zero: T): T;
  entries(): Iterator<readonly [K, V]>;
  keys(): Iterator<K>;
  values(): Iterator<V>;
  forEach(f: (val: V, k: K, map: HashMap<K, V>) => void): void;
  toLazySeq(): LazySeq<readonly [K, V]>;
  keysToLazySeq(): LazySeq<K>;
  valuesToLazySeq(): LazySeq<V>; // TODO: when converting to immutable-collections, switch most to just values

  set(k: K & HashKey, v: V): HashMap<K, V>;
  modify(k: K & HashKey, f: (v: V | undefined) => V): HashMap<K, V>;
  delete(k: K & HashKey): HashMap<K, V>;
  union(other: HashMap<K, V>, merge?: (v1: V, v2: V) => V): HashMap<K, V>;

  filter(f: (v: V, k: K) => boolean): HashMap<K, V>;
  mapValues<U>(f: (v: V, k: K) => U): HashMap<K, U>;
  collectValues(f: (v: V, k: K) => V | null | undefined): HashMap<K, V>;
}

interface MakeConfig<K> {
  keyEq: (a: K, b: K) => boolean;
  hash: (v: K) => number;
}

interface HamtMap<K, V> extends HashMap<K, V> {
  beginMutation(): HamtMap<K, V>;
  endMutation(): HamtMap<K, V>;

  _config: MakeConfig<K>;
}

function isHashKeyObj(k: unknown): k is HashKeyObj {
  return k !== null && typeof k === "object" && "hashPrimitives" in k && "equals" in k;
}

function primEq(a: unknown, b: unknown): boolean {
  return a === b;
}

// hamt_plus uses the hash value in javascript bit operations, and javascript bit operations cast
// the number to a signed 32-bit integer.  Thus each hash function should return a number which
// is within the range of a signed 32-bit integer.

function hash2Ints(h1: number, h2: number): number {
  // combines two 32-bit hashes into a 32-bit hash
  return (h1 * 16777619) ^ h2;
}

function stringHash(str: string): number {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = hash2Ints(hash, str.charCodeAt(i));
  }
  return hash2Ints(hash, str.length);
}

function boolHash(a: boolean): number {
  return a ? 1 : 0;
}

function numHash(a: number): number {
  // numbers are in general a IEEE double
  if (Number.isInteger(a) && a >= -2_147_483_648 && a <= 2_147_483_647) {
    return a; // hash is just the number itself since it is a 32-bit signed integer
  } else if (Object.is(a, Infinity) || Object.is(a, NaN)) {
    return 0;
  } else {
    // convert the number to a 64-bit array and combine the two 32-bit halves
    const buff = new ArrayBuffer(8);
    new Float64Array(buff)[0] = a;
    const intarr = new Int32Array(buff);
    return hash2Ints(intarr[0], intarr[1]);
  }
}

function objHash(a: HashKeyObj): number {
  const prims = a.hashPrimitives();
  let hash = 0;
  for (let i = 0; i < prims.length; i++) {
    const p = prims[i];
    if (p === null || p === undefined) {
      hash = hash2Ints(hash, 0);
    } else {
      switch (typeof p) {
        case "string":
          hash = hash2Ints(hash, stringHash(p));
          break;
        case "number":
          hash = hash2Ints(hash, numHash(p));
          break;
        case "boolean":
          hash = hash2Ints(hash, boolHash(p));
          break;
        default:
          if (p instanceof Date) {
            hash = hash2Ints(hash, p.getTime());
          } else if (isHashKeyObj(p)) {
            hash = hash2Ints(hash, objHash(p));
          } else {
            // typescript should prevent this from happening
            hash = hash2Ints(hash, stringHash((p as unknown as object).toString()));
          }
          break;
      }
    }
  }
  return hash2Ints(hash, prims.length);
}

function makeWithDynamicConfig<K, V>(): HamtMap<K, V> {
  // eslint-disable-next-line prefer-const
  let m: HamtMap<K, V>;

  // we have a small hack here.  At the time of creation, we don't know the
  // key type of the map.  We only know the type the first time a key/value is
  // inserted.  Therefore, for this initial empty map, we use a map configuration
  // for the keyEq and hash function which check the type of the key and then
  // replace the configuration with the correct one.  Technically the _config
  // property is an internal property but this works and we are only changing the _config
  // property once on the empty map and then never again.

  function updateMapConfig(k: K): void {
    switch (typeof k) {
      case "object":
        if (isHashKeyObj(k)) {
          // the key types passed to _config.keyEq and _config.hash are equal
          // to the type K which we just narrowed, but typescript doesn't know
          // about the narrowing when typing keyEq and hash
          m._config = {
            keyEq: (j1, j2) => (j1 as unknown as HashKeyObj).equals(j2),
            hash: objHash as unknown as (k: K) => number,
          };
          return;
        } else {
          throw new Error("key type must have equals and hash methods");
        }

      case "string":
        m._config = {
          keyEq: primEq,
          // we just narrowed K to string, but typescript forgets this when
          // typing hash
          hash: stringHash as unknown as (k: K) => number,
        };
        return;

      case "boolean":
        m._config = {
          keyEq: primEq,
          hash: boolHash as unknown as (k: K) => number,
        };
        return;

      case "number":
        m._config = {
          keyEq: primEq,
          hash: numHash as unknown as (k: K) => number,
        };
        return;
    }
  }

  function firstKeyEq(k1: K, k2: K): boolean {
    updateMapConfig(k1);
    return m._config.keyEq(k1, k2);
  }
  function firstHash(k: K): number {
    updateMapConfig(k);
    return m._config.hash(k);
  }

  // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-member-access, @typescript-eslint/no-unsafe-call
  m = hamt.make({
    keyEq: firstKeyEq,
    hash: firstHash,
  });
  return m;
}

export function emptyIMap<K, V>(): HashMap<K, V> {
  return makeWithDynamicConfig<K, V>();
}

export function iterableToIMap<K, V>(
  items: Iterable<readonly [K & HashKey, V]>,
  merge?: (v1: V, v2: V) => V
): HashMap<K, V> {
  const m = makeWithDynamicConfig<K, V>().beginMutation();
  if (merge !== undefined) {
    for (const [k, v] of items) {
      m.modify(k, (old) => (old === undefined ? v : merge(old, v)));
    }
  } else {
    for (const [k, v] of items) {
      m.set(k, v);
    }
  }
  return m.endMutation();
}

export function buildIMap<K, V, T>(
  items: Iterable<T>,
  getKey: (t: T) => K & HashKey,
  getVal: (old: V | undefined, t: T) => V
): HashMap<K, V> {
  const m = makeWithDynamicConfig<K, V>().beginMutation();
  for (const t of items) {
    m.modify(getKey(t), (old) => getVal(old, t));
  }
  return m.endMutation();
}

// --------------------------------------------------------------------------------
// Extra functions placed onto the IMap prototype
// --------------------------------------------------------------------------------

function imapToLazySeq<K, V>(this: HashMap<K, V>): LazySeq<readonly [K, V]> {
  return LazySeq.ofIterable(this);
}
function imapToKeysLazySeq<K, V>(this: HashMap<K, V>): LazySeq<K> {
  // eslint-disable-next-line @typescript-eslint/no-this-alias
  const m = this;
  return LazySeq.ofIterable({
    [Symbol.iterator]() {
      return m.keys();
    },
  });
}
function imapToValuesLazySeq<K, V>(this: HashMap<K, V>): LazySeq<V> {
  // eslint-disable-next-line @typescript-eslint/no-this-alias
  const m = this;
  return LazySeq.ofIterable({
    [Symbol.iterator]() {
      return m.values();
    },
  });
}

function bulkDeleteIMap<K, V>(this: HashMap<K & HashKey, V>, shouldRemove: (k: K, v: V) => boolean): HashMap<K, V> {
  // eslint-disable-next-line @typescript-eslint/no-this-alias
  let m = this;
  for (const [k, v] of this) {
    if (shouldRemove(k, v)) {
      m = m.delete(k);
    }
  }
  return m;
}

function filter<K, V>(this: HashMap<K & HashKey, V>, f: (k: V, v: K) => boolean): HashMap<K, V> {
  // eslint-disable-next-line @typescript-eslint/no-this-alias
  let m = this;
  for (const [k, v] of this) {
    if (!f(v, k)) {
      m = m.delete(k);
    }
  }
  return m;
}

function mapValuesIMap<K, V>(this: HashMap<K & HashKey, V>, f: (v: V) => V): HashMap<K, V> {
  return buildIMap(
    this,
    ([k]) => k,
    (_, [, v]) => f(v)
  );
}

function collectValuesIMap<K, V>(this: HashMap<K & HashKey, V>, f: (v: V) => V | null | undefined): HashMap<K, V> {
  // eslint-disable-next-line @typescript-eslint/no-this-alias
  let m = this;
  for (const [k, v] of this) {
    const newV = f(v);
    if (newV === undefined || newV === null) {
      m = m.delete(k);
    } else if (newV !== v) {
      m = m.set(k, newV);
    }
  }
  return m;
}

export function unionMaps<K, V>(
  merge: (v1: V, v2: V) => V,
  ...maps: readonly HashMap<K & HashKey, V>[]
): HashMap<K, V> {
  const nonEmpty = maps.filter((m) => m.size > 0);
  if (nonEmpty.length === 0) {
    return emptyIMap();
  } else if (nonEmpty.length === 1) {
    return nonEmpty[0];
  }
  let m = nonEmpty[0];
  for (let i = 1; i < nonEmpty.length; i++) {
    for (const [k, v] of nonEmpty[i]) {
      m = m.modify(k, (old) => (old === undefined || merge === undefined ? v : merge(old, v)));
    }
  }
  return m;
}

function unionOneMap<K, V>(this: HashMap<K, V>, other: HashMap<K, V>, merge?: (v1: V, v2: V) => V) {
  return unionMaps(merge ?? ((_, snd) => snd), this as HashMap<K & HashKey, V>, other as HashMap<K & HashKey, V>);
}

/* eslint-disable @typescript-eslint/no-unsafe-member-access */
/* eslint-disable @typescript-eslint/no-unsafe-argument */
/* eslint-disable @typescript-eslint/no-unsafe-assignment */
const hamtProto = hamt.empty.__proto__;
if (hamtProto.toLazySeq === undefined) {
  hamtProto.toLazySeq = imapToLazySeq;
  hamtProto.keysToLazySeq = imapToKeysLazySeq;
  hamtProto.valuesToLazySeq = imapToValuesLazySeq;
  hamtProto.filter = filter;
  hamtProto.bulkDelete = bulkDeleteIMap;
  hamtProto.mapValues = mapValuesIMap;
  hamtProto.collectValues = collectValuesIMap;
  hamtProto.union = unionOneMap;
  hamtProto["@@__IMMUTABLE_KEYED__@@"] = true;
}
