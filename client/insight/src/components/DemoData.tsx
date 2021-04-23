import * as React from "react";
import { useRecoilCallback } from "recoil";
import { JobsBackend } from "../data/backend";
import { currentStatus } from "../data/current-status";
import { programReportRefreshTime, toolReportRefreshTime } from "../data/tools-programs";

export function LoadDemoData() {
  const load = useRecoilCallback(({ set }) => () => {
    JobsBackend.currentStatus().then((st) => set(currentStatus, st));
    set(toolReportRefreshTime, new Date());
    set(programReportRefreshTime, new Date());
  });
  React.useEffect(() => {
    load();
  }, []);

  return null;
}
