/* Copyright (c) 2023, John Lenz

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
import { Button, Tooltip } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";
import { useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status.js";
import {
  inProcessMaterialInDialog,
  useAddExistingMaterialToQueue,
  useCloseMaterialDialog,
  useRemoveFromQueue,
  useSignalForQuarantine,
} from "../../cell-status/material-details.js";
import { currentOperator } from "../../data/operators.js";
import { ActionType, LocType } from "../../network/api.js";
import { fmsInformation } from "../../network/server-settings.js";

type QuarantineMaterialData =
  | {
      readonly type: "Remove";
      readonly quarantine: () => void;
      readonly removing: boolean;
    }
  | {
      readonly type: "Quarantine" | "Signal";
      readonly quarantine: () => void;
      readonly removing: boolean;
      readonly quarantineQueueDestination: string;
    };

function useQuarantineMaterial(ignoreOperator: boolean): QuarantineMaterialData | null {
  const fmsInfo = useRecoilValue(fmsInformation);
  const [addToQueue, addingToQueue] = useAddExistingMaterialToQueue();
  const [removeFromQueue, removingFromQueue] = useRemoveFromQueue();
  const [signalQuarantine, signalingQuarantine] = useSignalForQuarantine();
  const inProcMat = useRecoilValue(inProcessMaterialInDialog);
  const curSt = useRecoilValue(currentStatus);
  let operator = useRecoilValue(currentOperator);
  if (ignoreOperator) operator = null;

  if (inProcMat === null) return null;

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
    .toRSet((x) => x);
  const quarantineQueues = LazySeq.ofObject(curSt.queues)
    .filter(([qname, _]) => !activeQueues.has(qname))
    .toRSet(([qname, _]) => qname);

  if (quarantineQueue !== null && inProcMat.location.type === LocType.OnPallet) {
    return {
      quarantine: () => signalQuarantine(inProcMat.materialID, quarantineQueue, operator),
      removing: signalingQuarantine,
      type: "Signal",
      quarantineQueueDestination: quarantineQueue,
    };
  }

  if (
    // in a queue
    inProcMat.location.type === LocType.InQueue &&
    inProcMat.location.currentQueue &&
    // and either the FMS supports quarantining loading material or the material is not loading
    (fmsInfo.supportsQuarantineAtLoadStation || inProcMat.action.type !== ActionType.Loading)
  ) {
    if (
      quarantineQueues.has(inProcMat.location.currentQueue) ||
      quarantineQueue === null ||
      inProcMat.process === 0
    ) {
      return {
        type: "Remove",
        quarantine: () => removeFromQueue(inProcMat.materialID, operator),
        removing: removingFromQueue,
      };
    } else {
      return {
        quarantine: () =>
          addToQueue({
            materialId: inProcMat.materialID,
            queue: quarantineQueue,
            queuePosition: 0,
            operator,
          }),
        removing: addingToQueue,
        type: "Quarantine",
        quarantineQueueDestination: quarantineQueue,
      };
    }
  }

  return null;
}

export function QuarantineMatButton({
  onClose,
  ignoreOperator,
}: {
  onClose?: () => void;
  ignoreOperator?: boolean;
}) {
  const q = useQuarantineMaterial(!!ignoreOperator);
  const closeMatDialog = useCloseMaterialDialog();

  if (q === null) return null;

  let title: string;
  let btnTxt: string;

  switch (q.type) {
    case "Remove":
      title = "Remove from system";
      btnTxt = "Remove";
      break;
    case "Quarantine":
      title = `Move to ${q.quarantineQueueDestination}`;
      btnTxt = "Quarantine";
      break;
    case "Signal":
      title = `After unload, move to ${q.quarantineQueueDestination}`;
      btnTxt = "Quarantine";
  }

  return (
    <Tooltip title={title}>
      <Button
        color="primary"
        disabled={q.removing}
        onClick={() => {
          q.quarantine();
          closeMatDialog();
          onClose?.();
        }}
      >
        {btnTxt}
      </Button>
    </Tooltip>
  );
}
