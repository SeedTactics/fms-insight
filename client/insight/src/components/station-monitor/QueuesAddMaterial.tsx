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
import { Button, ListItemButton, styled } from "@mui/material";
import { List } from "@mui/material";
import { ListItem } from "@mui/material";
import { ListItemText } from "@mui/material";
import { ListItemIcon } from "@mui/material";
import { FormControl } from "@mui/material";
import { InputLabel } from "@mui/material";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Dialog } from "@mui/material";
import { DialogActions } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { Collapse } from "@mui/material";
import { CircularProgress } from "@mui/material";
import { TextField } from "@mui/material";
import { ExpandMore as ExpandMoreIcon } from "@mui/icons-material";
import { Tooltip } from "@mui/material";
import { useReactToPrint } from "react-to-print";

import { PartIdenticon } from "./Material.js";
import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { JobAndGroups, extractJobGroups } from "../../data/queue-material.js";
import { currentOperator } from "../../data/operators.js";
import { PrintedLabel } from "./PrintedLabel.js";
import { atom, useRecoilState, useRecoilValue, useRecoilValueLoadable } from "recoil";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { useAddNewCastingToQueue } from "../../cell-status/material-details.js";
import { castingNames } from "../../cell-status/names.js";

function showAddMaterial(
  inProc: Readonly<api.IInProcessMaterial> | null,
  queueNames: ReadonlyArray<string>
): boolean {
  if (inProc === null) return true;

  if (
    inProc.location.type === api.LocType.InQueue &&
    inProc.location.currentQueue &&
    queueNames.includes(inProc.location.currentQueue)
  ) {
    return false;
  }

  if (inProc.location.type === api.LocType.OnPallet) {
    return false;
  }

  return true;
}

const ExpandMore = styled(ExpandMoreIcon, { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })<{
  $expandedOpen?: boolean;
}>(({ theme, $expandedOpen }) => ({
  transform: $expandedOpen ? "rotate(0deg)" : "rotate(-90deg)",
  transition: theme.transitions.create("transform", {
    duration: theme.transitions.duration.shortest,
  }),
}));

export interface AddNewJobProcessState {
  readonly job: Readonly<api.IActiveJob>;
  readonly last_proc: number;
}

interface SelectJobProps {
  readonly queue: string | null;
  readonly selectedJob: AddNewJobProcessState | null;
  readonly onSelectJob: (j: AddNewJobProcessState | null) => void;
}

function SelectJob(props: SelectJobProps) {
  const [selectedJob, setSelectedJob] = React.useState<string | null>(null);
  const currentSt = useRecoilValue(currentStatus);
  const jobs: ReadonlyArray<JobAndGroups> = React.useMemo(
    () =>
      LazySeq.ofObject(currentSt.jobs)
        .map(([_uniq, j]) => extractJobGroups(j))
        .toSortedArray((j) => j.job.partName),
    [currentSt.jobs]
  );

  return (
    <List>
      {jobs.map((j, idx) => (
        <React.Fragment key={idx}>
          <ListItem
            button
            onClick={() => {
              if (props.selectedJob) props.onSelectJob(null);
              setSelectedJob(selectedJob === j.job.unique ? null : j.job.unique);
            }}
          >
            <ListItemIcon>
              <ExpandMore $expandedOpen={selectedJob === j.job.unique} />
            </ListItemIcon>
            <ListItemIcon>
              <PartIdenticon part={j.job.partName} />
            </ListItemIcon>
            <ListItemText
              primary={j.job.partName + " (" + j.job.unique + ")"}
              secondary={j.job.routeStartUTC.toLocaleString()}
            />
          </ListItem>
          <Collapse in={selectedJob === j.job.unique} timeout="auto">
            {j.machinedProcs.map((p, idx) => (
              <Tooltip
                title={props.queue !== null && !p.queues.has(props.queue) ? "Not used in " + props.queue : ""}
                key={idx}
              >
                <div>
                  <ListItem
                    button
                    sx={(theme) => ({ pl: theme.spacing(4) })}
                    disabled={props.queue !== null && !p.queues.has(props.queue)}
                    selected={
                      props.selectedJob?.job.unique === j.job.unique &&
                      props.selectedJob?.last_proc === p.lastProc
                    }
                    onClick={() => props.onSelectJob({ job: j.job, last_proc: p.lastProc })}
                  >
                    <ListItemText
                      primary={
                        p.lastProc === 0 ? "Raw Material" : "Last machined process " + p.lastProc.toString()
                      }
                      secondary={p.details}
                    />
                  </ListItem>
                </div>
              </Tooltip>
            ))}
          </Collapse>
        </React.Fragment>
      ))}
    </List>
  );
}

export function PromptForJob({
  selectedJob,
  setSelectedJob,
  toQueue,
}: {
  selectedJob: AddNewJobProcessState | null;
  setSelectedJob: (j: AddNewJobProcessState | null) => void;
  toQueue: string | null;
}) {
  const toShow = useRecoilValue(matDetails.materialDialogOpen);
  const existingMat = useRecoilValue(matDetails.materialInDialogInfo);
  if (toShow === null) return null;
  if (existingMat) return null;

  switch (toShow.type) {
    case "Barcode":
    case "AddMatWithEnteredSerial":
    case "ManuallyEnteredSerial":
      return <SelectJob queue={toQueue} selectedJob={selectedJob} onSelectJob={setSelectedJob} />;
    default:
      return null;
  }
}

export function PromptForQueue({
  selectedQueue,
  setSelectedQueue,
  queueNames,
}: {
  selectedQueue: string | null;
  setSelectedQueue: (queue: string) => void;
  queueNames: ReadonlyArray<string>;
}) {
  const inProcMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  if (!showAddMaterial(inProcMat, queueNames)) return null;

  if (queueNames.length <= 1) {
    return null;
  }

  return (
    <div>
      <p>Select a queue</p>
      <List>
        {queueNames.map((q, idx) => (
          <ListItemButton key={idx} selected={q === selectedQueue} onClick={() => setSelectedQueue(q)}>
            {q}
          </ListItemButton>
        ))}
      </List>
    </div>
  );
}

export function PromptForOperator({
  enteredOperator,
  setEnteredOperator,
  queueNames,
}: {
  enteredOperator: string | null;
  setEnteredOperator: (op: string) => void;
  queueNames: ReadonlyArray<string>;
}) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const inProcMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  if (!showAddMaterial(inProcMat, queueNames)) return null;
  if (!fmsInfo.requireOperatorNamePromptWhenAddingMaterial) return null;

  return (
    <div style={{ marginLeft: "1em" }}>
      <TextField
        fullWidth
        label="Operator"
        value={enteredOperator || ""}
        required={true}
        onChange={(e) => setEnteredOperator(e.target.value)}
      />
    </div>
  );
}

export function AddToQueueButton({
  enteredOperator,
  selectedJob,
  toQueue,
}: {
  enteredOperator: string | null;
  selectedJob: AddNewJobProcessState | null;
  toQueue: string | null;
}) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const operator = useRecoilValue(currentOperator);
  const closeMatDialog = matDetails.useCloseMaterialDialog();

  const newSerial = useRecoilValue(matDetails.serialInMaterialDialog);
  const existingMat = useRecoilValue(matDetails.materialInDialogInfo);
  const inProcMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  const evts = useRecoilValueLoadable(matDetails.materialInDialogEvents);

  const [addExistingMat, addingExistingMat] = matDetails.useAddExistingMaterialToQueue();
  const [addNewMat, addingNewMat] = matDetails.useAddNewMaterialToQueue();

  let addProcMsg = "";
  if (existingMat !== null) {
    if (evts.state === "hasValue") {
      const lastProc =
        LazySeq.of(evts.getValue())
          .filter(
            (e) =>
              e.details?.["PalletCycleInvalidated"] !== "1" &&
              (e.type === api.LogType.LoadUnloadCycle ||
                e.type === api.LogType.MachineCycle ||
                e.type === api.LogType.AddToQueue)
          )
          .flatMap((e) => e.material)
          .filter((m) => m.id === existingMat.materialID)
          .maxBy((m) => m.proc)?.proc ?? 0;
      addProcMsg = " To Run Process " + (lastProc + 1).toString();
    }
  } else {
    if (selectedJob?.last_proc !== undefined) {
      addProcMsg = " To Run Process " + (selectedJob.last_proc + 1).toString();
    }
  }

  const curQueue = inProcMat?.location.currentQueue ?? null;

  return (
    <Button
      color="primary"
      disabled={
        evts.state === "loading" ||
        toQueue === null ||
        addingExistingMat === true ||
        addingNewMat === true ||
        (existingMat === null && selectedJob === null) ||
        (fmsInfo.requireSerialWhenAddingMaterialToQueue && newSerial === null) ||
        (fmsInfo.requireOperatorNamePromptWhenAddingMaterial && enteredOperator === null)
      }
      onClick={() => {
        if (existingMat) {
          addExistingMat({
            materialId: existingMat.materialID,
            queue: toQueue ?? "",
            queuePosition: -1,
            operator: enteredOperator ?? operator,
          });
        } else if (selectedJob) {
          addNewMat({
            jobUnique: selectedJob.job.unique,
            lastCompletedProcess: selectedJob.last_proc,
            serial: newSerial ?? undefined,
            queue: toQueue ?? "",
            queuePosition: -1,
            operator: enteredOperator ?? operator,
          });
        }
        closeMatDialog();
      }}
    >
      {toQueue === null ? (
        "Add to Queue"
      ) : (
        <span>
          {curQueue !== null ? `Move From ${curQueue} To` : "Add To"} {toQueue}
          {addProcMsg}
        </span>
      )}
    </Button>
  );
}

export const enterSerialForNewMaterialDialog = atom<string | null>({
  key: "add-by-serial-dialog",
  default: null,
});

export const AddBySerialDialog = React.memo(function AddBySerialDialog() {
  const [queue, setQueue] = useRecoilState(enterSerialForNewMaterialDialog);
  const setMatToDisplay = matDetails.useSetMaterialToShowInDialog();
  const [serial, setSerial] = React.useState<string | undefined>(undefined);

  function lookup() {
    if (serial && serial !== "" && queue !== null) {
      setMatToDisplay({ type: "AddMatWithEnteredSerial", serial, toQueue: queue });
      setQueue(null);
      setSerial(undefined);
    }
  }
  function close() {
    setQueue(null);
    setSerial(undefined);
  }
  return (
    <Dialog open={queue !== null} onClose={close} maxWidth="md">
      <DialogTitle>Lookup Material</DialogTitle>
      <DialogContent>
        <TextField
          label="Serial"
          value={serial || ""}
          onChange={(e) => setSerial(e.target.value)}
          onKeyPress={(e) => {
            if (e.key === "Enter" && serial && serial !== "") {
              e.preventDefault();
              lookup();
            }
          }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={lookup} color="secondary">
          Lookup Serial
        </Button>
        <Button onClick={close} color="primary">
          Cancel
        </Button>
      </DialogActions>
    </Dialog>
  );
});

export const bulkAddCastingToQueue = atom<string | null>({
  key: "bulk-add-casting-dialog",
  default: null,
});

function AddAndPrintOnClientButton({
  operator,
  selectedCasting,
  queue,
  close,
  qty,
  disabled,
}: {
  operator: string | null;
  selectedCasting: string | null;
  queue: string | null;
  close: () => void;
  qty: number | null;
  disabled: boolean;
}) {
  const printRef = React.useRef(null);
  const [adding, setAdding] = React.useState<boolean>(false);
  const print = useReactToPrint({
    content: () => printRef.current,
    onAfterPrint: close,
    copyStyles: false,
  });
  const [materialToPrint, setMaterialToPrint] = React.useState<ReadonlyArray<api.IInProcessMaterial>>([]);
  const [addNewCasting] = useAddNewCastingToQueue();

  React.useEffect(() => {
    if (materialToPrint.length === 0) return;
    print();
  }, [materialToPrint]);

  function addAndPrint() {
    if (queue !== null && selectedCasting !== null && qty !== null && !isNaN(qty)) {
      setAdding(true);
      addNewCasting({
        casting: selectedCasting,
        quantity: qty,
        queue: queue,
        operator: operator,
        onNewMaterial: (mats) => {
          setMaterialToPrint(mats);
        },
        onError: () => {
          close();
        },
      });
    }
  }

  return (
    <>
      <Button color="primary" disabled={disabled || adding} onClick={addAndPrint}>
        {adding ? <CircularProgress size={10} /> : undefined}
        Add to {queue}
      </Button>
      <div style={{ display: "none" }}>
        <div ref={printRef}>
          <PrintedLabel
            materialName={selectedCasting}
            material={materialToPrint}
            operator={operator}
            oneJobPerPage={true}
          />
        </div>
      </div>
    </>
  );
}

export const BulkAddCastingWithoutSerialDialog = React.memo(function BulkAddCastingWithoutSerialDialog() {
  const [queue, setQueue] = useRecoilState(bulkAddCastingToQueue);

  const currentSt = useRecoilValue(currentStatus);
  const operator = useRecoilValue(currentOperator);
  const fmsInfo = useRecoilValue(fmsInformation);
  const printOnAdd = fmsInfo.usingLabelPrinterForSerials && fmsInfo.useClientPrinterForLabels;
  const [addNewCasting] = useAddNewCastingToQueue();

  const [selectedCasting, setSelectedCasting] = React.useState<string | null>(null);
  const [qty, setQty] = React.useState<number | null>(null);
  const [enteredOperator, setEnteredOperator] = React.useState<string | null>(null);
  const [adding, setAdding] = React.useState<boolean>(false);
  const castNames = useRecoilValue(castingNames);
  const castings: LazySeq<readonly [string, number]> = React.useMemo(
    () =>
      LazySeq.ofObject(currentSt.jobs)
        .flatMap(([, j]) => j.procsAndPaths[0].paths)
        .filter((p) => p.casting !== undefined && p.casting !== "")
        .map((p) => ({ casting: p.casting as string, cnt: 1 }))
        .concat(LazySeq.of(castNames).map((c) => ({ casting: c, cnt: 0 })))
        .buildOrderedMap<string, number>(
          (c) => c.casting,
          (old, c) => (old === undefined ? c.cnt : old + c.cnt)
        )
        .toAscLazySeq(),
    [currentSt.jobs, castNames]
  );

  function close() {
    setQueue(null);
    setSelectedCasting(null);
    setEnteredOperator(null);
    setAdding(false);
    setQty(null);
  }

  function add() {
    if (queue !== null && selectedCasting !== null && qty !== null && !isNaN(qty)) {
      addNewCasting({
        casting: selectedCasting,
        quantity: qty,
        queue: queue,
        operator: fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? enteredOperator : operator,
      });
    }
    close();
  }

  return (
    <>
      <Dialog open={queue !== null} onClose={() => close()}>
        <DialogTitle>Add Raw Material</DialogTitle>
        <DialogContent>
          <FormControl>
            <InputLabel id="select-casting-label">Raw Material</InputLabel>
            <Select
              style={{ minWidth: "15em" }}
              labelId="select-casting-label"
              value={selectedCasting || ""}
              onChange={(e) => setSelectedCasting(e.target.value)}
              renderValue={
                selectedCasting === null
                  ? undefined
                  : () => (
                      <div style={{ display: "flex", alignItems: "center" }}>
                        <div style={{ marginRight: "0.5em" }}>
                          <PartIdenticon part={selectedCasting} />
                        </div>
                        <div>{selectedCasting}</div>
                      </div>
                    )
              }
            >
              {castings.map(([casting, jobCnt], idx) => (
                <MenuItem key={idx} value={casting}>
                  <ListItemIcon>
                    <PartIdenticon part={casting} />
                  </ListItemIcon>
                  <ListItemText
                    primary={casting}
                    secondary={
                      jobCnt === 0 ? "Not used by any current jobs" : `Used by ${jobCnt} current jobs`
                    }
                  />
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <div style={{ marginTop: "3em", marginBottom: "2em" }}>
            <TextField
              fullWidth
              type="number"
              label="Quantity"
              inputProps={{ min: "1" }}
              value={qty === null || isNaN(qty) ? "" : qty}
              onChange={(e) => setQty(parseInt(e.target.value))}
            />
          </div>
          {fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? (
            <div style={{ marginBottom: "2em" }}>
              <TextField
                style={{ marginTop: "1em" }}
                fullWidth
                label="Operator"
                value={enteredOperator || ""}
                onChange={(e) => setEnteredOperator(e.target.value)}
              />
            </div>
          ) : undefined}
        </DialogContent>
        <DialogActions>
          {printOnAdd ? (
            <AddAndPrintOnClientButton
              queue={queue}
              selectedCasting={selectedCasting}
              operator={fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? enteredOperator : operator}
              qty={qty}
              close={close}
              disabled={
                selectedCasting === null ||
                qty === null ||
                isNaN(qty) ||
                (!!fmsInfo.requireOperatorNamePromptWhenAddingMaterial &&
                  (enteredOperator === null || enteredOperator === ""))
              }
            />
          ) : (
            <Button
              color="primary"
              disabled={
                selectedCasting === null ||
                qty === null ||
                isNaN(qty) ||
                (fmsInfo.requireOperatorNamePromptWhenAddingMaterial &&
                  (enteredOperator === null || enteredOperator === ""))
              }
              onClick={add}
            >
              Add to {queue}
            </Button>
          )}
          <Button color="primary" disabled={adding && printOnAdd} onClick={close}>
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
});
