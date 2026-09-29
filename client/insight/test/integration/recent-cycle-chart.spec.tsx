import { expect, test } from "vitest";
import { RecentCycleChart } from "../../src/components/operations/RecentCycleChart.js";
import { secondsSinceEpochAtom } from "../../src/cell-status/current-status.js";
import {
  ActionType,
  LocType,
  PalletLocationEnum,
  type ICurrentStatus,
} from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createCurrentStatus, createMaterial, createPallet } from "./load-station-testkit.js";

test("a cycle starting after the minute tick has visible elapsed and remaining bars", async () => {
  const statusTime = new Date("2030-01-01T12:00:45Z");
  const status: Readonly<ICurrentStatus> = createCurrentStatus({
    pallets: [
      createPallet({
        palletNum: 1,
        numFaces: 1,
        currentPalletLocation: { loc: PalletLocationEnum.Machine, group: "MC", num: 1 },
      }),
    ],
    material: [
      createMaterial({
        materialID: 1,
        jobUnique: "job",
        partName: "part",
        process: 1,
        path: 1,
        location: { type: LocType.OnPallet, palletNum: 1 },
        action: {
          type: ActionType.Machining,
          program: "program",
          elapsedMachiningTime: "PT15S",
          expectedRemainingMachiningTime: "PT5M",
        },
      }),
    ],
  });
  const screen = await renderInsightPage(<RecentCycleChart width={1000} height={300} />, {
    currentStatus: { ...status, timeOfCurrentStatusUTC: statusTime },
    seedStore: (store) => store.set(secondsSinceEpochAtom, statusTime.getTime() / 1000),
  });

  await expect
    .poll(() =>
      Array.from(screen.container.querySelectorAll("svg rect[fill]")).map(
        (rect) => Number(rect.getAttribute("width")) > 0,
      ),
    )
    .toEqual([true, true]);
});
