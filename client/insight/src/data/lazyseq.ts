import { Vector, HashMap, WithEquality, Option, ToOrderable, Ordering } from "prelude-ts";

function mkIterable<T>(f: () => IterableIterator<T>): Iterable<T> {
  return {
    [Symbol.iterator]() {
      return f();
    }
  };
}

function mapI<T, S>(f: (x: T, idx: number) => S, i: Iterable<T>): Iterable<S> {
  return mkIterable(function*() {
    let idx = 0;
    for (let x of i) {
      yield f(x, idx);
      idx += 1;
    }
  });
}

function flatI<T, S>(f: (x: T) => Iterable<S>, i: Iterable<T>): Iterable<S> {
  return mkIterable(function*() {
    for (let x of i) {
      yield* f(x);
    }
  });
}

function filterI<T>(f: (x: T) => boolean, i: Iterable<T>): Iterable<T> {
  return mkIterable(function*() {
    for (let x of i) {
      if (f(x)) {
        yield x;
      }
    }
  });
}

function objectI<V>(obj: { [k: string]: V }): Iterable<[string, V]> {
  return mkIterable(function*() {
    for (let k in obj) {
      if (obj.hasOwnProperty(k)) {
        yield [k, obj[k]] as [string, V];
      }
    }
  });
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

  map<S>(f: (x: T, idx: number) => S): LazySeq<S> {
    return new LazySeq<S>(mapI(f, this.iter));
  }

  filter(f: (x: T) => boolean): LazySeq<T> {
    return new LazySeq<T>(filterI(f, this.iter));
  }

  flat<S>(f: (x: T) => Iterable<S>): LazySeq<S> {
    return new LazySeq<S>(flatI(f, this.iter));
  }

  reduce(f: (x1: T, x2: T) => T): Option<T> {
    let ret = Option.none<T>();
    for (let x of this.iter) {
      if (ret.isNone()) {
        ret = Option.of(x);
      } else {
        ret = Option.of(f(ret.get(), x));
      }
    }
    return ret;
  }

  minBy(compare: (v1: T, v2: T) => Ordering): Option<T> {
    return this.reduce((v1, v2) => (compare(v1, v2) > 0 ? v2 : v1));
  }

  minOn(getOrderable: ToOrderable<T>): Option<T> {
    let ret = Option.none<T>();
    let minVal: number | string | boolean | undefined;
    for (let x of this.iter) {
      if (ret.isNone()) {
        ret = Option.of(x);
        minVal = getOrderable(x);
      } else {
        const curVal = getOrderable(x);
        if (!minVal || minVal > curVal) {
          ret = Option.of(x);
          minVal = curVal;
        }
      }
    }
    return ret;
  }

  maxBy(compare: (v1: T, v2: T) => Ordering): Option<T> {
    return this.reduce((v1, v2) => (compare(v1, v2) < 0 ? v2 : v1));
  }

  maxOn(getOrderable: ToOrderable<T>): Option<T> {
    let ret = Option.none<T>();
    let maxVal: number | string | boolean | undefined;
    for (let x of this.iter) {
      if (ret.isNone()) {
        ret = Option.of(x);
        maxVal = getOrderable(x);
      } else {
        const curVal = getOrderable(x);
        if (!maxVal || maxVal < curVal) {
          ret = Option.of(x);
          maxVal = curVal;
        }
      }
    }
    return ret;
  }

  groupOn<K>(f: (x: T) => K & WithEquality): HashMap<K, Vector<T>> {
    return this.groupBy<K & WithEquality, Vector<T>>(
      x => [f(x), Vector.of(x)] as [K & WithEquality, Vector<T>],
      (v1, v2) => v1.appendAll(v2)
    );
  }

  groupBy<K, S>(f: (x: T) => [K & WithEquality, S], merge: (v1: S, v2: S) => S): HashMap<K, S> {
    let m = HashMap.empty<K, S>();
    for (let x of this.iter) {
      const [k, s] = f(x);
      m = m.putWithMerge(k, s, merge);
    }
    return m;
  }

  toVector(): Vector<T> {
    return Vector.ofIterable(this.iter);
  }

  toArray(): Array<T> {
    return Array.from(this.iter);
  }

  private constructor(private iter: Iterable<T>) {}
}
