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

import { useState, memo, useRef, useEffect, useMemo } from "react";
import {
  Button,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  ListItemText,
  ListItemIcon,
  MenuItem,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  CircularProgress,
  TextField,
} from "@mui/material";
import { useReactToPrint } from "react-to-print";

import { MaterialDetailTitle, PartIdenticon } from "./Material.js";
import * as api from "../../network/api.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { extractJobRawMaterial, loadRawMaterialEvents } from "../../data/queue-material.js";
import { currentOperator } from "../../data/operators.js";
import { PrintedLabel, PrintOnClientButton } from "./PrintedLabel.js";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { useAddNewCastingToQueue, usePrintLabel } from "../../cell-status/material-details.js";
import { castingNames } from "../../cell-status/names.js";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";
import { JobsBackend } from "../../network/backend.js";
import { LogEntries } from "../LogEntry.js";

export const bulkAddCastingToQueue = atom<string | null>(null);

type WaitingForMaterialAssignment = {
  readonly selectedCasting: string;
  readonly operator: string | null;
  readonly materialIds: ReadonlySet<number>;
};

const waitingForMaterialAssignmentDialog = atom<WaitingForMaterialAssignment | null>(null);

function WaitingForMaterialDialog() {
  const [waiting, setWaiting] = useAtom(waitingForMaterialAssignmentDialog);
  const st = useAtomValue(currentStatus);
  const printRef = useRef<HTMLDivElement>(null);
  const printStarted = useRef<boolean>(false);
  const print = useReactToPrint({
    contentRef: printRef,
    onAfterPrint: () => setWaiting(null),
    ignoreGlobalStyles: true,
  });

  let materialToPrint = waiting ? st.material.filter((m) => waiting.materialIds.has(m.materialID)) : null;
  if (materialToPrint?.length !== waiting?.materialIds.size) {
    materialToPrint = null;
  }

  useEffect(() => {
    if (waiting === null) {
      if (printStarted.current) {
        printStarted.current = false;
      }
      return;
    }

    if (printStarted.current || materialToPrint === null) return;
    print();
    printStarted.current = true;
  }, [waiting, materialToPrint, printStarted, print]);

  console.log(waiting, materialToPrint);

  return (
    <>
      <Dialog open={waiting !== null}>
        <DialogTitle>Waiting for Material Assignment</DialogTitle>
        <DialogContent>
          {materialToPrint === null ? (
            waiting === null ? (
              <div />
            ) : (
              <Stack direction="column" spacing={2} mt="0.5em" mb="0.5em">
                <CircularProgress color="secondary" />
                <div>
                  Waiting for material assignment. This could take a while if a download is currently in
                  progress.
                </div>
              </Stack>
            )
          ) : (
            <div>Printing...</div>
          )}
        </DialogContent>
        <DialogActions>
          <Button color="primary" onClick={() => setWaiting(null)}>
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
      <div style={{ display: "none" }}>
        <div ref={printRef}>
          <PrintedLabel
            materialName={waiting?.selectedCasting}
            material={materialToPrint}
            operator={waiting?.operator}
            oneJobPerPage={true}
          />
        </div>
      </div>
    </>
  );
}

function JobsForCasting({ casting, queue }: { casting: string; queue: string }) {
  const currentSt = useAtomValue(currentStatus);

  const jobs = useMemo(
    () =>
      extractJobRawMaterial(currentSt.jobs, currentSt.material, queue).filter(
        (j) => j.rawMatName === casting,
      ),
    [currentSt.jobs, currentSt.material, casting, queue],
  );

  if (jobs.length === 0) return null;

  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          <TableCell>Job</TableCell>
          <TableCell align="right">Plan</TableCell>
          <TableCell align="right">Remaining</TableCell>
          <TableCell align="right">Assigned</TableCell>
          <TableCell align="right">Required</TableCell>
          <TableCell align="right">Available</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {jobs.map((j, idx) => (
          <TableRow key={idx}>
            <TableCell>{j.job.unique}</TableCell>
            <TableCell align="right">{j.job.cycles}</TableCell>
            <TableCell align="right">{j.remainingToStart}</TableCell>
            <TableCell align="right">{j.assignedRaw}</TableCell>
            <TableCell align="right">{Math.max(j.remainingToStart - j.assignedRaw, 0)}</TableCell>
            <TableCell align="right">{j.availableUnassigned}</TableCell>
          </TableRow>
        ))}
        {jobs.length >= 2 ? (
          <TableRow>
            <TableCell>Total</TableCell>
            <TableCell align="right">{LazySeq.of(jobs).sumBy((j) => j.job.cycles)}</TableCell>
            <TableCell align="right">{LazySeq.of(jobs).sumBy((j) => j.remainingToStart)}</TableCell>
            <TableCell align="right">{LazySeq.of(jobs).sumBy((j) => j.assignedRaw)}</TableCell>
            <TableCell align="right">
              {LazySeq.of(jobs).sumBy((j) => Math.max(j.remainingToStart - j.assignedRaw, 0))}
            </TableCell>
            <TableCell align="right">{LazySeq.of(jobs).sumBy((j) => j.availableUnassigned)}</TableCell>
          </TableRow>
        ) : undefined}
      </TableBody>
    </Table>
  );
}

export const BulkAddCastingWithoutSerialDialog = memo(function BulkAddCastingWithoutSerialDialog() {
  const [queue, setQueue] = useAtom(bulkAddCastingToQueue);

  const operator = useAtomValue(currentOperator);
  const fmsInfo = useAtomValue(fmsInformation);
  const printOnAdd = fmsInfo.usingLabelPrinterForSerials && fmsInfo.useClientPrinterForLabels;
  const [addNewCasting] = useAddNewCastingToQueue();
  const setWaitingToPrint = useSetAtom(waitingForMaterialAssignmentDialog);

  const [selectedCasting, setSelectedCasting] = useState<string | null>(null);
  const [enteredQty, setQty] = useState<number | null>(null);
  const [enteredOperator, setEnteredOperator] = useState<string | null>(null);
  const [adding, setAdding] = useState<boolean>(false);

  const currentSt = useAtomValue(currentStatus);
  const historicCastNames = useAtomValue(castingNames);

  const castings = useMemo(
    () =>
      LazySeq.ofObject(currentSt.jobs)
        .flatMap(([, j]) => j.procsAndPaths[0].paths)
        .filter((p) => p.casting !== undefined && p.casting !== "")
        .map((p) => ({ casting: p.casting as string, cnt: 1 }))
        .concat(LazySeq.of(historicCastNames).map((c) => ({ casting: c, cnt: 0 })))
        .buildOrderedMap<string, number>(
          (c) => c.casting,
          (old, c) => (old === undefined ? c.cnt : old + c.cnt),
        )
        .toAscLazySeq(),
    [currentSt.jobs, historicCastNames],
  );

  function close() {
    setQueue(null);
    setSelectedCasting(null);
    setEnteredOperator(null);
    setAdding(false);
    setQty(null);
  }

  function add() {
    if (
      queue !== null &&
      selectedCasting !== null &&
      enteredQty !== null &&
      !isNaN(enteredQty) &&
      enteredQty > 0
    ) {
      addNewCasting({
        casting: selectedCasting,
        quantity: enteredQty,
        queue: queue,
        workorder: null,
        operator: fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? enteredOperator : operator,
      });
    }
    close();
  }

  function addAndPrint() {
    if (queue !== null && selectedCasting !== null && enteredQty !== null && !isNaN(enteredQty)) {
      setAdding(true);
      addNewCasting({
        casting: selectedCasting,
        quantity: enteredQty,
        queue: queue,
        operator: operator,
        workorder: null,
        onNewMaterial: (mats) => {
          setWaitingToPrint({
            selectedCasting: selectedCasting,
            operator: fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? enteredOperator : operator,
            materialIds: new Set(mats.map((m) => m.materialID)),
          });
          close();
        },
        onError: () => {
          close();
        },
      });
    }
  }

  return (
    <>
      <Dialog open={queue !== null} onClose={() => close()} maxWidth="md">
        <DialogTitle>Add Raw Material</DialogTitle>
        <DialogContent>
          <Stack direction="column" spacing={2} mt="0.5em">
            <TextField
              style={{ minWidth: "15em" }}
              value={selectedCasting || ""}
              onChange={(e) => setSelectedCasting(e.target.value)}
              select
              fullWidth
              label="Raw Material"
              slotProps={{
                select: {
                  renderValue:
                    selectedCasting === null
                      ? undefined
                      : () => (
                          <div style={{ display: "flex", alignItems: "center" }}>
                            <div style={{ marginRight: "0.5em" }}>
                              <PartIdenticon part={selectedCasting} size={25} />
                            </div>
                            <div>{selectedCasting}</div>
                          </div>
                        ),
                },
              }}
            >
              {castings.map(([casting, jobCnt], idx) => (
                <MenuItem key={idx} value={casting}>
                  <ListItemIcon>
                    <PartIdenticon part={casting} />
                  </ListItemIcon>
                  <ListItemText
                    primary={casting}
                    slotProps={{ primary: { variant: "h4" } }}
                    secondary={
                      jobCnt === 0
                        ? "Not used by any current jobs"
                        : `Used by ${jobCnt} current job${jobCnt > 1 ? "s" : ""}`
                    }
                  />
                </MenuItem>
              ))}
            </TextField>
            <TextField
              fullWidth
              type="number"
              label="Quantity"
              disabled={selectedCasting === null}
              slotProps={{ htmlInput: { min: "1" } }}
              value={enteredQty === null || isNaN(enteredQty) || enteredQty <= 0 ? "" : enteredQty}
              onChange={(e) => setQty(parseInt(e.target.value))}
            />
            {fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? (
              <TextField
                fullWidth
                label="Operator"
                value={enteredOperator || ""}
                onChange={(e) => setEnteredOperator(e.target.value)}
              />
            ) : undefined}
            {selectedCasting !== null && queue !== null ? (
              <JobsForCasting casting={selectedCasting} queue={queue} />
            ) : undefined}
          </Stack>
        </DialogContent>
        <DialogActions>
          {printOnAdd ? (
            <Button
              color="primary"
              disabled={
                adding ||
                selectedCasting === null ||
                enteredQty === null ||
                isNaN(enteredQty) ||
                (fmsInfo.requireOperatorNamePromptWhenAddingMaterial &&
                  (enteredOperator === null || enteredOperator === ""))
              }
              onClick={addAndPrint}
            >
              {adding ? <CircularProgress size={10} /> : undefined}
              Add {enteredQty !== null && !isNaN(enteredQty) ? enteredQty.toString() + " " : ""}to {queue}
            </Button>
          ) : (
            <Button
              color="primary"
              disabled={
                selectedCasting === null ||
                enteredQty === null ||
                isNaN(enteredQty) ||
                (fmsInfo.requireOperatorNamePromptWhenAddingMaterial &&
                  (enteredOperator === null || enteredOperator === ""))
              }
              onClick={add}
            >
              Add {enteredQty !== null && !isNaN(enteredQty) ? enteredQty.toString() + " " : ""}to {queue}
            </Button>
          )}
          <Button color="primary" disabled={adding && printOnAdd} onClick={close}>
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
      <WaitingForMaterialDialog />
    </>
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
  const [printLabel, printingLabel] = usePrintLabel();

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
              slotProps={{ htmlInput: { min: "1", max: props.material.length.toString() } }}
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
