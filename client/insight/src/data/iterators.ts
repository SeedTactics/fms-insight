import { Vector, HashMap, WithEquality } from "prelude-ts";

function* mapI<T, S>(f: (x: T) => S, i: Iterable<T>): IterableIterator<S> {
  for (let x of i) {
    yield f(x);
  }
}

function* mapToKeyAndSingleton<T, K>(f: (x: T) => K, i: Iterable<T>): IterableIterator<[K, Vector<T>]> {
  for (let x of i) {
    yield [f(x), Vector.of(x)];
  }
}

function* flatI<T, S>(f: (x: T) => Iterable<S>, i: Iterable<T>): IterableIterator<S> {
  for (let x of i) {
    yield* f(x);
  }
}

function* objectI<V>(obj: { [k: string]: V }): IterableIterator<[string, V]> {
  for (let k in obj) {
    if (obj.hasOwnProperty(k)) {
      yield [k, obj[k]];
    }
  }
}

export class LazySeq<T> {
  static ofIterable<T>(iter: Iterable<T>): LazySeq<T> {
    return new LazySeq(iter);
  }

  static ofObject<V>(obj: { [k: string]: V }): LazySeq<[string, V]> {
    return new LazySeq<[string, V]>(objectI(obj));
  }

  [Symbol.iterator](): Iterator<T> {
    return this.iter[Symbol.iterator]();
  }

  map<S>(f: (x: T) => S): LazySeq<S> {
    return new LazySeq<S>(mapI(f, this.iter));
  }

  flat<S>(f: (x: T) => Iterable<S>): LazySeq<S> {
    return new LazySeq<S>(flatI(f, this.iter));
  }

  groupOn<K>(f: (x: T) => K & WithEquality): HashMap<K, Vector<T>> {
    return HashMap.empty<K, Vector<T>>().mergeWith(mapToKeyAndSingleton(f, this.iter), (v1, v2) => v1.appendAll(v2));
  }

  groupBy<K, S>(f: (x: T) => [K & WithEquality, S], merge: (v1: S, v2: S) => S): HashMap<K, S> {
    return HashMap.empty<K, S>().mergeWith(mapI(f, this.iter), merge);
  }

  toVector(): Vector<T> {
    return Vector.ofIterable(this.iter);
  }

  toArray(): Array<T> {
    return Array.from(this.iter);
  }

  private constructor(private iter: Iterable<T>) {}
}
