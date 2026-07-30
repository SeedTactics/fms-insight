import * as api from "../network/api.js";

type ContainerNameEntry = Readonly<Pick<api.ILogEntry, "pal" | "containerId">>;
type BasketCycleEntry = ContainerNameEntry &
  Readonly<Pick<api.ILogEntry, "startofcycle" | "containerIds">>;

export function basketContainerName(entry: ContainerNameEntry, basketName: string): string {
  if (entry.containerId) return `${basketName} fragment ${entry.containerId.slice(0, 8)}`;
  if (entry.pal > 0) return `${basketName} ${entry.pal}`;
  return basketName;
}

export function basketCycleDescription(entry: BasketCycleEntry, basketName: string): string {
  const container = basketContainerName(entry, basketName);
  if (entry.startofcycle) return `${container} started cycle`;

  const fragmentCount = entry.containerIds?.length ?? 0;
  const fragments =
    fragmentCount > 0
      ? ` from ${fragmentCount} UUID fragment${fragmentCount === 1 ? "" : "s"}`
      : "";
  return `${container} completed cycle${fragments}`;
}
