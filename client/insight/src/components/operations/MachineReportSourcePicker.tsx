import { Alert, Box } from "@mui/material";
import { useAtomValue, useSetAtom } from "jotai";
import {
  machineReportHistoryGroups,
  machineReportSource,
  machineReportSources,
  selectedMachineReportSource,
} from "../../data/machine-report-sources.js";
import { useIsDemo } from "../routes.js";

export function MachineReportSourcePicker() {
  const source = useAtomValue(machineReportSource);
  const sources = useAtomValue(machineReportSources);
  const historyGroups = useAtomValue(machineReportHistoryGroups);
  const select = useSetAtom(selectedMachineReportSource);
  const demo = useIsDemo();

  if (demo || source === null || sources.length < 2) return null;
  const sharedHistory = source.machines.some((machines) => !historyGroups?.has(machines.group));
  return (
    <Box sx={{ mb: 1 }}>
      <label>
        Cell{" "}
        <select value={source.id} onChange={(event) => select(event.target.value)}>
          {sources.map((option) => (
            <option key={option.id} value={option.id}>
              {option.label}
            </option>
          ))}
        </select>
      </label>
      {sharedHistory && (
        <Alert severity="info" sx={{ mt: 1 }}>
          Cycle times and tool usage history are unavailable for machine groups shared by multiple
          cells.
        </Alert>
      )}
    </Box>
  );
}
