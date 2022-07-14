import { LazySeq } from "./lazyseq";
import { it, expect } from "vitest";
import { emptyIMap, unionMaps } from "./imap";

const oddSeq = LazySeq.ofIterator(function* () {
  for (let x = 1; x <= 9; x += 2) {
    yield x;
  }
});

function seqShouldBe<T>(actual: LazySeq<T>, expected: ReadonlyArray<T>) {
  expect(Array.from(actual)).toEqual(expected);
}

it("loads a lazyseq from an iterable", () => {
  seqShouldBe(LazySeq.ofIterable([1, 2, 3]), [1, 2, 3]);
});

it("loads a lazyseq from an object", () => {
  seqShouldBe(LazySeq.ofObject({ a: 1, b: 2 }), [
    ["a", 1],
    ["b", 2],
  ]);
});

it("loads a lazyseq range", () => {
  seqShouldBe(LazySeq.ofRange(3, 8, 2), [3, 5, 7]);
});

it("allMatch", () => {
  expect(oddSeq.allMatch((x) => x % 2 === 1)).toBe(true);
  expect(oddSeq.allMatch((x) => x === 1)).toBe(false);
});

it("anyMatch", () => {
  expect(oddSeq.anyMatch((x) => x % 2 === 0)).toBe(false);
  expect(oddSeq.anyMatch((x) => x === 5)).toBe(true);
});

it("appends", () => {
  seqShouldBe(oddSeq.append(2), [1, 3, 5, 7, 9, 2]);
});

it("appends all", () => {
  seqShouldBe(oddSeq.concat([2, 4]), [1, 3, 5, 7, 9, 2, 4]);
});

it("chunk", () => {
  seqShouldBe(oddSeq.chunk(2), [[1, 3], [5, 7], [9]]);
});

it("drops", () => {
  seqShouldBe(oddSeq.drop(2), [5, 7, 9]);
  seqShouldBe(oddSeq.drop(100), []);
});

it("dropWhile", () => {
  seqShouldBe(
    oddSeq.dropWhile((x) => x < 6),
    [7, 9]
  );
});

it("isEmpty", () => {
  expect(LazySeq.ofIterable([]).isEmpty()).toBe(true);
  expect(oddSeq.isEmpty()).toBe(false);
});

it("filter", () => {
  seqShouldBe(
    oddSeq.filter((x) => x === 3 || x === 7),
    [3, 7]
  );
});

it("find", () => {
  expect(oddSeq.find((x) => x === 3)).toBe(3);
  expect(oddSeq.find((x) => x === 2)).toBeUndefined();
});

it("flatMap", () => {
  seqShouldBe(
    oddSeq.flatMap((x) => [x, x + 1]),
    [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
  );
});

it("groupBy", () => {
  const m = oddSeq.groupBy((x) => (x < 6 ? "low" : "high"));
  seqShouldBe(m, [
    ["low", [1, 3, 5]],
    ["high", [7, 9]],
  ]);
});

it("head", () => {
  expect(oddSeq.head()).toBe(1);
  expect(LazySeq.ofIterable([]).head()).toBeUndefined();
});

it("length", () => {
  expect(oddSeq.length()).toBe(5);
});

it("map", () => {
  seqShouldBe(
    oddSeq.map((x) => x.toString()),
    ["1", "3", "5", "7", "9"]
  );
});

it("collect", () => {
  seqShouldBe(
    oddSeq.collect((x) => (x === 5 ? null : x.toString())),
    ["1", "3", "7", "9"]
  );
});

it("maxOn", () => {
  expect(oddSeq.map((x) => ({ prop: x })).maxOn((x) => x.prop)).toEqual({ prop: 9 });
  expect(LazySeq.ofIterable([] as { prop: number }[]).maxOn((x) => x.prop)).toBeUndefined();
});

it("minOn", () => {
  expect(oddSeq.map((x) => ({ prop: x })).minOn((x) => x.prop)).toEqual({ prop: 1 });
  expect(LazySeq.ofIterable([] as { prop: number }[]).minOn((x) => x.prop)).toBeUndefined();
});

it("prepend", () => {
  seqShouldBe(oddSeq.prepend(100), [100, 1, 3, 5, 7, 9]);
});

it("prependAll", () => {
  seqShouldBe(oddSeq.prependAll([100, 200]), [100, 200, 1, 3, 5, 7, 9]);
});

it("sortBy", () => {
  seqShouldBe(
    LazySeq.ofIterable([9, 8, 7, 6]).sortWith((v1, v2) => v1 - v2),
    [6, 7, 8, 9]
  );
});

it("sortBy to array", () => {
  expect(
    LazySeq.ofIterable([9, 8, 7, 6])
      .sortWith((v1, v2) => v1 - v2)
      .toMutableArray()
  ).toStrictEqual([6, 7, 8, 9]);
});

it("sortOn", () => {
  seqShouldBe(
    LazySeq.ofIterable([{ a: 5 }, { a: 4 }, { a: 10 }, { a: 7 }]).sortBy((x) => x.a),
    [{ a: 4 }, { a: 5 }, { a: 7 }, { a: 10 }]
  );
});

it("sortOn to array", () => {
  expect(
    LazySeq.ofIterable([{ a: 5 }, { a: 4 }, { a: 10 }, { a: 7 }])
      .sortBy((x) => x.a)
      .toMutableArray()
  ).toStrictEqual([{ a: 4 }, { a: 5 }, { a: 7 }, { a: 10 }]);
});

it("sumOn", () => {
  expect(oddSeq.sumOn((x) => x + 1)).toBe(2 + 4 + 6 + 8 + 10);
});

it("tail", () => {
  seqShouldBe(oddSeq.tail(), [3, 5, 7, 9]);
});

it("take", () => {
  seqShouldBe(oddSeq.take(2), [1, 3]);
  seqShouldBe(oddSeq.take(100), [1, 3, 5, 7, 9]);
  seqShouldBe(oddSeq.take(0), []);
});

it("takeWhile", () => {
  seqShouldBe(
    oddSeq.takeWhile((x) => x < 6),
    [1, 3, 5]
  );
});

it("toArray", () => {
  expect(oddSeq.toMutableArray()).toEqual([1, 3, 5, 7, 9]);
});

it("transform", () => {
  expect(oddSeq.transform((s) => s.toMutableArray())).toEqual([1, 3, 5, 7, 9]);
});

it("zip", () => {
  seqShouldBe(oddSeq.zip([2, 4, 6]), [
    [1, 2],
    [3, 4],
    [5, 6],
  ]);
  seqShouldBe(oddSeq.zip([2, 4, 6, 8, 10, 12]), [
    [1, 2],
    [3, 4],
    [5, 6],
    [7, 8],
    [9, 10],
  ]);
});

it("filters IMap", () => {
  let m = emptyIMap<number, string>();
  m = m.set(1, "a");
  m = m.set(2, "b");
  m = m.set(3, "c");
  m = m.set(4, "d");

  m = m.filter((_, i) => i > 2);

  expect(m.toLazySeq().toRArray()).toEqual([
    [3, "c"],
    [4, "d"],
  ]);
});

it("maps values in IMap", () => {
  let m = emptyIMap<number, string>();
  m = m.set(1, "a");
  m = m.set(2, "b");
  m = m.set(3, "c");
  m = m.set(4, "d");

  m = m.mapValues((v) => v + "!");

  expect(m.toLazySeq().toRArray()).toEqual([
    [1, "a!"],
    [2, "b!"],
    [3, "c!"],
    [4, "d!"],
  ]);
});

it("collects values in IMap", () => {
  let m = emptyIMap<number, string>();
  m = m.set(1, "a");
  m = m.set(2, "b");
  m = m.set(3, "c");
  m = m.set(4, "d");

  m = m.collectValues((v) => (v === "b" ? null : v + "!"));

  expect(m.toLazySeq().toRArray()).toEqual([
    [1, "a!"],
    [3, "c!"],
    [4, "d!"],
  ]);
});

it("union maps", () => {
  let m = emptyIMap<number, string>();
  m = m.set(1, "a");
  m = m.set(2, "b");
  m = m.set(3, "c");
  m = m.set(4, "d");

  let m2 = emptyIMap<number, string>();
  m2 = m2.set(4, "e");
  m2 = m2.set(5, "f");

  const e = unionMaps((a, b) => a + b, m, m2);

  expect(e.toLazySeq().toRArray()).toEqual([
    [1, "a"],
    [2, "b"],
    [3, "c"],
    [4, "de"],
    [5, "f"],
  ]);
});

it("toIMap", () => {
  const seq = LazySeq.ofIterable([
    { foo: 1, bar: "aa" },
    { foo: 2, bar: "bb" },
    { foo: 2, bar: "bbbb" },
    { foo: 3, bar: "c" },
    { foo: 1, bar: "aaaa" },
  ]);

  const m = seq.toHashMap((x) => [x.foo, x.bar]);

  expect(m.toLazySeq().toRArray()).toEqual([
    [1, "aaaa"],
    [2, "bbbb"],
    [3, "c"],
  ]);

  const m2 = seq.toHashMap(
    (x) => [x.foo, x.bar],
    (a, b) => a + b
  );

  expect(m2.toLazySeq().toRArray()).toEqual([
    [1, "aaaaaa"],
    [2, "bbbbbb"],
    [3, "c"],
  ]);
});

it("toLookup", () => {
  const seq = LazySeq.ofIterable([
    { foo: 1, bar: "aa" },
    { foo: 1, bar: "aaaa" },
    { foo: 2, bar: "bb" },
    { foo: 3, bar: "c" },
    { foo: 2, bar: "bbbb" },
  ]);

  const lookup = seq.toLookup(
    (x) => x.foo,
    (x) => x.bar
  );

  expect(lookup.toLazySeq().toRArray()).toEqual([
    [1, ["aa", "aaaa"]],
    [2, ["bb", "bbbb"]],
    [3, ["c"]],
  ]);

  const lookupKey = seq.toLookup((x) => x.foo);

  expect(lookupKey.toLazySeq().toRArray()).toEqual([
    [
      1,
      [
        { foo: 1, bar: "aa" },
        { foo: 1, bar: "aaaa" },
      ],
    ],
    [
      2,
      [
        { foo: 2, bar: "bb" },
        { foo: 2, bar: "bbbb" },
      ],
    ],
    [3, [{ foo: 3, bar: "c" }]],
  ]);
});

it("toRLookup", () => {
  const seq = LazySeq.ofIterable([
    { foo: 1, bar: "aa" },
    { foo: 1, bar: "aaaa" },
    { foo: 2, bar: "bb" },
    { foo: 3, bar: "c" },
    { foo: 2, bar: "bbbb" },
  ]);

  const lookup = seq.toRLookup(
    (x) => x.foo,
    (x) => x.bar
  );

  expect(lookup).toStrictEqual(
    new Map([
      [1, ["aa", "aaaa"]],
      [2, ["bb", "bbbb"]],
      [3, ["c"]],
    ])
  );

  const lookupKey = seq.toRLookup((x) => x.foo);

  expect(lookupKey).toStrictEqual(
    new Map([
      [
        1,
        [
          { foo: 1, bar: "aa" },
          { foo: 1, bar: "aaaa" },
        ],
      ],
      [
        2,
        [
          { foo: 2, bar: "bb" },
          { foo: 2, bar: "bbbb" },
        ],
      ],
      [3, [{ foo: 3, bar: "c" }]],
    ])
  );
});

it("toLookupMap", () => {
  const seq = LazySeq.ofIterable([
    { foo: 1, bar: "aa" },
    { foo: 1, bar: "aaaa" },
    { foo: 2, bar: "bb" },
    { foo: 3, bar: "c" },
    { foo: 2, bar: "bbbb" },
  ]);

  const lookup = seq.toLookupMap(
    (x) => x.foo,
    (x) => x.bar
  );

  expect(
    lookup
      .toLazySeq()
      .map(([a, b]) => [a, b.toLazySeq().toRMap((x) => x)] as const)
      .toRMap((x) => x)
  ).toEqual(
    new Map([
      [
        1,
        new Map([
          ["aaaa", { foo: 1, bar: "aaaa" }],
          ["aa", { foo: 1, bar: "aa" }],
        ]),
      ],
      [
        2,
        new Map([
          ["bbbb", { foo: 2, bar: "bbbb" }],
          ["bb", { foo: 2, bar: "bb" }],
        ]),
      ],
      [3, new Map([["c", { foo: 3, bar: "c" }]])],
    ])
  );
});
