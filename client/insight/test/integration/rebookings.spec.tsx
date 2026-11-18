import { afterEach, expect, test, vi } from "vitest";
import { page } from "vitest/browser";

import { RebookingsPage } from "../../src/components/operations/Rebookings.js";
import { ActiveJob, ProcessInfo } from "../../src/network/api.js";
import { registerNetworkBackend } from "../../src/network/backend.js";
import { renderInsightPage } from "./framework.js";
import { createCurrentStatus } from "./load-station-testkit.js";

afterEach(() => vi.restoreAllMocks());

test.each([
  { input: "  New Part  ", select: false, part: "New Part" },
  { input: "Existing", select: true, part: "Existing Part" },
])("creates a booking for $part", async ({ input, select, part }) => {
  const fetch = vi
    .spyOn(window, "fetch")
    .mockImplementation(async (_, init) =>
      init?.method === "POST" ? new Response(null, { status: 204 }) : Response.json([]),
    );
  registerNetworkBackend();

  await renderInsightPage(<RebookingsPage />, {
    currentStatus: createCurrentStatus({
      jobs: {
        job: new ActiveJob({
          unique: "job",
          partName: "Existing Part",
          cycles: 1,
          routeStartUTC: new Date("2026-04-24T12:00:00Z"),
          routeEndUTC: new Date("2026-04-24T13:00:00Z"),
          archived: false,
          copiedToSystem: true,
          procsAndPaths: [new ProcessInfo({ paths: [] })],
        }),
      },
    }),
  });

  await page.getByRole("button", { name: "Add Rebooking" }).click();
  await page.getByRole("combobox", { name: "Part Name" }).fill(input);
  if (select) {
    await page.getByRole("option", { name: part }).click();
  }
  await page.getByRole("spinbutton", { name: "Quantity" }).fill("1");
  const create = page.getByRole("button", { name: "Create" });
  await expect.element(create).toBeEnabled();
  await create.click();

  expect(fetch).toHaveBeenCalledWith(
    `/api/v1/log/events/rebooking?partName=${encodeURIComponent(part)}&qty=1`,
    expect.objectContaining({ method: "POST" }),
  );
});
