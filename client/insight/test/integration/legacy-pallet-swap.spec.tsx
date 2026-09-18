import { expect, test } from "vitest";
import { LogEntry, LogType } from "../../src/network/api.js";
import { LogEntries } from "../../src/components/LogEntry.js";
import { renderInsightPage } from "./framework.js";

test("displays a retired pallet swap audit from existing history", async () => {
  const audit = LogEntry.fromJS({
    counter: 100,
    material: [],
    type: "SwapMaterialOnPallet",
    startofcycle: false,
    endUTC: "2025-01-02T12:00:00Z",
    loc: "SwapMatOnPallet",
    locnum: 1,
    pal: 2,
    program: "SwapMatOnPallet",
    result: "Replace A with B on pallet 2",
    elapsed: "00:00:00",
    active: "00:00:00",
  });
  expect(audit.type).toBe(LogType.SwapMaterialOnPallet);
  const screen = await renderInsightPage(<LogEntries entries={[audit]} />);
  await expect.element(screen.getByText("Swap Serial", { exact: true })).toBeVisible();
  await expect.element(screen.getByText("Replace A with B on pallet 2")).toBeVisible();
});
