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

import { useRef, memo, useState, useCallback, useEffect, useMemo } from "react";
import {
  Button,
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
import { PrintedLabel, PrintMaterial } from "./PrintedLabel.js";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { JobsBackend } from "../../network/backend.js";
import { LogEntries } from "../LogEntry.js";
import { loadRawMaterialEvents } from "../../data/queue-material.js";
import {
  AddToQueueButton,
  NewMaterialToQueueType,
  AddToQueueMaterialDialogCt,
  useMaterialInDialogAddType,
} from "./QueuesAddMaterial.js";
import { QuarantineMatButton } from "./QuarantineButton.js";
import { useAtomValue } from "jotai";
import {
  InvalidateCycleDialogButton,
  InvalidateCycleDialogContent,
  InvalidateCycleState,
} from "./InvalidateCycle.js";

export function PrintOnClientButton({
  mat,
  materialName,
  operator,
}: {
  mat: PrintMaterial | ReadonlyArray<PrintMaterial>;
  materialName?: string | null;
  operator?: string | null;
}) {
  const printRef = useRef<HTMLDivElement>(null);
  const print = useReactToPrint({
    contentRef: printRef,
    ignoreGlobalStyles: true,
  });

  return (
    <>
      <Button color="primary" onClick={() => print()}>
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
  const fmsInfo = useAtomValue(fmsInformation);
  const curMat = useAtomValue(matDetails.inProcessMaterialInDialog);
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
          })
        }
      >
        Print Label
      </Button>
    );
  }
}

function QueuesDialogCt({
  toQueue,
  enteredOperator,
  setEnteredOperator,
  selectedQueue,
  setSelectedQueue,
  newMaterialTy,
  setNewMaterialTy,
  queueNames,
  invalidateSt,
  setInvalidateSt,
}: {
  toQueue: string | null;
  enteredOperator: string | null;
  setEnteredOperator: (operator: string | null) => void;
  selectedQueue: string | null;
  setSelectedQueue: (q: string | null) => void;
  newMaterialTy: NewMaterialToQueueType | null;
  setNewMaterialTy: (job: NewMaterialToQueueType | null) => void;
  queueNames: ReadonlyArray<string>;
  invalidateSt: InvalidateCycleState | null;
  setInvalidateSt: (st: InvalidateCycleState | null) => void;
}) {
  const kind = useMaterialInDialogAddType(queueNames);

  switch (kind) {
    case "None":
    case "MatInQueue":
      return null;
    case "AddToQueue":
      return (
        <>
          <AddToQueueMaterialDialogCt
            queueNames={queueNames}
            toQueue={toQueue}
            enteredOperator={enteredOperator}
            setEnteredOperator={setEnteredOperator}
            selectedQueue={selectedQueue}
            setSelectedQueue={setSelectedQueue}
            newMaterialTy={newMaterialTy}
            setNewMaterialTy={setNewMaterialTy}
          />
          {invalidateSt !== null ? (
            <InvalidateCycleDialogContent st={invalidateSt} setState={setInvalidateSt} />
          ) : undefined}
        </>
      );
  }
}

function QueueButtons({
  toQueue,
  enteredOperator,
  newMaterialTy,
  queueNames,
  onClose,
  invalidateSt,
  setInvalidateSt,
}: {
  toQueue: string | null;
  enteredOperator: string | null;
  newMaterialTy: NewMaterialToQueueType | null;
  queueNames: ReadonlyArray<string>;
  onClose: () => void;
  invalidateSt: InvalidateCycleState | null;
  setInvalidateSt: (st: InvalidateCycleState | null) => void;
}) {
  const kind = useMaterialInDialogAddType(queueNames);

  switch (kind) {
    case "None":
      return null;
    case "MatInQueue":
      return (
        <>
          <PrintLabelButton />
          <QuarantineMatButton onClose={onClose} />
        </>
      );
    case "AddToQueue":
      return (
        <>
          <InvalidateCycleDialogButton st={invalidateSt} setState={setInvalidateSt} onClose={onClose} />
          <AddToQueueButton
            newMaterialTy={newMaterialTy}
            toQueue={toQueue}
            enteredOperator={enteredOperator}
            onClose={onClose}
          />
        </>
      );
  }
}

export const QueuedMaterialDialog = memo(function QueuedMaterialDialog({
  queueNames,
}: {
  queueNames: ReadonlyArray<string>;
}) {
  const toShow = useAtomValue(matDetails.materialDialogOpen);
  const [selectedQueue, setSelectedQueue] = useState<string | null>(null);
  const [enteredOperator, setEnteredOperator] = useState<string | null>(null);
  const [newMaterialTy, setNewMaterialTy] = useState<NewMaterialToQueueType | null>(null);
  const [invalidateSt, setInvalidateSt] = useState<InvalidateCycleState | null>(null);

  let toQueue: string | null = null;
  if (toShow && toShow.type === "AddMatWithEnteredSerial") {
    toQueue = toShow.toQueue;
  } else if (queueNames.length === 1) {
    toQueue = queueNames[0];
  } else {
    toQueue = selectedQueue;
  }

  const onClose = useCallback(() => {
    setSelectedQueue(null);
    setEnteredOperator(null);
    setNewMaterialTy(null);
    setInvalidateSt(null);
  }, [setSelectedQueue, setEnteredOperator, setNewMaterialTy]);

  return (
    <MaterialDialog
      allowNote
      onClose={onClose}
      highlightProcess={invalidateSt?.process ?? undefined}
      extraDialogElements={
        <QueuesDialogCt
          toQueue={toQueue}
          enteredOperator={enteredOperator}
          setEnteredOperator={setEnteredOperator}
          selectedQueue={selectedQueue}
          setSelectedQueue={setSelectedQueue}
          newMaterialTy={newMaterialTy}
          setNewMaterialTy={setNewMaterialTy}
          queueNames={queueNames}
          invalidateSt={invalidateSt}
          setInvalidateSt={setInvalidateSt}
        />
      }
      buttons={
        <QueueButtons
          newMaterialTy={newMaterialTy}
          toQueue={toQueue}
          enteredOperator={enteredOperator}
          queueNames={queueNames}
          onClose={onClose}
          invalidateSt={invalidateSt}
          setInvalidateSt={setInvalidateSt}
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

export const MultiMaterialDialog = memo(function MultiMaterialDialog(props: MultiMaterialDialogProps) {
  const fmsInfo = useAtomValue(fmsInformation);
  const jobs = useAtomValue(currentStatus).jobs;
  const [printLabel, printingLabel] = matDetails.usePrintLabel();

  const [loading, setLoading] = useState(false);
  const [events, setEvents] = useState<ReadonlyArray<Readonly<api.ILogEntry>>>([]);
  const [showRemove, setShowRemove] = useState(false);
  const [removeCnt, setRemoveCnt] = useState<number>(NaN);
  const [lastOperator, setLastOperator] = useState<string | undefined>(undefined);

  useEffect(() => {
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
      .catch(console.error)
      .finally(() => setLoading(false));
    return () => {
      isSubscribed = false;
    };
  }, [props.material]);

  const rawMatName = useMemo(() => {
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
            .toRArray(),
        )
          .catch(console.error)
          .finally(close);
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
