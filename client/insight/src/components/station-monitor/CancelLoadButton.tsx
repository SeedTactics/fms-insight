import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
} from "@mui/material";
import { useAtomValue, useSetAtom } from "jotai";
import { useState } from "react";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentStatus } from "../../cell-status/current-status.js";
import {
  inProcessMaterialInDialog,
  materialDialogOpen,
} from "../../cell-status/material-details.js";
import { currentOperator } from "../../data/operators.js";
import { JobsBackend } from "../../network/backend.js";
import { CancelLoadRequest, IInProcessMaterial } from "../../network/api.js";
import { apiErrorMessage } from "../../network/api-errors.js";
import { canCancelLoad } from "../../data/material-operation-policy.js";
import { alarmsBlockingMaterialChanges } from "../../cell-status/alarms.js";
import { SystemAlarmTooltip } from "./SystemAlarmTooltip.js";

type CancelLoadSnapshot = {
  readonly material: Readonly<IInProcessMaterial>;
  readonly cancellationId: string;
  readonly group: ReadonlyArray<Readonly<IInProcessMaterial>>;
};

export function CancelLoadButton({
  onClose,
  ignoreOperator = false,
}: {
  readonly onClose?: () => void;
  readonly ignoreOperator?: boolean;
}) {
  const material = useAtomValue(inProcessMaterialInDialog);
  const status = useAtomValue(currentStatus);
  const operator = useAtomValue(currentOperator);
  const setMaterialDialogOpen = useSetAtom(materialDialogOpen);
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [updating, setUpdating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [snapshot, setSnapshot] = useState<CancelLoadSnapshot | null>(null);

  const cancellationId = material?.action.loadCancellationId ?? null;
  const currentSnapshot: CancelLoadSnapshot | null =
    material === null || cancellationId === null || !canCancelLoad(material)
      ? null
      : {
          material,
          cancellationId,
          group: LazySeq.of(status.material)
            .filter((candidate) => candidate.action.loadCancellationId === cancellationId)
            .toRArray(),
        };

  if (currentSnapshot === null && snapshot === null) return null;

  const currentAlarms = alarmsBlockingMaterialChanges(status, currentSnapshot?.group ?? []);
  const confirmationAlarms = alarmsBlockingMaterialChanges(status, snapshot?.group ?? []);

  async function cancelLoad() {
    if (snapshot === null || confirmationAlarms.length > 0) return;
    setUpdating(true);
    setError(null);
    try {
      await JobsBackend.cancelLoad(
        snapshot.material.materialID,
        ignoreOperator ? null : operator,
        new CancelLoadRequest({
          expectedLoadCancellationId: snapshot.cancellationId,
          reason: reason || undefined,
        }),
      );
      setOpen(false);
      setSnapshot(null);
      setMaterialDialogOpen(null);
      onClose?.();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setUpdating(false);
    }
  }

  return (
    <>
      {currentSnapshot ? (
        <SystemAlarmTooltip
          suspended={currentAlarms.length > 0}
          title="Cancel the displayed load instruction"
        >
          <Button
            color="primary"
            disabled={updating || currentAlarms.length > 0}
            onClick={() => {
              setSnapshot(currentSnapshot);
              setReason("");
              setError(null);
              setOpen(true);
            }}
          >
            Cancel Load
          </Button>
        </SystemAlarmTooltip>
      ) : null}
      <Dialog open={open && snapshot !== null} onClose={() => setOpen(false)}>
        <DialogTitle>Cancel Load</DialogTitle>
        <DialogContent>
          <p>The displayed load instruction will be cancelled and recalculated for:</p>
          <ul>
            {snapshot?.group.map((candidate, index) => (
              <li key={candidate.materialID >= 0 ? candidate.materialID : `raw-${index}`}>
                {candidate.materialID < 0
                  ? `${candidate.partName} raw material`
                  : candidate.serial || `Material ID ${candidate.materialID}`}
              </li>
            ))}
          </ul>
          <TextField
            label="Reason (optional)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            fullWidth
            multiline
          />
          {error ? <p role="alert">{error}</p> : null}
        </DialogContent>
        <DialogActions>
          <SystemAlarmTooltip suspended={confirmationAlarms.length > 0}>
            <Button
              color="primary"
              disabled={updating || confirmationAlarms.length > 0}
              onClick={cancelLoad}
            >
              Cancel Load
            </Button>
          </SystemAlarmTooltip>
          <Button color="secondary" disabled={updating} onClick={() => setOpen(false)}>
            Keep Load
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
