import * as React from "react";
import { useRecoilCallback } from "recoil";
import { JobsBackend } from "../data/backend";
import { currentStatus } from "../data/current-status";

export function LoadDemoData() {
  const load = useRecoilCallback(({ set }) => () => {
    JobsBackend.currentStatus().then((st) => set(currentStatus, st));
  });
  React.useEffect(() => {
    load();
  }, []);

  return null;
}
