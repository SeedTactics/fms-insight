import * as api from "../network/api.js";

type BasketNameEntry = Readonly<Pick<api.ILogEntry, "pal" | "basketContentEpisodeId">>;
type BasketCycleEntry = BasketNameEntry &
  Readonly<Pick<api.ILogEntry, "startofcycle" | "basketCycleEndContentEpisodeIds">>;

export function basketContainerName(entry: BasketNameEntry, basketName: string): string {
  if (entry.basketContentEpisodeId)
    return `${basketName} fragment ${entry.basketContentEpisodeId.slice(0, 8)}`;
  if (entry.pal > 0) return `${basketName} ${entry.pal}`;
  return basketName;
}

export function basketCycleDescription(entry: BasketCycleEntry, basketName: string): string {
  const container = basketContainerName(entry, basketName);
  if (entry.startofcycle) return `${container} started cycle`;

  const fragmentCount = entry.basketCycleEndContentEpisodeIds?.length ?? 0;
  const fragments =
    fragmentCount > 0
      ? ` from ${fragmentCount} UUID fragment${fragmentCount === 1 ? "" : "s"}`
      : "";
  return `${container} completed cycle${fragments}`;
}
