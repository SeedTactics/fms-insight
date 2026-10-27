import { expect, test } from "vitest";
import { page, userEvent } from "vitest/browser";
import { onLoadCurrentSt } from "../../src/cell-status/loading.js";
import { Header } from "../../src/components/Navigation.js";
import type { ICurrentStatus } from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createCurrentStatus } from "./load-station-testkit.js";

const header = (
  <Header
    showAlarms
    showLogout={false}
    showSearch={false}
    showOperator={false}
    Nav1={undefined}
    Nav2={undefined}
  />
);

test.each([
  { alarms: [], label: "No alarms", tooltip: "No alarms" },
  {
    alarms: ["Controller connection unavailable."],
    label: "1 alarm",
    tooltip: "Controller connection unavailable.",
  },
  {
    alarms: ["Controller connection unavailable.", "Robot sensing unavailable."],
    label: "2 alarms",
    tooltip: "2 alarms",
  },
])(
  "the alarm indicator summarizes and opens the current list: $label",
  async ({ alarms, label, tooltip }) => {
    const status: Readonly<ICurrentStatus> = createCurrentStatus();
    const screen = await renderInsightPage(header, { currentStatus: { ...status, alarms } });
    const button = screen.getByRole("button", { name: label, exact: true });
    await expect.element(button).toBeEnabled();
    await userEvent.hover(button);
    await expect.element(page.getByRole("tooltip")).toHaveTextContent(tooltip);
    await button.click();
    const dialog = page.getByRole("dialog", { name: "System alarms" });
    await expect.element(dialog).toBeVisible();
    if (alarms.length === 0) await expect.element(dialog.getByText("No alarms.")).toBeVisible();
    for (const message of alarms)
      await expect.element(dialog.getByText(message, { exact: true })).toBeVisible();
    await dialog.getByRole("button", { name: "Close", exact: true }).click();
    await expect.element(dialog).not.toBeInTheDocument();
  },
);

test("an open alarm dialog follows the current snapshot through recovery", async () => {
  const status: Readonly<ICurrentStatus> = createCurrentStatus();
  const screen = await renderInsightPage(header, { currentStatus: status });
  await screen.getByRole("button", { name: "No alarms", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "System alarms" });
  screen.store.set(onLoadCurrentSt, { ...status, alarms: ["First alarm"] });
  await expect.element(dialog.getByText("First alarm", { exact: true })).toBeVisible();
  screen.store.set(onLoadCurrentSt, { ...status, alarms: ["Replacement alarm"] });
  await expect.element(dialog.getByText("First alarm", { exact: true })).not.toBeInTheDocument();
  await expect.element(dialog.getByText("Replacement alarm", { exact: true })).toBeVisible();
  screen.store.set(onLoadCurrentSt, status);
  await expect
    .element(dialog.getByText("Replacement alarm", { exact: true }))
    .not.toBeInTheDocument();
  await expect.element(dialog.getByText("No alarms.")).toBeVisible();
  await userEvent.keyboard("{Escape}");
  await expect.element(dialog).not.toBeInTheDocument();
});

test("a long alarm list scrolls while Close remains available", async () => {
  const alarms = Array.from(
    { length: 40 },
    (_, index) =>
      `Alarm ${index + 1}: ${"A long equipment diagnostic for visual review. ".repeat(4)}`,
  );
  const status: Readonly<ICurrentStatus> = createCurrentStatus();
  const screen = await renderInsightPage(header, { currentStatus: { ...status, alarms } });
  await screen.getByRole("button", { name: "40 alarms", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "System alarms" });
  const lastAlarm = dialog.getByRole("listitem").last();
  const close = dialog.getByRole("button", { name: "Close", exact: true });
  await expect.element(lastAlarm).not.toBeInViewport();
  await expect.element(close).toBeInViewport();
  await lastAlarm.click();
  await expect.element(lastAlarm).toBeInViewport();
  await expect.element(close).toBeInViewport();
  await close.click();
  await expect.element(dialog).not.toBeInTheDocument();
});
