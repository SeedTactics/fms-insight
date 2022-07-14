import { emptyIMap, HashKey, HashMap } from "./imap";
import { LazySeq } from "./lazyseq";

export class HashSet<T> {
  #imap: HashMap<T, number>;

  private constructor(imap: HashMap<T, number>) {
    this.#imap = imap;
  }

  public static empty<T>(): HashSet<T> {
    return new HashSet<T>(emptyIMap<T, number>());
  }

  public has(item: T & HashKey): boolean {
    return this.#imap.has(item);
  }

  public get size(): number {
    return this.#imap.size;
  }

  public add(item: T & HashKey): HashSet<T> {
    return new HashSet<T>(this.#imap.set(item, 1));
  }

  public delete(item: T & HashKey): HashSet<T> {
    return new HashSet<T>(this.#imap.delete(item));
  }

  [Symbol.iterator](): Iterator<T> {
    return this.#imap.keys();
  }

  public toLazySeq(): LazySeq<T> {
    return this.#imap.keysToLazySeq();
  }
}
