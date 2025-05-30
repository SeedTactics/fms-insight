/* Copyright (c) 2025, John Lenz

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

import { useState, Fragment, SetStateAction, Dispatch } from "react";
import {
  Button,
  Collapse,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  TextField,
  Typography,
  styled,
} from "@mui/material";
import { ExpandMore as ExpandMoreIcon } from "@mui/icons-material";

import { PartIdenticon, WorkorderFromBarcode } from "./Material.js";
import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import {
  SelectableCasting,
  SelectableJob,
  SelectableMaterialType,
  usePossibleNewMaterialTypes,
} from "../../data/queue-material.js";
import { currentOperator } from "../../data/operators.js";
import { fmsInformation } from "../../network/server-settings.js";
import { useAddNewCastingToQueue } from "../../cell-status/material-details.js";
import { useAtomValue, useSetAtom } from "jotai";
import {
  InvalidateCycleDialogButton,
  InvalidateCycleDialogContent,
  InvalidateCycleState,
} from "./InvalidateCycle.js";

export type AddMaterialState = {
  readonly toQueue: string | null;
  readonly enteredOperator: string | null;
  readonly newMaterialTy: NewMaterialToQueueType | null;
  readonly invalidateSt: InvalidateCycleState | null;
};

function useAllowAddToQueue(queueNames: ReadonlyArray<string>): boolean {
  const existingMat = useAtomValue(matDetails.materialInDialogInfo);
  const inProcMat = useAtomValue(matDetails.inProcessMaterialInDialog);
  const barcode = useAtomValue(matDetails.barcodeMaterialDetail);

  const curInQueueOnScreen =
    inProcMat !== null &&
    inProcMat.location.type === api.LocType.InQueue &&
    inProcMat.location.currentQueue &&
    queueNames.includes(inProcMat.location.currentQueue);
  if (curInQueueOnScreen) return false;

  const curOnPallet = inProcMat !== null && inProcMat.location.type === api.LocType.OnPallet;
  if (curOnPallet) return false;

  if (existingMat === null && !barcode?.potentialNewMaterial) {
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
            <ListItemText
              primary={casting.casting}
              primaryTypographyProps={{ variant: "h4" }}
              secondary={casting.message ?? undefined}
            />
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
          <div key={idx}>
            <ListItem>
              <ListItemButton
                alignItems="flex-start"
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
                  slotProps={{ primary: { variant: "h4" } }}
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
        ) : (
          <Fragment key={idx}>
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
                  slotProps={{ primary: { variant: "h4" } }}
                  secondary={j.job.routeStartUTC.toLocaleDateString()}
                />
              </ListItemButton>
            </ListItem>
            <Collapse
              in={curCollapse !== null && curCollapse.kind === "Job" && curCollapse.unique === j.job.unique}
              timeout="auto"
            >
              {j.machinedProcs.map((p, idx) => (
                <div key={idx}>
                  <ListItem sx={(theme) => ({ pl: theme.spacing(indent ? 8 : 4) })}>
                    <ListItemButton
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
                          p.lastProc === 0 ? "Raw Material" : "Last machined process " + p.lastProc.toString()
                        }
                        secondary={p.details}
                      />
                    </ListItemButton>
                  </ListItem>
                </div>
              ))}
            </Collapse>
          </Fragment>
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
  const [curCollapse, setCurCollapse] = useState<CurrentCollapseOpen | null>(null);
  const materialTypes = usePossibleNewMaterialTypes(toQueue);

  if (existingMat) return null;
  if (serial === null) return null;
  if (toQueue === null) return null;

  if (materialTypes.castings.length === 0 && materialTypes.jobs.length === 0) {
    return <div>No material is currently scheduled for {toQueue}</div>;
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

export function PromptForQueue({
  selectedQueue,
  setSelectedQueue,
  queueNames,
}: {
  selectedQueue: string | null;
  setSelectedQueue: (queue: string) => void;
  queueNames: ReadonlyArray<string>;
}) {
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

export function AddToQueueMaterialDialogCt({
  st,
  queueNames,
  setState,
}: {
  st: AddMaterialState;
  queueNames: ReadonlyArray<string>;
  setState: (st: AddMaterialState) => void;
}) {
  const allowAdd = useAllowAddToQueue(queueNames);

  if (!allowAdd) {
    return null;
  }

  return (
    <>
      <WorkorderFromBarcode />
      {queueNames.length > 1 ? (
        <PromptForQueue
          selectedQueue={st.toQueue}
          setSelectedQueue={(q) => {
            setState({
              toQueue: q,
              newMaterialTy: null,
              enteredOperator: st.enteredOperator,
              invalidateSt: st.invalidateSt,
            });
          }}
          queueNames={queueNames}
        />
      ) : undefined}
      <PromptForMaterialType
        newMaterialTy={st.newMaterialTy}
        toQueue={queueNames.length > 1 ? st.toQueue : (queueNames[0] ?? null)}
        setNewMaterialTy={(ty) => setState({ ...st, newMaterialTy: ty })}
      />
      <PromptForOperator
        enteredOperator={st.enteredOperator}
        setEnteredOperator={(o) => setState({ ...st, enteredOperator: o })}
      />
      {st.invalidateSt !== null ? (
        <InvalidateCycleDialogContent
          st={st.invalidateSt}
          setState={(is) => setState({ ...st, invalidateSt: is })}
        />
      ) : undefined}
    </>
  );
}

export function AddToQueueButton({
  st: { enteredOperator, newMaterialTy, toQueue, invalidateSt },
  setState,
  queueNames,
  onClose,
}: {
  st: AddMaterialState;
  setState: Dispatch<SetStateAction<AddMaterialState>>;
  queueNames: ReadonlyArray<string>;
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

  const allowAdd = useAllowAddToQueue(queueNames);

  if (!allowAdd) {
    return null;
  }

  if (toQueue === null && queueNames.length === 1) {
    toQueue = queueNames[0];
  }

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
    <>
      <InvalidateCycleDialogButton
        onClose={onClose}
        st={invalidateSt}
        setState={(s) => setState((as) => ({ ...as, invalidateSt: s }))}
      />
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
    </>
  );
}
