import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
  Tooltip,
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
import { ApiException, CancelLoadRequest, IInProcessMaterial } from "../../network/api.js";
import { canCancelLoad } from "../../data/material-operation-policy.js";

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

  async function cancelLoad() {
    if (snapshot === null) return;
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
      setMaterialDialogOpen(null);
      onClose?.();
    } catch (e) {
      setError(
        ApiException.isApiException(e) ? e.response : e instanceof Error ? e.message : String(e),
      );
    } finally {
      setUpdating(false);
    }
  }

  return (
    <>
      {currentSnapshot ? (
        <Tooltip title="Cancel the displayed load instruction">
          <Button
            color="primary"
            disabled={updating}
            onClick={() => {
              setSnapshot(currentSnapshot);
              setReason("");
              setError(null);
              setOpen(true);
            }}
          >
            Cancel Load
          </Button>
        </Tooltip>
      ) : null}
      <Dialog open={open && snapshot !== null} onClose={() => setOpen(false)}>
        <DialogTitle>Cancel Load</DialogTitle>
        <DialogContent>
          <p>The displayed load instruction will be cancelled and recalculated for:</p>
          <ul>
            {snapshot?.group.map((candidate) => (
              <li key={candidate.materialID}>
                {candidate.serial || `Material ID ${candidate.materialID}`}
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
          <Button color="primary" disabled={updating} onClick={cancelLoad}>
            Cancel Load
          </Button>
          <Button color="secondary" disabled={updating} onClick={() => setOpen(false)}>
            Keep Load
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
