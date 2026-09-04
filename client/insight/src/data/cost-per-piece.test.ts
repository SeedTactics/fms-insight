import { expect, it } from "vitest";
import { HashMap, HashSet, LazySeq } from "@seedtactics/immutable-collections";

import { compute_monthly_cost_percentages } from "./cost-per-piece.js";
import type { PartCycleData } from "../cell-status/station-cycles.js";
import type { MaterialSummaryAndCompletedData } from "../cell-status/material-summary.js";

it("counts basket-only labor in labor cost but not automation", () => {
  const cycles: ReadonlyArray<PartCycleData> = [
    {
      endTime: new Date(Date.UTC(2026, 0, 5, 12, 0, 0)),
      elapsedMinsPerMaterial: 6,
      cntr: 1,
      part: "tray",
      stationGroup: "L/U",
      stationNumber: 1,
      stationLabel: "L/U #1",
      operation: "UNLOAD",
      carrier: { kind: "basket-lul", basket: 55 },
      activeMinutes: 5,
      material: [],
      operator: "",
      medianCycleMinutes: 5,
      MAD_aboveMinutes: 0.5,
      isOutlier: false,
    },
  ];

  const cost = compute_monthly_cost_percentages(
    LazySeq.of(cycles),
    HashSet.empty(),
    HashMap.empty(),
    {
      month: new Date(Date.UTC(2026, 0, 1, 0, 0, 0)),
    },
  );

  expect(cost.parts).toHaveLength(1);
  expect(cost.parts[0].labor).toBe(1);
  expect(cost.parts[0].automation).toBe(0);

  const returned: MaterialSummaryAndCompletedData = {
    materialID: 1,
    jobUnique: "job",
    partName: "tray",
    startedProcess1: true,
    signaledInspections: [],
    quarantineAfterUnload: null,
    numProcesses: 2,
    unloaded_processes: { 2: cycles[0].endTime },
  };
  const returnedCost = compute_monthly_cost_percentages(
    LazySeq.of(cycles),
    HashSet.empty(),
    HashMap.empty<number, MaterialSummaryAndCompletedData>().set(1, returned),
    { month: new Date(Date.UTC(2026, 0, 1)) },
  );
  expect(returnedCost.parts[0].parts_completed).toBe(0);
  const completedCost = compute_monthly_cost_percentages(
    LazySeq.of(cycles),
    HashSet.empty(),
    HashMap.empty<number, MaterialSummaryAndCompletedData>().set(1, {
      ...returned,
      completed_time: cycles[0].endTime,
    }),
    { month: new Date(Date.UTC(2026, 0, 1)) },
  );
  expect(completedCost.parts[0].parts_completed).toBe(1);
});
