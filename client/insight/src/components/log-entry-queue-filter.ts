import * as api from "../network/api.js";

export function* filterRemoveAddQueue(
  entries: Iterable<Readonly<api.ILogEntry>>,
): Iterable<Readonly<api.ILogEntry>> {
  let prev: Readonly<api.ILogEntry> | null = null;

  for (const e of entries) {
    if (
      prev != null &&
      prev.type === api.LogType.RemoveFromQueue &&
      e.type === api.LogType.AddToQueue &&
      prev.loc === e.loc
    ) {
      prev = null;
    } else {
      if (prev !== null) {
        yield prev;
      }
      prev = e;
    }
  }

  if (prev !== null) {
    yield prev;
  }
}
