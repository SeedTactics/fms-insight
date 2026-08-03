import { expect, it } from "vitest";
import { mapUnscheduledRebookings } from "./rebookings.js";

it("treats a null unscheduled-rebookings response as empty", () => {
  expect(mapUnscheduledRebookings(null).size).toBe(0);
});
