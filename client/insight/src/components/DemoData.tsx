import * as React from "react";
import { useRecoilCallback } from "recoil";
import { JobsBackend } from "../data/backend";
import { currentStatus } from "../data/current-status";
import { useRefreshProgramReport, useRefreshToolReport } from "../data/tools-programs";
import { reduxStore } from "../store/store";

export function LoadDemoData() {
  const load = useRecoilCallback(({ set }) => () => {
    JobsBackend.currentStatus().then((st) => set(currentStatus, st));
  });
  const loadTools = useRefreshToolReport();
  const loadPrograms = useRefreshProgramReport();
  const seenStoreEvents = React.useRef(false);
  React.useEffect(() => {
    load();
    loadTools();
    loadPrograms();

    if (reduxStore?.getState().Events.last30.cycles.part_cycles.isEmpty()) {
      // need to refresh tools and programs once the events load
      reduxStore?.subscribe(() => {
        if (!seenStoreEvents.current && !reduxStore?.getState().Events.last30.cycles.part_cycles.isEmpty()) {
          seenStoreEvents.current = true;
          loadTools();
          loadPrograms();
        }
      });
    }
  }, []);

  return null;
}
