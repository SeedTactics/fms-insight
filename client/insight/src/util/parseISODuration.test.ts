import { expect, test } from "vitest";
import parseISODuration, { durationToMinutes, durationToSeconds } from "./parseISODuration.js";

test.each([
  ["PT10S", 10],
  ["-PT10S", -10],
  ["PT1M30S", 90],
  ["-PT1M30S", -90],
  ["-P1DT2H3M4.5S", -93784.5],
  ["-PT0.25S", -0.25],
  ["PT-10S", -10],
  ["PT-1M-30S", -90],
  ["PT-1M30S", -30],
  ["-PT-1M30S", 30],
  ["PT0S", 0],
])("converts %s to signed seconds and minutes", (input, expected) => {
  expect(durationToSeconds(input)).toBe(expected);
  expect(durationToMinutes(input)).toBe(expected / 60);
});

test("applies the leading sign to every duration component", () => {
  expect(parseISODuration("-P1Y2M3DT4H5M6.5S")).toEqual({
    years: -1,
    months: -2,
    days: -3,
    hours: -4,
    minutes: -5,
    seconds: -6.5,
  });
});

test.each(["", "P", "PT", "-P", "-PT", "notPT10S", "PT10Signored", "--PT10S"])(
  "rejects malformed duration %s",
  (input) => {
    expect(parseISODuration(input)).toBeNull();
    expect(durationToSeconds(input)).toBe(0);
  },
);
