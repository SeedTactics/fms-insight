import * as api from "../network/api.js";

type BasketNameEntry = Readonly<Pick<api.ILogEntry, "pal">>;
type BasketCycleEntry = BasketNameEntry & Readonly<Pick<api.ILogEntry, "startofcycle">>;

export function basketContainerName(entry: BasketNameEntry, basketName: string): string {
  if (entry.pal > 0) return `${basketName} ${entry.pal}`;
  return basketName;
}

export function basketCycleDescription(entry: BasketCycleEntry, basketName: string): string {
  const container = basketContainerName(entry, basketName);
  if (entry.startofcycle) return `${container} started cycle`;

  return `${container} completed cycle`;
}
