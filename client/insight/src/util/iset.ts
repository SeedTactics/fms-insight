import { emptyIMap, HashKey, IMap, iterableToIMap } from "./imap";
import { LazySeq } from "./lazyseq";

export class ISet<T> {
  #imap: IMap<T, number>;

  private constructor(imap: IMap<T, number>) {
    this.#imap = imap;
  }

  public static empty<T>(): ISet<T> {
    return new ISet<T>(emptyIMap<T, number>());
  }

  public static fromIterable<T>(items: Iterable<T & HashKey>): ISet<T> {
    return new ISet<T>(iterableToIMap(LazySeq.ofIterable(items).map((i) => [i, 1])));
  }

  public has(item: T & HashKey): boolean {
    return this.#imap.has(item);
  }

  public get size(): number {
    return this.#imap.size;
  }

  public add(item: T & HashKey): ISet<T> {
    return new ISet<T>(this.#imap.set(item, 1));
  }

  public delete(item: T & HashKey): ISet<T> {
    return new ISet<T>(this.#imap.delete(item));
  }

  [Symbol.iterator](): Iterator<T> {
    return this.#imap.keys();
  }

  public toLazySeq(): LazySeq<T> {
    return this.#imap.keysToLazySeq();
  }
}
