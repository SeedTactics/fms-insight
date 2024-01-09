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

import * as React from "react";
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField, Tooltip } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentStatus } from "../../cell-status/current-status.js";
import {
  inProcessMaterialInDialog,
  materialDialogOpen,
  useRemoveFromQueue,
  useSignalForQuarantine,
} from "../../cell-status/material-details.js";
import { currentOperator } from "../../data/operators.js";
import { ActionType, LocType, PalletLocationEnum, QueueRole } from "../../network/api.js";
import { fmsInformation } from "../../network/server-settings.js";
import { useAtomValue, useSetAtom } from "jotai";

type QuarantineMaterialTypes = "Remove" | "Scrap" | "SignalForScrap" | "CancelLoad";

type QuarantineMaterialData = {
  readonly type: QuarantineMaterialTypes;
  readonly quarantine: (reason: string) => void;
  readonly removing: boolean;
  readonly quarantineQueueDestination: string | null;
};

function useQuarantineMaterial(ignoreOperator: boolean): QuarantineMaterialData | null {
  const fmsInfo = useAtomValue(fmsInformation);
  const [removeFromQueue, removingFromQueue] = useRemoveFromQueue();
  const [signalQuarantine, signalingQuarantine] = useSignalForQuarantine();
  const inProcMat = useAtomValue(inProcessMaterialInDialog);
  const curSt = useAtomValue(currentStatus);
  let operator = useAtomValue(currentOperator);
  if (ignoreOperator) operator = null;

  if (inProcMat === null || inProcMat.materialID < 0) return null;

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
          ([, info]) => info.role === QueueRole.RawMaterial || info.role === QueueRole.InProcessTransfer,
        )
        .map(([qname, _]) => qname),
    )
    .toRSet((x) => x);
  const quarantineQueues = LazySeq.ofObject(curSt.queues)
    .filter(([qname, _]) => !activeQueues.has(qname))
    .toRSet(([qname, _]) => qname);
  const palLoc = LazySeq.ofObject(curSt.pallets).toOrderedMap(([_, pal]) => [
    pal.palletNum,
    pal.currentPalletLocation,
  ]);

  // If in a quarantine queue, allow removal from system
  if (
    inProcMat.location.type === LocType.InQueue &&
    inProcMat.location.currentQueue &&
    quarantineQueues.has(inProcMat.location.currentQueue)
  ) {
    return {
      type: "Remove",
      quarantine: () => removeFromQueue(inProcMat.materialID, operator),
      removing: removingFromQueue,
      quarantineQueueDestination: null,
    };
  }

  let type: QuarantineMaterialTypes | null = null;

  switch (inProcMat.location.type) {
    case LocType.OnPallet:
      if (fmsInfo.allowQuarantineToCancelLoad) {
        // either cancel load or signal based on material action and pallet location
        const palAtLoad =
          inProcMat.location.palletNum &&
          palLoc.get(inProcMat.location.palletNum)?.loc === PalletLocationEnum.LoadUnload;
        if (inProcMat.action.type === ActionType.Loading || palAtLoad) {
          type = "CancelLoad";
        } else {
          type = "SignalForScrap";
        }
      } else {
        // must signal since it is currently on a pallet, but only if the material is not loading
        if (inProcMat.action.type === ActionType.Loading) {
          return null;
        }
        // Check that the job outputs to a queue, only then can signaling for quarantine work
        const job = curSt.jobs[inProcMat.jobUnique];
        if (!job) return null;
        const path = job.procsAndPaths?.[inProcMat.process - 1]?.paths?.[inProcMat.path - 1];
        if (inProcMat.process != job.procsAndPaths.length && (!path || !path.outputQueue)) {
          return null;
        }
        type = "SignalForScrap";
      }

      break;

    case LocType.InQueue:
      if (inProcMat.action.type === ActionType.Loading && !fmsInfo.allowQuarantineToCancelLoad) {
        return null;
      } else if (inProcMat.action.type === ActionType.Loading) {
        type = "CancelLoad";
      } else {
        type = "Scrap";
      }
      break;

    case LocType.Free:
      type = "Scrap";
      break;
  }

  if (type) {
    return {
      type,
      quarantine: (reason) => signalQuarantine(inProcMat.materialID, operator, reason),
      removing: signalingQuarantine,
      quarantineQueueDestination: quarantineQueue,
    };
  } else {
    return null;
  }
}

export function QuarantineMatButton({
  onClose,
  ignoreOperator,
}: {
  onClose?: () => void;
  ignoreOperator?: boolean;
}) {
  const [open, setOpen] = React.useState(false);
  const [reason, setReason] = React.useState("");
  const q = useQuarantineMaterial(!!ignoreOperator);
  const setMatToShow = useSetAtom(materialDialogOpen);

  if (q === null) return null;

  let title: string;
  let btnTxt: string;

  switch (q.type) {
    case "Remove":
      title = "Remove from system";
      btnTxt = "Remove";
      break;
    case "Scrap":
      title = q.quarantineQueueDestination ? `Move to ${q.quarantineQueueDestination}` : "Remove as scrap";
      btnTxt = q.quarantineQueueDestination ? "Quarantine" : "Scrap";
      break;
    case "SignalForScrap":
      title = q.quarantineQueueDestination
        ? `After unload, move to ${q.quarantineQueueDestination}`
        : "After unload, remove the part as scrap";
      btnTxt = q.quarantineQueueDestination ? "Quarantine" : "Scrap";
      break;

    case "CancelLoad":
      title = q.quarantineQueueDestination
        ? `Cancel load and move to ${q.quarantineQueueDestination}`
        : "Cancel load";
      btnTxt = "Cancel Load";
      break;
  }

  function quarantine() {
    q?.quarantine(reason);
    setMatToShow(null);
    setOpen(false);
    setReason("");
    onClose?.();
  }

  return (
    <>
      <Tooltip title={title}>
        <Button color="primary" disabled={q.removing} onClick={() => setOpen(true)}>
          {btnTxt}
        </Button>
      </Tooltip>
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
        </DialogContent>
        <DialogActions>
          <Button color="primary" onClick={quarantine}>
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
