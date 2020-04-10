import { LazySeq } from "./lazyseq";
import { HashMap, Vector, Option, HashSet } from "prelude-ts";

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
  seqShouldBe(oddSeq.appendAll([2, 4]), [1, 3, 5, 7, 9, 2, 4]);
});

it("arrangeBy", () => {
  const m = oddSeq.arrangeBy((x) => x.toString());
  expect(m.isSome()).toBe(true);
  expect(m.getOrThrow().equals(HashMap.of(["1", 1], ["3", 3], ["5", 5], ["7", 7], ["9", 9]))).toBe(true);
  expect(
    LazySeq.ofIterable([1, 2, 1])
      .arrangeBy((x) => x)
      .isNone()
  ).toBe(true);
});

it("chunk", () => {
  seqShouldBe(oddSeq.chunk(2), [Vector.of(1, 3), Vector.of(5, 7), Vector.of(9)]);
});

it("contains", () => {
  expect(oddSeq.contains(3)).toBe(true);
  expect(oddSeq.contains(2)).toBe(false);
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
  expect(oddSeq.find((x) => x === 3).getOrThrow()).toBe(3);
  expect(oddSeq.find((x) => x === 2).isNone()).toBe(true);
});

it("flatMap", () => {
  seqShouldBe(
    oddSeq.flatMap((x) => [x, x + 1]),
    [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
  );
});

it("groupBy", () => {
  const m: HashMap<string, Vector<number>> = oddSeq.groupBy((x) => (x < 6 ? "low" : "high"));
  expect(m.equals(HashMap.of(["low", Vector.of(1, 3, 5)], ["high", Vector.of(7, 9)])));
});

it("head", () => {
  expect(oddSeq.head().getOrThrow()).toBe(1);
  expect(LazySeq.ofIterable([]).head().isNone()).toBe(true);
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

it("mapOption", () => {
  seqShouldBe(
    oddSeq.mapOption((x) => (x === 5 ? Option.none<string>() : Option.some(x.toString()))),
    ["1", "3", "7", "9"]
  );
});

it("maxBy", () => {
  expect(oddSeq.maxBy((v1, v2) => v1 - v2).getOrThrow()).toBe(9);
  expect(
    LazySeq.ofIterable([])
      .maxBy((v1, v2) => v1 - v2)
      .isNone()
  ).toBe(true);
});

it("maxOn", () => {
  expect(
    oddSeq
      .map((x) => ({ prop: x }))
      .maxOn((x) => x.prop)
      .getOrThrow()
  ).toEqual({ prop: 9 });
  expect(
    LazySeq.ofIterable([] as { prop: number }[])
      .maxOn((x) => x.prop)
      .isNone()
  ).toBe(true);
});

it("minBy", () => {
  expect(oddSeq.minBy((v1, v2) => v1 - v2).getOrThrow()).toBe(1);
  expect(
    LazySeq.ofIterable([])
      .minBy((v1, v2) => v1 - v2)
      .isNone()
  ).toBe(true);
});

it("minOn", () => {
  expect(
    oddSeq
      .map((x) => ({ prop: x }))
      .minOn((x) => x.prop)
      .getOrThrow()
  ).toEqual({ prop: 1 });
  expect(
    LazySeq.ofIterable([] as { prop: number }[])
      .minOn((x) => x.prop)
      .isNone()
  ).toBe(true);
});

it("prepend", () => {
  seqShouldBe(oddSeq.prepend(100), [100, 1, 3, 5, 7, 9]);
});

it("prependAll", () => {
  seqShouldBe(oddSeq.prependAll([100, 200]), [100, 200, 1, 3, 5, 7, 9]);
});

it("reduce", () => {
  expect(oddSeq.reduce((x1, x2) => x1 + x2).getOrThrow()).toBe(1 + 3 + 5 + 7 + 9);
  expect(
    LazySeq.ofIterable([])
      .reduce((x1, _) => x1)
      .isNone()
  ).toBe(true);
});

it("single", () => {
  expect(oddSeq.single().isNone()).toBe(true);
  expect(LazySeq.ofIterable([]).single().isNone()).toBe(true);
  expect(LazySeq.ofIterable([1]).single().getOrThrow()).toBe(1);
});

it("sortBy", () => {
  seqShouldBe(
    LazySeq.ofIterable([9, 8, 7, 6]).sortBy((v1, v2) => v1 - v2),
    [6, 7, 8, 9]
  );
});

it("sortOn", () => {
  seqShouldBe(
    LazySeq.ofIterable([{ a: 5 }, { a: 4 }, { a: 10 }, { a: 7 }]).sortOn((x) => x.a),
    [{ a: 4 }, { a: 5 }, { a: 7 }, { a: 10 }]
  );
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
  expect(oddSeq.toArray()).toEqual([1, 3, 5, 7, 9]);
});

it("toMap", () => {
  const m = oddSeq.toMap(
    (x) => (x < 6 ? ["low", x] : ["high", x]),
    (v1, v2) => v1 + v2
  );
  const expected = HashMap.of(["low", 1 + 3 + 5], ["high", 7 + 9]);
  expect(m.equals(expected)).toBe(true);
});

it("toSet", () => {
  expect(oddSeq.toSet((x) => x + 1).equals(HashSet.of(2, 4, 6, 8, 10))).toBe(true);
});

it("toVector", () => {
  expect(oddSeq.toVector().equals(Vector.of(1, 3, 5, 7, 9))).toBe(true);
});

it("transform", () => {
  expect(oddSeq.transform((s) => s.toArray())).toEqual([1, 3, 5, 7, 9]);
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
