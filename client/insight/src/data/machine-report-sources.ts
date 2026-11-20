import { HashMap } from "@seedtactics/immutable-collections";
import { atom, type Atom } from "jotai";
import type { IMachiningStop, IProgramInCellController, IToolInMachine } from "../network/api.js";

export interface MachineReportSource {
  readonly id: string;
  readonly label: string;
  readonly machines: ReadonlyArray<{
    readonly group: string;
    readonly numbers: ReadonlyArray<number>;
  }>;
  readonly readTools: () => Promise<ReadonlyArray<Readonly<IToolInMachine>>>;
  readonly readPrograms: () => Promise<ReadonlyArray<Readonly<IProgramInCellController>>>;
}

export const machineReportSourceProvider = atom<Atom<ReadonlyArray<MachineReportSource>> | null>(
  null,
);
export const machineReportSources = atom((get): ReadonlyArray<MachineReportSource> => {
  const provider = get(machineReportSourceProvider);
  return provider === null ? [] : get(provider);
});
export const selectedMachineReportSource = atom<string | null>(null);
export const machineReportSource = atom((get): MachineReportSource | null => {
  const sources = get(machineReportSources);
  const selected = get(selectedMachineReportSource);
  return sources.find((source) => source.id === selected) ?? sources[0] ?? null;
});

export function machineReportIncludesStop(
  source: MachineReportSource | null,
  stop: IMachiningStop,
) {
  return (
    source === null ||
    source.machines.some(
      (machines) =>
        machines.group === stop.stationGroup &&
        stop.stationNums.some((num) => machines.numbers.includes(num)),
    )
  );
}

// The existing historical projections aggregate by station group. A shared group can
// contribute inventory and planned stops, but its history cannot identify a specific source.
export const machineReportHistoryGroups = atom((get): ReadonlySet<string> | null => {
  const source = get(machineReportSource);
  if (source === null) return null;
  const others = get(machineReportSources).filter((other) => other.id !== source.id);
  return new Set(
    source.machines
      .map((machines) => machines.group)
      .filter(
        (group) =>
          !others.some((other) => other.machines.some((machines) => machines.group === group)),
      ),
  );
});

type Inventory<Row> = Readonly<{
  rows: ReadonlyArray<Row>;
  time: Date | null;
  loading: boolean;
  error: string | null;
}>;

export function machineInventory<Row>(
  read: (source: MachineReportSource | null) => Promise<ReadonlyArray<Row>>,
) {
  const states = atom(HashMap.empty<string, Inventory<Row>>());
  const empty: Inventory<Row> = { rows: [], time: null, loading: false, error: null };
  const status = atom((get) => get(states).get(get(machineReportSource)?.id ?? "") ?? empty);
  const refresh = atom(null, async (get, set, time: Date) => {
    const source = get(machineReportSource);
    const key = source?.id ?? "";
    const previous = get(states).get(key) ?? empty;
    if (previous.loading) return;

    set(states, (current) => current.set(key, { ...previous, loading: true, error: null }));
    try {
      const rows = await read(source);
      set(states, (current) => current.set(key, { rows, time, loading: false, error: null }));
    } catch (error) {
      const message = error instanceof Error ? error.message : "The inventory request failed.";
      const label = source?.label;
      set(states, (current) =>
        current.set(key, {
          ...previous,
          loading: false,
          error: label ? `${label}: ${message}` : message,
        }),
      );
    }
  });
  return { status, refresh };
}
