/* Copyright (c) 2022, John Lenz

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
import {
  Button,
  Tooltip,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  CircularProgress,
  TextField,
} from "@mui/material";
import { useReactToPrint } from "react-to-print";

import { MaterialDetailTitle, MaterialDialog } from "./Material.js";
import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentOperator } from "../../data/operators.js";
import { PrintedLabel, PrintMaterial } from "./PrintedLabel.js";
import { useRecoilValue } from "recoil";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { JobsBackend } from "../../network/backend.js";
import { LogEntries } from "../LogEntry.js";
import { loadRawMaterialEvents } from "../../data/queue-material.js";
import {
  PromptForOperator,
  PromptForQueue,
  AddToQueueButton,
  AddNewJobProcessState,
  PromptForJob,
} from "./QueuesAddMaterial.js";

export function PrintOnClientButton({
  mat,
  materialName,
  operator,
}: {
  mat: PrintMaterial | ReadonlyArray<PrintMaterial>;
  materialName?: string | null;
  operator?: string | null;
}) {
  const printRef = React.useRef(null);
  const print = useReactToPrint({
    content: () => printRef.current,
    copyStyles: false,
  });

  return (
    <>
      <Button color="primary" onClick={print}>
        Print Label
      </Button>
      <div style={{ display: "none" }}>
        <div ref={printRef}>
          <PrintedLabel
            materialName={materialName}
            material={Array.isArray(mat) ? mat : [mat]}
            oneJobPerPage={false}
            operator={operator}
          />
        </div>
      </div>
    </>
  );
}

function PrintLabelButton() {
  const fmsInfo = useRecoilValue(fmsInformation);
  const curMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  const [printLabel, printingLabel] = matDetails.usePrintLabel();

  if (curMat === null || !fmsInfo.usingLabelPrinterForSerials) return null;

  if (fmsInfo.useClientPrinterForLabels) {
    return <PrintOnClientButton mat={curMat} />;
  } else {
    return (
      <Button
        color="primary"
        disabled={printingLabel}
        onClick={() =>
          printLabel({
            materialId: curMat.materialID,
            proc: curMat.process,
            loadStation: null,
            queue: curMat.location.currentQueue ?? null,
          })
        }
      >
        Print Label
      </Button>
    );
  }
}

function RemoveFromSystemButton({ onClose }: { onClose: () => void }) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const curMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  const [removeFromQueue, removingFromQueue] = matDetails.useRemoveFromQueue();
  const operator = useRecoilValue(currentOperator);
  const closeMatDialog = matDetails.useCloseMaterialDialog();

  if (curMat === null) return null;

  // first, check if can't remove because material is being loaded
  if (
    (fmsInfo.allowQuarantineAtLoadStation ?? false) === false &&
    curMat.action.type === api.ActionType.Loading
  ) {
    return null;
  } else if (curMat.location.type === api.LocType.OnPallet) {
    // can't remove if on a pallet
    return null;
  } else {
    // can't remove if there is a quarantine queue and material is in process
    if (fmsInfo.quarantineQueue && fmsInfo.quarantineQueue !== "" && curMat.process > 0) {
      return null;
    }
  }

  return (
    <Button
      color="primary"
      disabled={removingFromQueue}
      onClick={() => {
        removeFromQueue(curMat.materialID, operator);
        closeMatDialog();
        onClose();
      }}
    >
      Remove From System
    </Button>
  );
}

function QuarantineMaterialButton({ onClose }: { onClose: () => void }) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const curMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  const [addMat, addingMat] = matDetails.useAddExistingMaterialToQueue();
  const operator = useRecoilValue(currentOperator);
  const closeMatDialog = matDetails.useCloseMaterialDialog();

  if (curMat === null) return null;

  // can't quarantine if there is no queue defined
  const quarantineQueue = fmsInfo.quarantineQueue;
  if (!quarantineQueue || quarantineQueue === "") return null;

  // check if can't remove because material is being loaded
  if (
    (fmsInfo.allowQuarantineAtLoadStation ?? false) === false &&
    curMat.action.type === api.ActionType.Loading
  ) {
    return null;
  }

  // can't quarantine if on a pallet
  if (curMat.location.type === api.LocType.OnPallet) {
    return null;
  }

  return (
    <Tooltip title={"Move to " + quarantineQueue}>
      <Button
        color="primary"
        disabled={addingMat}
        onClick={() => {
          addMat({
            materialId: curMat.materialID,
            queue: quarantineQueue,
            queuePosition: 0,
            operator: operator,
          });
          closeMatDialog();
          onClose();
        }}
      >
        Quarantine Material
      </Button>
    </Tooltip>
  );
}

function QueueButtons({
  toQueue,
  enteredOperator,
  selectedJob,
  queueNames,
  onClose,
}: {
  toQueue: string | null;
  enteredOperator: string | null;
  selectedJob: AddNewJobProcessState | null;
  queueNames: ReadonlyArray<string>;
  onClose: () => void;
}) {
  const inProcMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  const curInQueueOnScreen =
    inProcMat !== null &&
    inProcMat.location.type === api.LocType.InQueue &&
    inProcMat.location.currentQueue &&
    queueNames.includes(inProcMat.location.currentQueue);

  if (curInQueueOnScreen) {
    return (
      <>
        <PrintLabelButton />
        <QuarantineMaterialButton onClose={onClose} />
        <RemoveFromSystemButton onClose={onClose} />
      </>
    );
  } else {
    return (
      <AddToQueueButton
        selectedJob={selectedJob}
        toQueue={toQueue}
        queueNames={queueNames}
        enteredOperator={enteredOperator}
        onClose={onClose}
      />
    );
  }
}

export const QueuedMaterialDialog = React.memo(function QueuedMaterialDialog({
  queueNames,
}: {
  queueNames: ReadonlyArray<string>;
}) {
  const toShow = useRecoilValue(matDetails.materialDialogOpen);
  const [selectedQueue, setSelectedQueue] = React.useState<string | null>(null);
  const [enteredOperator, setEnteredOperator] = React.useState<string | null>(null);
  const [selectedJob, setSelectedJob] = React.useState<AddNewJobProcessState | null>(null);

  let toQueue: string | null = null;
  let requireSelectQueue = false;
  if (toShow && toShow.type === "AddMatWithoutSerial") {
    toQueue = toShow.toQueue;
  } else if (toShow && toShow.type === "AddMatWithEnteredSerial") {
    toQueue = toShow.toQueue;
  } else if (queueNames.length === 1) {
    toQueue = queueNames[0];
  } else {
    requireSelectQueue = true;
    toQueue = selectedQueue;
  }

  const onClose = React.useCallback(() => {
    setSelectedQueue(null);
    setEnteredOperator(null);
    setSelectedJob(null);
  }, [setSelectedQueue, setEnteredOperator, setSelectedJob]);

  return (
    <MaterialDialog
      allowNote
      onClose={onClose}
      extraDialogElements={
        <>
          {requireSelectQueue ? (
            <PromptForQueue
              selectedQueue={selectedQueue}
              setSelectedQueue={setSelectedQueue}
              queueNames={queueNames}
            />
          ) : undefined}
          <PromptForJob
            selectedJob={selectedJob}
            setSelectedJob={setSelectedJob}
            toQueue={toQueue}
            queueNames={queueNames}
          />
          <PromptForOperator
            enteredOperator={enteredOperator}
            setEnteredOperator={setEnteredOperator}
            queueNames={queueNames}
          />
        </>
      }
      buttons={
        <QueueButtons
          selectedJob={selectedJob}
          toQueue={toQueue}
          enteredOperator={enteredOperator}
          queueNames={queueNames}
          onClose={onClose}
        />
      }
    />
  );
});

export interface MultiMaterialDialogProps {
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>> | null;
  readonly closeDialog: () => void;
  readonly operator: string | null;
}

export const MultiMaterialDialog = React.memo(function MultiMaterialDialog(props: MultiMaterialDialogProps) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const jobs = useRecoilValue(currentStatus).jobs;
  const [printLabel, printingLabel] = matDetails.usePrintLabel();

  const [loading, setLoading] = React.useState(false);
  const [events, setEvents] = React.useState<ReadonlyArray<Readonly<api.ILogEntry>>>([]);
  const [showRemove, setShowRemove] = React.useState(false);
  const [removeCnt, setRemoveCnt] = React.useState<number>(NaN);
  const [lastOperator, setLastOperator] = React.useState<string | undefined>(undefined);

  React.useEffect(() => {
    if (props.material === null) return;
    let isSubscribed = true;
    setLoading(true);
    loadRawMaterialEvents(props.material)
      .then((events) => {
        if (isSubscribed) {
          setEvents(events);
          let operator: string | undefined;
          for (const e of events) {
            if (e.type === api.LogType.AddToQueue && e.details?.["operator"] !== undefined) {
              operator = e.details["operator"];
            }
          }
          setLastOperator(operator);
        }
      })
      .finally(() => setLoading(false));
    return () => {
      isSubscribed = false;
    };
  }, [props.material]);

  const rawMatName = React.useMemo(() => {
    if (!props.material || props.material.length === 0) return undefined;
    const uniq = props.material[0].jobUnique;
    if (!uniq || uniq === "" || !jobs[uniq]) return undefined;
    return LazySeq.of(jobs[uniq].procsAndPaths[0].paths)
      .filter((p) => p.casting !== undefined && p.casting !== "")
      .head()?.casting;
  }, [props.material, jobs]);

  function close() {
    props.closeDialog();
    setShowRemove(false);
    setRemoveCnt(NaN);
    setLoading(false);
    setEvents([]);
  }

  function remove() {
    if (showRemove) {
      if (!isNaN(removeCnt)) {
        setLoading(true);
        JobsBackend.bulkRemoveMaterialFromQueues(
          props.operator,
          LazySeq.of(props.material || [])
            .take(removeCnt)
            .map((m) => m.materialID)
            .toRArray()
        ).finally(close);
      }
    } else {
      setShowRemove(true);
    }
  }

  const mat1 = props.material?.[0];
  return (
    <Dialog open={props.material !== null} onClose={close} maxWidth="md">
      <DialogTitle>
        {mat1 && props.material && props.material.length > 0 ? (
          <MaterialDetailTitle
            partName={mat1.partName}
            subtitle={
              props.material.length.toString() +
              (mat1.jobUnique && mat1.jobUnique !== "" ? " assigned to " + mat1.jobUnique : " unassigned")
            }
          />
        ) : (
          "Material"
        )}
      </DialogTitle>
      <DialogContent>
        {loading ? <CircularProgress color="secondary" /> : <LogEntries entries={events} copyToClipboard />}
        {showRemove && props.material ? (
          <div style={{ marginTop: "1em" }}>
            <TextField
              type="number"
              variant="outlined"
              fullWidth
              label="Quantity to Remove"
              inputProps={{ min: "1", max: props.material.length.toString() }}
              value={isNaN(removeCnt) ? "" : removeCnt}
              onChange={(e) => setRemoveCnt(parseInt(e.target.value))}
            />
          </div>
        ) : undefined}
      </DialogContent>
      <DialogActions>
        {props.material && props.material.length > 0 && fmsInfo.usingLabelPrinterForSerials ? (
          fmsInfo.useClientPrinterForLabels ? (
            <PrintOnClientButton
              mat={props.material || []}
              materialName={rawMatName}
              operator={lastOperator}
            />
          ) : (
            <Button
              color="primary"
              disabled={printingLabel}
              onClick={() =>
                props.material && props.material.length > 0
                  ? printLabel({
                      materialId: props.material[0].materialID,
                      proc: 0,
                      loadStation: null,
                      queue: props.material[0].location.currentQueue || null,
                    })
                  : void 0
              }
            >
              Print Label
            </Button>
          )
        ) : undefined}
        <Button color="primary" onClick={remove} disabled={loading || (showRemove && isNaN(removeCnt))}>
          {loading && showRemove
            ? "Removing..."
            : showRemove && !isNaN(removeCnt)
            ? `Remove ${removeCnt} material`
            : "Remove Material"}
        </Button>
        <Button color="primary" onClick={close}>
          Close
        </Button>
      </DialogActions>
    </Dialog>
  );
});
