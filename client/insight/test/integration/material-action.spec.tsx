import { expect, test } from "vitest";

import { MaterialAction } from "../../src/components/station-monitor/Material.js";
import * as api from "../../src/network/api.js";
import { createPalletOnlyFixture } from "./basket-fixtures.js";
import { renderInsightPage } from "./framework.js";
import { createCurrentStatus, createMaterial } from "./load-station-testkit.js";

const cases: ReadonlyArray<readonly [string, number, number, boolean, string]> = [
  ["empty second process", 2, 1, true, "Waiting"],
  ["machining on the first process", 1, 1, true, "Waiting for machining"],
  ["machining on an alternative second-process path", 2, 2, true, "Waiting for machining"],
  ["job route not yet available", 2, 1, false, "Waiting for machining"],
];

test.each(cases)("pallet material status: %s", async (_, process, path, jobAvailable, expected) => {
  const fixture = createPalletOnlyFixture();
  const first = fixture.job.procsAndPaths[0];
  const machining = first.paths[0].toJSON();
  const job = api.ActiveJob.fromJS({
    ...fixture.job.toJSON(),
    ProcsAndPaths: [
      first.toJSON(),
      { paths: [{ ...machining, Load: [3], Unload: [4], Stops: [] }, machining] },
    ],
  });
  const mat = createMaterial({
    materialID: 877,
    jobUnique: job.unique,
    partName: job.partName,
    process,
    path,
    location: { type: api.LocType.OnPallet, palletNum: 11, face: 1 },
    action: { type: api.ActionType.Waiting },
  });
  const screen = await renderInsightPage(<MaterialAction mat={mat} />, {
    currentStatus: createCurrentStatus({
      jobs: jobAvailable ? { [job.unique]: job } : {},
      material: [mat],
    }),
  });
  await expect.element(screen.getByText(expected, { exact: true })).toBeVisible();
});
