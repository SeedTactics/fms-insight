import * as api from "../network/api.js";

type ContainerNameEntry = Readonly<Pick<api.ILogEntry, "pal" | "containerId">>;

export function basketContainerName(entry: ContainerNameEntry, basketName: string): string {
  if (entry.containerId) return `${basketName} fragment ${entry.containerId.slice(0, 8)}`;
  if (entry.pal > 0) return `${basketName} ${entry.pal}`;
  return basketName;
}
