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
import {
  Button,
  ListItemButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
  styled,
} from "@mui/material";
import { List } from "@mui/material";
import { ListItem } from "@mui/material";
import { ListItemText } from "@mui/material";
import { ListItemIcon } from "@mui/material";
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
import {
  SelectableCasting,
  SelectableJob,
  SelectableMaterialType,
  extractJobRawMaterial,
  usePossibleNewMaterialTypes,
} from "../../data/queue-material.js";
import { currentOperator } from "../../data/operators.js";
import { PrintedLabel } from "./PrintedLabel.js";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { useAddNewCastingToQueue } from "../../cell-status/material-details.js";
import { castingNames } from "../../cell-status/names.js";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";

export function useMaterialInDialogAddType(
  queueNames: ReadonlyArray<string>,
): "None" | "MatInQueue" | "AddToQueue" {
  const existingMat = useAtomValue(matDetails.materialInDialogInfo);
  const inProcMat = useAtomValue(matDetails.inProcessMaterialInDialog);
  const fmsInfo = useAtomValue(fmsInformation);

  const curInQueueOnScreen =
    inProcMat !== null &&
    inProcMat.location.type === api.LocType.InQueue &&
    inProcMat.location.currentQueue &&
    queueNames.includes(inProcMat.location.currentQueue);
  if (curInQueueOnScreen) return "MatInQueue";

  const curOnPallet = inProcMat !== null && inProcMat.location.type === api.LocType.OnPallet;
  if (curOnPallet) return "None";

  // this is just a prelimiary check to see if we should show the dialog at all,
  // other combinations of addInProcessMaterial and addRawMaterial are handled by the details
  // shown inside the dialog, perhaps preventing the user from selecting a queue or job
  // in certian cases (with an appropriate error message).
  const missingButMatRequired =
    existingMat === null &&
    fmsInfo.addInProcessMaterial === api.AddInProcessMaterialType.RequireExistingMaterial &&
    fmsInfo.addRawMaterial === api.AddRawMaterialType.RequireExistingMaterial;
  if (missingButMatRequired) return "None";

  return "AddToQueue";
}

const ExpandMore = styled(ExpandMoreIcon, { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })<{
  $expandedOpen?: boolean;
}>(({ theme, $expandedOpen }) => ({
  transform: $expandedOpen ? "rotate(0deg)" : "rotate(-90deg)",
  transition: theme.transitions.create("transform", {
    duration: theme.transitions.duration.shortest,
  }),
}));

export type NewMaterialToQueueType =
  | {
      readonly kind: "JobAndProc";
      readonly job: Readonly<api.IJob>;
      readonly last_proc: number;
    }
  | { readonly kind: "RawMat"; readonly rawMatName: string };

type CurrentCollapseOpen =
  | { readonly kind: "AllJobs" }
  | { readonly kind: "Job"; readonly unique: string }
  | { readonly kind: "RawMat" };

function SelectRawMaterial({
  newMaterialTy,
  setNewMaterialTy,
  castings,
  indent,
}: {
  readonly newMaterialTy: NewMaterialToQueueType | null;
  readonly setNewMaterialTy: (j: NewMaterialToQueueType | null) => void;
  readonly castings: ReadonlyArray<SelectableCasting>;
  readonly indent?: boolean;
}) {
  return (
    <List sx={indent ? (theme) => ({ pl: theme.spacing(4) }) : undefined}>
      {castings.map((casting) => (
        <ListItem key={casting.casting}>
          <ListItemButton
            selected={newMaterialTy?.kind === "RawMat" && newMaterialTy?.rawMatName === casting.casting}
            onClick={() => setNewMaterialTy({ kind: "RawMat", rawMatName: casting.casting })}
          >
            <ListItemIcon>
              <PartIdenticon part={casting.casting} />
            </ListItemIcon>
            <ListItemText primary={casting.casting} secondary={casting.message ?? undefined} />
          </ListItemButton>
        </ListItem>
      ))}
    </List>
  );
}

function SelectJob({
  newMaterialTy,
  setNewMaterialTy,
  curCollapse,
  setCurCollapse,
  indent,
  jobs,
}: {
  readonly jobs: ReadonlyArray<SelectableJob>;
  readonly newMaterialTy: NewMaterialToQueueType | null;
  readonly setNewMaterialTy: (j: NewMaterialToQueueType | null) => void;
  readonly curCollapse: CurrentCollapseOpen | null;
  readonly setCurCollapse: (c: CurrentCollapseOpen | null) => void;
  readonly indent?: boolean;
}) {
  return (
    <List sx={indent ? (theme) => ({ pl: theme.spacing(4) }) : undefined}>
      {jobs.map((j, idx) =>
        j.machinedProcs.length === 1 ? (
          <Tooltip title={j.machinedProcs[0].disabledMsg ?? ""} key={idx}>
            <div>
              <ListItem>
                <ListItemButton
                  alignItems="flex-start"
                  disabled={!!j.machinedProcs[0].disabledMsg}
                  selected={
                    newMaterialTy?.kind === "JobAndProc" &&
                    newMaterialTy?.job.unique === j.job.unique &&
                    newMaterialTy?.last_proc === j.machinedProcs[0].lastProc
                  }
                  onClick={() =>
                    setNewMaterialTy({
                      kind: "JobAndProc",
                      job: j.job,
                      last_proc: j.machinedProcs[0].lastProc,
                    })
                  }
                >
                  <ListItemIcon>
                    <PartIdenticon part={j.job.partName} />
                  </ListItemIcon>
                  <ListItemText
                    primary={j.job.partName + " (" + j.job.unique + ")"}
                    secondary={
                      <Typography variant="body2">
                        {j.machinedProcs[0].lastProc === 0
                          ? "Raw Material"
                          : "Last machined process " + j.machinedProcs[0].lastProc.toString()}
                        , {j.job.routeStartUTC.toLocaleDateString()}, {j.machinedProcs[0].details}
                      </Typography>
                    }
                  />
                </ListItemButton>
              </ListItem>
            </div>
          </Tooltip>
        ) : (
          <React.Fragment key={idx}>
            <ListItem>
              <ListItemButton
                onClick={() => {
                  if (newMaterialTy) setNewMaterialTy(null);
                  setCurCollapse(
                    curCollapse && curCollapse.kind === "Job" && curCollapse.unique === j.job.unique
                      ? null
                      : { kind: "Job", unique: j.job.unique },
                  );
                }}
              >
                <ListItemIcon>
                  <ExpandMore
                    $expandedOpen={
                      curCollapse !== null &&
                      curCollapse.kind === "Job" &&
                      curCollapse.unique === j.job.unique
                    }
                  />
                </ListItemIcon>
                <ListItemIcon>
                  <PartIdenticon part={j.job.partName} />
                </ListItemIcon>
                <ListItemText
                  primary={j.job.partName + " (" + j.job.unique + ")"}
                  secondary={j.job.routeStartUTC.toLocaleDateString()}
                />
              </ListItemButton>
            </ListItem>
            <Collapse
              in={curCollapse !== null && curCollapse.kind === "Job" && curCollapse.unique === j.job.unique}
              timeout="auto"
            >
              {j.machinedProcs.map((p, idx) => (
                <Tooltip title={p.disabledMsg ?? ""} key={idx}>
                  <div>
                    <ListItem sx={(theme) => ({ pl: theme.spacing(indent ? 8 : 4) })}>
                      <ListItemButton
                        disabled={!!p.disabledMsg}
                        selected={
                          newMaterialTy?.kind === "JobAndProc" &&
                          newMaterialTy?.job.unique === j.job.unique &&
                          newMaterialTy?.last_proc === p.lastProc
                        }
                        onClick={() =>
                          setNewMaterialTy({ kind: "JobAndProc", job: j.job, last_proc: p.lastProc })
                        }
                      >
                        <ListItemText
                          primary={
                            p.lastProc === 0
                              ? "Raw Material"
                              : "Last machined process " + p.lastProc.toString()
                          }
                          secondary={p.details}
                        />
                      </ListItemButton>
                    </ListItem>
                  </div>
                </Tooltip>
              ))}
            </Collapse>
          </React.Fragment>
        ),
      )}
    </List>
  );
}

function SelectRawMatAndJob({
  newMaterialTy,
  options,
  setNewMaterialTy,
  curCollapse,
  setCurCollapse,
}: {
  readonly options: SelectableMaterialType;
  readonly newMaterialTy: NewMaterialToQueueType | null;
  readonly setNewMaterialTy: (j: NewMaterialToQueueType | null) => void;
  readonly curCollapse: CurrentCollapseOpen | null;
  readonly setCurCollapse: (c: CurrentCollapseOpen | null) => void;
}) {
  return (
    <List>
      <ListItem>
        <ListItemButton
          onClick={() => {
            if (newMaterialTy) setNewMaterialTy(null);
            setCurCollapse(curCollapse && curCollapse.kind === "RawMat" ? null : { kind: "RawMat" });
          }}
        >
          <ListItemIcon>
            <ExpandMore $expandedOpen={curCollapse !== null && curCollapse.kind === "RawMat"} />
          </ListItemIcon>
          <ListItemText primary="Raw Material" />
        </ListItemButton>
      </ListItem>
      <Collapse in={curCollapse !== null && curCollapse.kind === "RawMat"} timeout="auto">
        <SelectRawMaterial
          castings={options.castings}
          newMaterialTy={newMaterialTy}
          setNewMaterialTy={setNewMaterialTy}
          indent
        />
      </Collapse>
      <ListItem>
        <ListItemButton
          onClick={() => {
            if (newMaterialTy) setNewMaterialTy(null);
            setCurCollapse(
              curCollapse && (curCollapse.kind === "AllJobs" || curCollapse.kind === "Job")
                ? null
                : { kind: "AllJobs" },
            );
          }}
        >
          <ListItemIcon>
            <ExpandMore
              $expandedOpen={
                curCollapse !== null && (curCollapse.kind === "AllJobs" || curCollapse.kind === "Job")
              }
            />
          </ListItemIcon>
          <ListItemText primary="Specify Job" />
        </ListItemButton>
      </ListItem>
      <Collapse
        in={curCollapse !== null && (curCollapse.kind === "AllJobs" || curCollapse.kind === "Job")}
        timeout="auto"
      >
        <SelectJob
          newMaterialTy={newMaterialTy}
          jobs={options.jobs}
          setNewMaterialTy={setNewMaterialTy}
          curCollapse={curCollapse}
          setCurCollapse={setCurCollapse}
          indent
        />
      </Collapse>
    </List>
  );
}

function PromptForMaterialType({
  newMaterialTy,
  setNewMaterialTy,
  toQueue,
}: {
  newMaterialTy: NewMaterialToQueueType | null;
  setNewMaterialTy: (j: NewMaterialToQueueType | null) => void;
  toQueue: string | null;
}) {
  const serial = useAtomValue(matDetails.serialInMaterialDialog);
  const existingMat = useAtomValue(matDetails.materialInDialogInfo);
  const [curCollapse, setCurCollapse] = React.useState<CurrentCollapseOpen | null>(null);
  const materialTypes = usePossibleNewMaterialTypes(toQueue);

  if (existingMat) return null;
  if (serial === null) return null;

  if (materialTypes.castings.length === 0 && materialTypes.jobs.length === 0) {
    return <div>No scheduled material uses this queue</div>;
  }

  if (materialTypes.jobs.length === 0) {
    return (
      <SelectRawMaterial
        newMaterialTy={newMaterialTy}
        setNewMaterialTy={setNewMaterialTy}
        castings={materialTypes.castings}
      />
    );
  }

  if (materialTypes.castings.length === 0) {
    return (
      <SelectJob
        jobs={materialTypes.jobs}
        newMaterialTy={newMaterialTy}
        setNewMaterialTy={setNewMaterialTy}
        curCollapse={curCollapse}
        setCurCollapse={setCurCollapse}
      />
    );
  }

  return (
    <SelectRawMatAndJob
      options={materialTypes}
      newMaterialTy={newMaterialTy}
      setNewMaterialTy={setNewMaterialTy}
      curCollapse={curCollapse}
      setCurCollapse={setCurCollapse}
    />
  );
}

function PromptForQueue({
  selectedQueue,
  setSelectedQueue,
  queueNames,
}: {
  selectedQueue: string | null;
  setSelectedQueue: (queue: string) => void;
  queueNames: ReadonlyArray<string>;
}) {
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

function PromptForOperator({
  enteredOperator,
  setEnteredOperator,
}: {
  enteredOperator: string | null;
  setEnteredOperator: (op: string) => void;
}) {
  const fmsInfo = useAtomValue(fmsInformation);
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

function WorkorderFromBarcode() {
  const currentSt = useAtomValue(currentStatus);
  const workorderId = useAtomValue(matDetails.workorderInMaterialDialog);
  if (!workorderId) return null;

  const comments = currentSt.workorders?.find((w) => w.workorderId === workorderId)?.comments ?? [];

  return (
    <div style={{ marginTop: "1em", marginLeft: "1em" }}>
      <p>Workorder: {workorderId}</p>
      <ul>
        {comments.map((c, idx) => (
          <li key={idx}>{c.comment}</li>
        ))}
      </ul>
    </div>
  );
}

export function AddToQueueMaterialDialogCt({
  queueNames,
  toQueue,
  enteredOperator,
  setEnteredOperator,
  selectedQueue,
  setSelectedQueue,
  newMaterialTy,
  setNewMaterialTy,
}: {
  queueNames: ReadonlyArray<string>;
  toQueue: string | null;
  enteredOperator: string | null;
  setEnteredOperator: (operator: string | null) => void;
  selectedQueue: string | null;
  setSelectedQueue: (q: string | null) => void;
  newMaterialTy: NewMaterialToQueueType | null;
  setNewMaterialTy: (job: NewMaterialToQueueType | null) => void;
}) {
  const toShow = useAtomValue(matDetails.materialDialogOpen);
  const requireSelectQueue = queueNames.length > 1 && toShow?.type !== "AddMatWithEnteredSerial";
  return (
    <>
      <WorkorderFromBarcode />
      {requireSelectQueue ? (
        <PromptForQueue
          selectedQueue={selectedQueue}
          setSelectedQueue={(q) => {
            setSelectedQueue(q);
            setNewMaterialTy(null);
          }}
          queueNames={queueNames}
        />
      ) : undefined}
      <PromptForMaterialType
        newMaterialTy={newMaterialTy}
        setNewMaterialTy={setNewMaterialTy}
        toQueue={toQueue}
      />
      <PromptForOperator enteredOperator={enteredOperator} setEnteredOperator={setEnteredOperator} />
    </>
  );
}

export function AddToQueueButton({
  enteredOperator,
  newMaterialTy,
  toQueue,
  onClose,
}: {
  enteredOperator: string | null;
  newMaterialTy: NewMaterialToQueueType | null;
  toQueue: string | null;
  onClose: () => void;
}) {
  const fmsInfo = useAtomValue(fmsInformation);
  const operator = useAtomValue(currentOperator);
  const setMatToShow = useSetAtom(matDetails.materialDialogOpen);

  const newSerial = useAtomValue(matDetails.serialInMaterialDialog);
  const newWorkorder = useAtomValue(matDetails.workorderInMaterialDialog);
  const existingMat = useAtomValue(matDetails.materialInDialogInfo);
  const inProcMat = useAtomValue(matDetails.inProcessMaterialInDialog);
  const lastProcMat = useAtomValue(matDetails.materialInDialogLargestUsedProcess);

  const [addExistingMat, addingExistingMat] = matDetails.useAddExistingMaterialToQueue();
  const [addNewMat, addingNewMat] = matDetails.useAddNewMaterialToQueue();
  const [addNewCasting, addingNewCasting] = useAddNewCastingToQueue();

  let addProcMsg = "";
  if (existingMat !== null) {
    if (lastProcMat && lastProcMat.process >= lastProcMat.totalNumProcesses) {
      return null;
    } else {
      const lastProc = lastProcMat?.process ?? 0;
      addProcMsg = " To Run Process " + (lastProc + 1).toString();
    }
  } else {
    if (newMaterialTy?.kind === "JobAndProc" && newMaterialTy?.last_proc !== undefined) {
      addProcMsg = " To Run Process " + (newMaterialTy.last_proc + 1).toString();
    }
    if (newMaterialTy?.kind === "RawMat" && newMaterialTy?.rawMatName) {
      addProcMsg = " As " + newMaterialTy.rawMatName;
    }
  }

  let withSerialMsg = "";
  if (!existingMat && newSerial && newSerial !== "") {
    withSerialMsg = " With Serial " + newSerial;
  }

  const curQueue = inProcMat?.location.currentQueue ?? null;

  return (
    <Button
      color="primary"
      disabled={
        toQueue === null ||
        addingExistingMat === true ||
        addingNewMat === true ||
        addingNewCasting === true ||
        (existingMat === null && newMaterialTy === null) ||
        (fmsInfo.requireOperatorNamePromptWhenAddingMaterial &&
          (enteredOperator === null || enteredOperator === ""))
      }
      onClick={() => {
        if (existingMat) {
          addExistingMat({
            materialId: existingMat.materialID,
            queue: toQueue ?? "",
            queuePosition: -1,
            operator: enteredOperator ?? operator,
          });
        } else if (newMaterialTy && newMaterialTy.kind === "JobAndProc" && newMaterialTy.job) {
          addNewMat({
            jobUnique: newMaterialTy.job.unique,
            lastCompletedProcess: newMaterialTy.last_proc,
            serial: newSerial ?? undefined,
            workorder: newWorkorder ?? null,
            queue: toQueue ?? "",
            queuePosition: -1,
            operator: enteredOperator ?? operator,
          });
        } else if (newMaterialTy && newMaterialTy.kind === "RawMat" && newMaterialTy.rawMatName) {
          addNewCasting({
            casting: newMaterialTy.rawMatName,
            quantity: 1,
            queue: toQueue ?? "",
            serials: newSerial ? [newSerial] : undefined,
            workorder: newWorkorder,
            operator: enteredOperator ?? operator,
          });
        }
        setMatToShow(null);
        onClose();
      }}
    >
      {toQueue === null ? (
        "Add to Queue"
      ) : (
        <span>
          {curQueue !== null ? `Move From ${curQueue} To` : "Add To"} {toQueue}
          {withSerialMsg}
          {addProcMsg}
        </span>
      )}
    </Button>
  );
}

export const enterSerialForNewMaterialDialog = atom<string | null>(null);

export const AddBySerialDialog = React.memo(function AddBySerialDialog() {
  const [queue, setQueue] = useAtom(enterSerialForNewMaterialDialog);
  const setMatToDisplay = useSetAtom(matDetails.materialDialogOpen);
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
          style={{ marginTop: "0.5em" }}
          autoFocus
          value={serial || ""}
          onChange={(e) => setSerial(e.target.value)}
          onKeyDown={(e) => {
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

export const bulkAddCastingToQueue = atom<string | null>(null);

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
  }, [materialToPrint, print]);

  function addAndPrint() {
    if (queue !== null && selectedCasting !== null && qty !== null && !isNaN(qty)) {
      setAdding(true);
      addNewCasting({
        casting: selectedCasting,
        quantity: qty,
        queue: queue,
        operator: operator,
        workorder: null,
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

function JobsForCasting({ queue, casting }: { queue: string; casting: string }) {
  const currentSt = useAtomValue(currentStatus);

  const jobs = React.useMemo(
    () =>
      extractJobRawMaterial(queue, currentSt.jobs, currentSt.material).filter(
        (j) => j.rawMatName === casting,
      ),
    [currentSt.jobs, currentSt.material, casting, queue],
  );

  if (jobs.length === 0) return null;

  return (
    <Table size="small">
      <TableHead>
        <TableCell>Job</TableCell>
        <TableCell align="right">Plan</TableCell>
        <TableCell align="right">Remaining</TableCell>
        <TableCell align="right">Assigned</TableCell>
        <TableCell align="right">Required</TableCell>
        <TableCell align="right">Available</TableCell>
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

export const BulkAddCastingWithoutSerialDialog = React.memo(function BulkAddCastingWithoutSerialDialog() {
  const [queue, setQueue] = useAtom(bulkAddCastingToQueue);

  const operator = useAtomValue(currentOperator);
  const fmsInfo = useAtomValue(fmsInformation);
  const printOnAdd = fmsInfo.usingLabelPrinterForSerials && fmsInfo.useClientPrinterForLabels;
  const [addNewCasting] = useAddNewCastingToQueue();

  const [selectedCasting, setSelectedCasting] = React.useState<string | null>(null);
  const [enteredQty, setQty] = React.useState<number | null>(null);
  const [enteredOperator, setEnteredOperator] = React.useState<string | null>(null);
  const [adding, setAdding] = React.useState<boolean>(false);

  const currentSt = useAtomValue(currentStatus);
  const historicCastNames = useAtomValue(castingNames);

  const castings = React.useMemo(
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

  return (
    <>
      <Dialog open={queue !== null} onClose={() => close()}>
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
              SelectProps={{
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
              }}
            >
              {castings.map(([casting, jobCnt], idx) => (
                <MenuItem key={idx} value={casting}>
                  <ListItemIcon>
                    <PartIdenticon part={casting} />
                  </ListItemIcon>
                  <ListItemText
                    primary={casting}
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
              inputProps={{ min: "1" }}
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
              <JobsForCasting queue={queue} casting={selectedCasting} />
            ) : undefined}
          </Stack>
        </DialogContent>
        <DialogActions>
          {printOnAdd ? (
            <AddAndPrintOnClientButton
              queue={queue}
              selectedCasting={selectedCasting}
              operator={fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? enteredOperator : operator}
              qty={enteredQty}
              close={close}
              disabled={
                selectedCasting === null ||
                enteredQty === null ||
                isNaN(enteredQty) ||
                enteredQty <= 0 ||
                (!!fmsInfo.requireOperatorNamePromptWhenAddingMaterial &&
                  (enteredOperator === null || enteredOperator === ""))
              }
            />
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
    </>
  );
});
