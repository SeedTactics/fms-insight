/* Copyright (c) 2024, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

import { useState } from "react";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
  Tooltip,
} from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentStatus } from "../../cell-status/current-status.js";
import {
  inProcessMaterialInDialog,
  materialDialogOpen,
  useQuarantineQueuedMaterial,
  useRemoveFromQueue,
  useSignalForQuarantine,
} from "../../cell-status/material-details.js";
import { currentOperator } from "../../data/operators.js";
import {
  ApiException,
  ICurrentStatus,
  IInProcessMaterial,
  LocType,
  QueueRole,
} from "../../network/api.js";
import { fmsInformation } from "../../network/server-settings.js";
import { useAtomValue, useSetAtom } from "jotai";
import {
  canDirectlyQuarantine,
  canRemoveFromQueue,
  canSignalQuarantine,
  isActiveLoadStationOperation,
} from "../../data/material-operation-policy.js";

type QuarantineMaterialTypes = "Remove" | "Scrap" | "SignalForScrap";

type QuarantineMaterialData = {
  readonly type: QuarantineMaterialTypes;
  readonly material: Readonly<IInProcessMaterial>;
  readonly quarantine: (reason: string) => Promise<void>;
  readonly removing: boolean;
  readonly quarantineQueueDestination: string | null;
  readonly removeFromQueues: (() => Promise<void>) | null;
  readonly removingFromQueues: boolean;
};

function hasSupportedQuarantineExit(
  material: Readonly<IInProcessMaterial>,
  status: Readonly<ICurrentStatus>,
): boolean {
  const job = status.jobs[material.jobUnique];
  if (!job) return false;

  const path = job.procsAndPaths[material.process - 1]?.paths[material.path - 1];
  if (!path) return false;

  return material.process === job.procsAndPaths.length || Boolean(path.outputQueue);
}

function useQuarantineMaterial(ignoreOperator: boolean): QuarantineMaterialData | null {
  const fmsInfo = useAtomValue(fmsInformation);
  const [removeFromQueue, removingFromQueue] = useRemoveFromQueue();
  const [signalQuarantine, signalingQuarantine] = useSignalForQuarantine();
  const [quarantineQueued, quarantiningQueued] = useQuarantineQueuedMaterial();
  const inProcMat = useAtomValue(inProcessMaterialInDialog);
  const curSt = useAtomValue(currentStatus);
  let operator = useAtomValue(currentOperator);
  if (ignoreOperator) operator = null;

  if (inProcMat === null || inProcMat.materialID < 0) return null;

  if (isActiveLoadStationOperation(inProcMat)) return null;

  if (inProcMat.quarantineAfterUnload) return null;

  const quarantineQueue = fmsInfo.quarantineQueue?.length ? fmsInfo.quarantineQueue : null;

  const activeQueues = LazySeq.ofObject(curSt.jobs)
    .flatMap(([_, job]) => job.procsAndPaths)
    .flatMap((proc) => proc.paths)
    .flatMap((path) => {
      const q: string[] = [];
      if (path.inputQueue !== undefined) q.push(path.inputQueue);
      if (path.outputQueue !== undefined) q.push(path.outputQueue);
      return q;
    })
    .concat(
      LazySeq.ofObject(curSt.queues)
        .filter(
          ([, info]) =>
            info.role === QueueRole.RawMaterial || info.role === QueueRole.InProcessTransfer,
        )
        .map(([qname, _]) => qname),
    )
    .toRSet((x) => x);
  const quarantineQueues = LazySeq.ofObject(curSt.queues)
    .filter(([qname, _]) => !activeQueues.has(qname))
    .toRSet(([qname, _]) => qname);
  // If in a quarantine queue, allow removal from system
  if (
    inProcMat.location.type === LocType.InQueue &&
    inProcMat.location.currentQueue &&
    quarantineQueues.has(inProcMat.location.currentQueue)
  ) {
    return {
      type: "Remove",
      material: inProcMat,
      quarantine: () => removeFromQueue(inProcMat.materialID, operator),
      removing: removingFromQueue,
      quarantineQueueDestination: null,
      removeFromQueues: null,
      removingFromQueues: removingFromQueue,
    };
  }

  let type: QuarantineMaterialTypes | null = null;

  switch (inProcMat.location.type) {
    case LocType.OnPallet:
    case LocType.InBasket:
      if (!canSignalQuarantine(inProcMat) || !hasSupportedQuarantineExit(inProcMat, curSt)) {
        return null;
      }
      type = "SignalForScrap";
      break;

    case LocType.InQueue:
      if (!canDirectlyQuarantine(inProcMat)) return null;
      if (
        inProcMat.location.currentQueue === undefined ||
        quarantineQueues.has(inProcMat.location.currentQueue)
      ) {
        return null;
      }
      type = "Scrap";
      break;

    case LocType.Free:
      return null;
  }

  if (type) {
    return {
      type,
      material: inProcMat,
      quarantine:
        type === "SignalForScrap"
          ? async (reason) => signalQuarantine(inProcMat.materialID, operator, reason)
          : async (reason) => quarantineQueued(inProcMat.materialID, operator, reason),
      removing: type === "SignalForScrap" ? signalingQuarantine : quarantiningQueued,
      quarantineQueueDestination: quarantineQueue,
      removeFromQueues:
        inProcMat.location.type === LocType.InQueue &&
        inProcMat.location.currentQueue !== undefined &&
        !quarantineQueues.has(inProcMat.location.currentQueue) &&
        canRemoveFromQueue(inProcMat)
          ? () => removeFromQueue(inProcMat.materialID, operator)
          : null,
      removingFromQueues: removingFromQueue,
    };
  } else {
    return null;
  }
}

function RemoveFromQueuesButton({
  material,
  removeFromQueues,
  removing,
}: {
  readonly material: Readonly<IInProcessMaterial>;
  readonly removeFromQueues: () => Promise<void>;
  readonly removing: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const setMatToShow = useSetAtom(materialDialogOpen);

  async function remove() {
    setError(null);
    try {
      await removeFromQueues();
      setOpen(false);
      setMatToShow({
        type: "MatSummary",
        summary: {
          materialID: material.materialID,
          partName: material.partName,
          serial: material.serial,
        },
      });
    } catch (e) {
      setError(
        ApiException.isApiException(e) ? e.response : e instanceof Error ? e.message : String(e),
      );
    }
  }

  function openRemoval() {
    setError(null);
    setOpen(true);
  }

  return (
    <>
      <Tooltip title="Remove from the current queue so it can be rescanned">
        <Button color="primary" disabled={removing} onClick={openRemoval}>
          Remove from Queue
        </Button>
      </Tooltip>
      <Dialog open={open} onClose={() => setOpen(false)}>
        <DialogTitle>Remove Material from Queue</DialogTitle>
        <DialogContent>
          <p>
            Remove this material from its current queue. Its production history will remain
            available. You can scan it again to correct its history or assignment and then add it
            back to a queue.
          </p>
          {error ? <p role="alert">{error}</p> : null}
        </DialogContent>
        <DialogActions>
          <Button color="primary" disabled={removing} onClick={remove}>
            Remove from Queue
          </Button>
          <Button color="secondary" disabled={removing} onClick={() => setOpen(false)}>
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

export function QuarantineMatButton({
  onClose,
  ignoreOperator,
}: {
  onClose?: () => void;
  ignoreOperator?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const q = useQuarantineMaterial(!!ignoreOperator);
  const setMatToShow = useSetAtom(materialDialogOpen);

  function openQuarantine() {
    setError(null);
    setReason("");
    setOpen(true);
  }

  if (q === null) return null;

  let title: string;
  let btnTxt: string;

  switch (q.type) {
    case "Remove":
      title = "Remove from system";
      btnTxt = "Remove";
      break;
    case "Scrap":
      title = q.quarantineQueueDestination
        ? `Move to ${q.quarantineQueueDestination}`
        : "Remove from queue and treat as scrap";
      btnTxt = q.quarantineQueueDestination ? "Quarantine" : "Scrap";
      break;
    case "SignalForScrap":
      title = q.quarantineQueueDestination
        ? `The current automated operation will continue. When the material leaves automation control, move it to ${q.quarantineQueueDestination}`
        : "The current automated operation will continue. When the material leaves automation control, remove it from normal production flow as scrap";
      btnTxt = q.quarantineQueueDestination ? "Quarantine" : "Scrap";
      break;
  }

  async function quarantine() {
    if (!q) return;
    setError(null);
    try {
      await q.quarantine(reason);
      setOpen(false);
      setReason("");
      if (q.type === "Remove") {
        setMatToShow({
          type: "MatSummary",
          summary: {
            materialID: q.material.materialID,
            partName: q.material.partName,
            serial: q.material.serial,
          },
        });
      } else {
        setMatToShow(null);
        onClose?.();
      }
    } catch (e) {
      setError(
        ApiException.isApiException(e) ? e.response : e instanceof Error ? e.message : String(e),
      );
    }
  }

  return (
    <>
      <Tooltip title={title}>
        <Button color="primary" disabled={q.removing} onClick={openQuarantine}>
          {btnTxt}
        </Button>
      </Tooltip>
      {q.removeFromQueues ? (
        <RemoveFromQueuesButton
          material={q.material}
          removeFromQueues={q.removeFromQueues}
          removing={q.removingFromQueues}
        />
      ) : null}
      <Dialog open={open} onClose={() => setOpen(false)}>
        <DialogTitle>Quarantine Material</DialogTitle>
        <DialogContent>
          <p>{title}</p>
          <TextField
            label="Reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            fullWidth
            autoFocus
            multiline
          />
          {error ? <p role="alert">{error}</p> : null}
        </DialogContent>
        <DialogActions>
          <Button color="primary" disabled={q.removing} onClick={quarantine}>
            {btnTxt}
          </Button>
          <Button color="secondary" onClick={() => setOpen(false)}>
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
