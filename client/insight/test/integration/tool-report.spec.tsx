import copy from "copy-to-clipboard";
import { atom } from "jotai";
import { expect, test, vi } from "vitest";
import { ToolReportPage } from "../../src/components/operations/ToolReport.js";
import {
  machineReportSourceProvider,
  type MachineReportSource,
} from "../../src/data/machine-report-sources.js";
import { renderInsightPage } from "./framework.js";

vi.mock("copy-to-clipboard", () => ({ default: vi.fn() }));

test("shared machine groups keep scheduled use unknown and retain measured tool life", async () => {
  const sources: ReadonlyArray<MachineReportSource> = [
    {
      id: "first",
      label: "First cell",
      machines: [
        { group: "Mill", numbers: [1] },
        { group: "Drill", numbers: [1] },
      ],
      readTools: async () => [
        {
          toolName: "Insert",
          machineGroupName: "Mill",
          machineNum: 1,
          pocket: 4,
          currentUse: "PT10M",
          totalLifeTime: "PT60M",
          currentUseCount: 2,
          totalLifeCount: 12,
        },
        {
          toolName: "Bit",
          machineGroupName: "Drill",
          machineNum: 1,
          pocket: 3,
          currentUse: "PT2M",
          totalLifeTime: "PT20M",
        },
      ],
      readPrograms: async () => [],
    },
    {
      id: "second",
      label: "Second cell",
      machines: [{ group: "Mill", numbers: [2] }],
      readTools: async () => [],
      readPrograms: async () => [],
    },
  ];
  const screen = await renderInsightPage(<ToolReportPage />, {
    seedStore: (store) => store.set(machineReportSourceProvider, atom(sources)),
  });
  await screen.getByRole("button", { name: "Load Tools" }).click();
  const insert = screen.getByRole("row", { name: /Insert/ });
  await expect.element(insert.getByRole("cell", { name: "Unknown" })).toHaveLength(3);
  const bit = screen.getByRole("row", { name: /Bit/ });
  await expect.element(bit.getByText("0", { exact: true })).toBeVisible();
  await insert.getByRole("button").click();
  await expect
    .element(screen.getByText("Program use is unknown for one or more machines."))
    .toBeVisible();

  await screen.getByRole("button", { name: "Copy to Clipboard" }).click();
  const html = vi.mocked(copy).mock.lastCall?.[0] ?? "";
  expect(html).toContain(
    "<td>Insert</td><td>Mill #1</td><td>4</td><td>Unknown</td><td>10.0</td><td>60.0</td><td>50.0</td><td>Unknown</td>",
  );
  expect(html).toContain("<td>Bit</td><td>Drill #1</td><td>3</td><td>0.0</td>");

  await screen.getByRole("combobox", { name: "" }).click();
  await screen.getByRole("option", { name: "Mill #1" }).click();
  await expect.element(insert.getByText("Unknown")).toBeVisible();
  await expect.element(insert.getByText("50.0 min", { exact: false })).toBeVisible();
  await insert.getByRole("button").click();
  await expect.element(screen.getByText("Program use is unknown for this machine.")).toBeVisible();
});
