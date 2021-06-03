/* Copyright (c) 2021, John Lenz

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
import { createStyles } from "@material-ui/core/styles";
import Button from "@material-ui/core/Button";
import List from "@material-ui/core/List";
import ListItem from "@material-ui/core/ListItem";
import ListItemText from "@material-ui/core/ListItemText";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import FormControl from "@material-ui/core/FormControl";
import InputLabel from "@material-ui/core/InputLabel";
import Select from "@material-ui/core/Select";
import MenuItem from "@material-ui/core/MenuItem";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogTitle from "@material-ui/core/DialogTitle";
import Collapse from "@material-ui/core/Collapse";
import CircularProgress from "@material-ui/core/CircularProgress";
import TextField from "@material-ui/core/TextField";
import { makeStyles } from "@material-ui/core/styles";
import ExpandMoreIcon from "@material-ui/icons/ExpandMore";
import Tooltip from "@material-ui/core/Tooltip";
import ReactToPrint from "react-to-print";
import clsx from "clsx";

import { MaterialDetailTitle, MaterialDetailContent, PartIdenticon } from "./Material";
import * as api from "../../data/api";
import { useSelector } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { LazySeq } from "../../data/lazyseq";
import { JobAndGroups, extractJobGroups } from "../../data/queue-material";
import { currentOperator } from "../../data/operators";
import { PrintedLabel } from "./PrintedLabel";
import { atom, useRecoilState, useRecoilValue, useSetRecoilState } from "recoil";
import { fmsInformation } from "../../data/server-settings";
import { currentStatus } from "../../data/current-status";
import { useAddNewCastingToQueue } from "../../data/material-details";

interface ExistingMatInQueueDialogBodyProps {
  readonly display_material: matDetails.MaterialDetail;
  readonly in_proc_material: Readonly<api.IInProcessMaterial>;
}

function ExistingMatInQueueDialogBody(props: ExistingMatInQueueDialogBodyProps) {
  const printRef = React.useRef(null);
  const operator = useRecoilValue(currentOperator);
  const fmsInfo = useRecoilValue(fmsInformation);

  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);
  const [addMat, addingMat] = matDetails.useAddExistingMaterialToQueue();
  const [removeFromQueue, removingFromQueue] = matDetails.useRemoveFromQueue();
  const [printLabel, printingLabel] = matDetails.usePrintLabel();

  const allowRemove = React.useMemo(() => {
    // first, check if can't remove because material is being loaded
    if (
      (fmsInfo.allowQuarantineAtLoadStation ?? false) === false &&
      props.in_proc_material.action.type === api.ActionType.Loading
    ) {
      return false;
    }

    // can't remove if on a pallet
    if (props.in_proc_material.location.type === api.LocType.OnPallet) {
      return false;
    }

    // can remove if no quarantine queue or material is raw material
    return !fmsInfo.quarantineQueue || fmsInfo.quarantineQueue === "" || props.in_proc_material.process === 0;
  }, [props.in_proc_material, fmsInfo]);

  const allowQuarantine = React.useMemo(() => {
    // can't quarantine if there is no queue defined
    if (!fmsInfo.quarantineQueue || fmsInfo.quarantineQueue === "") return null;

    // check if can't remove because material is being loaded
    if (
      (fmsInfo.allowQuarantineAtLoadStation ?? false) === false &&
      props.in_proc_material.action.type === api.ActionType.Loading
    ) {
      return null;
    }

    // can't quarantine if on a pallet
    if (props.in_proc_material.location.type === api.LocType.OnPallet) {
      return false;
    }

    return fmsInfo.quarantineQueue;
  }, [props.in_proc_material, fmsInfo]);

  const matId = props.in_proc_material.materialID;
  return (
    <>
      <DialogTitle disableTypography>
        <MaterialDetailTitle partName={props.display_material.partName} serial={props.display_material.serial} />
      </DialogTitle>
      <DialogContent>
        <MaterialDetailContent mat={props.display_material} />
      </DialogContent>
      <DialogActions>
        {fmsInfo.usingLabelPrinterForSerials ? (
          fmsInfo.useClientPrinterForLabels ? (
            <>
              <ReactToPrint
                content={() => printRef.current}
                copyStyles={false}
                trigger={() => <Button color="primary">Print Label</Button>}
              />
              <div style={{ display: "none" }}>
                <div ref={printRef}>
                  <PrintedLabel material={[props.display_material]} oneJobPerPage={false} />
                </div>
              </div>
            </>
          ) : (
            <Button
              color="primary"
              disabled={printingLabel}
              onClick={() =>
                printLabel({
                  materialId: matId,
                  proc: props.in_proc_material.action.processAfterLoad ?? props.in_proc_material.process,
                  loadStation: null,
                  queue: props.in_proc_material.location.currentQueue ?? null,
                })
              }
            >
              Print Label
            </Button>
          )
        ) : undefined}
        {allowRemove ? (
          <Button
            color="primary"
            disabled={removingFromQueue}
            onClick={() => {
              removeFromQueue(matId, operator);
              setMatToShow(null);
            }}
          >
            Remove From System
          </Button>
        ) : undefined}
        {allowQuarantine ? (
          <Tooltip title={"Move to " + allowQuarantine}>
            <Button
              color="primary"
              disabled={addingMat}
              onClick={() => {
                addMat({
                  materialId: matId,
                  queue: allowQuarantine,
                  queuePosition: 0,
                  operator: operator,
                });
                setMatToShow(null);
              }}
            >
              Quarantine Material
            </Button>
          </Tooltip>
        ) : undefined}
        <Button onClick={() => setMatToShow(null)} color="primary">
          Close
        </Button>
      </DialogActions>
    </>
  );
}

interface AddSerialFoundProps {
  readonly current_quarantine_queue: string | null;
  readonly display_material: matDetails.MaterialDetail;
  readonly queueNames: ReadonlyArray<string>;
}

function AddSerialFound(props: AddSerialFoundProps) {
  const [selected_queue, setSelectedQueue] = React.useState<string | null>(null);
  const operator = useRecoilValue(currentOperator);
  const [matToShow, setMatToShow] = useRecoilState(matDetails.materialToShowInDialog);
  const [addMat, addingMat] = matDetails.useAddExistingMaterialToQueue();

  let queue: string | null;
  const queueDests = props.queueNames.filter((q) => q !== props.current_quarantine_queue);
  let promptForQueue = false;
  if (queueDests.length === 1) {
    queue = queueDests[0];
  } else if (matToShow && matToShow.type === "Serial" && matToShow.addToQueue) {
    queue = matToShow.addToQueue;
  } else {
    queue = selected_queue;
    promptForQueue = true;
  }

  let addProcMsg: string | null = null;
  if (!props.display_material.loading_events) {
    const lastProc = LazySeq.ofIterable(props.display_material.events)
      .filter(
        (e) =>
          e.details?.["PalletCycleInvalidated"] !== "1" &&
          (e.type === api.LogType.LoadUnloadCycle ||
            e.type === api.LogType.MachineCycle ||
            e.type === api.LogType.AddToQueue)
      )
      .flatMap((e) => e.material)
      .filter((m) => m.id === props.display_material.materialID)
      .maxOn((m) => m.proc)
      .map((m) => m.proc)
      .getOrElse(0);
    addProcMsg = " To Run Process " + (lastProc + 1).toString();
  }
  return (
    <>
      <DialogTitle disableTypography>
        <MaterialDetailTitle partName={props.display_material.partName} serial={props.display_material.serial} />
      </DialogTitle>
      <DialogContent>
        {promptForQueue ? (
          <div style={{ marginBottom: "1em" }}>
            <p>Select a queue.</p>
            <List>
              {queueDests.map((q, idx) => (
                <MenuItem key={idx} selected={q === selected_queue} onClick={() => setSelectedQueue(q)}>
                  {q}
                </MenuItem>
              ))}
            </List>
          </div>
        ) : undefined}
        <MaterialDetailContent mat={props.display_material} />
      </DialogContent>
      <DialogActions>
        <Button
          color="primary"
          disabled={props.display_material.loading_events || queue === null || addingMat === true}
          onClick={() => {
            addMat({
              materialId: props.display_material.materialID,
              queue: queue ?? "",
              queuePosition: -1,
              operator: operator,
            });
            setMatToShow(null);
          }}
        >
          {props.current_quarantine_queue !== null ? `Move From ${props.current_quarantine_queue} To` : "Add To"}{" "}
          {queue}
          {addProcMsg}
        </Button>
        <Button onClick={() => setMatToShow(null)} color="primary">
          Cancel
        </Button>
      </DialogActions>
    </>
  );
}

const useSelectJobStyles = makeStyles((theme) =>
  createStyles({
    expand: {
      transform: "rotate(-90deg)",
      transition: theme.transitions.create("transform", {
        duration: theme.transitions.duration.shortest,
      }),
    },
    expandOpen: {
      transform: "rotate(0deg)",
    },
    nested: {
      paddingLeft: theme.spacing(4),
    },
  })
);

interface SelectJobProps {
  readonly queue: string | undefined;
  readonly selected_job?: AddNewJobProcessState;
  readonly onSelectJob: (j: AddNewJobProcessState | undefined) => void;
}

function SelectJob(props: SelectJobProps) {
  const [selectedJob, setSelectedJob] = React.useState<string | null>(null);
  const currentSt = useRecoilValue(currentStatus);
  const jobs: ReadonlyArray<JobAndGroups> = React.useMemo(
    () =>
      LazySeq.ofObject(currentSt.jobs)
        .map(([_uniq, j]) => extractJobGroups(j))
        .sortOn((j) => j.job.partName)
        .toArray(),
    [currentSt.jobs]
  );
  const classes = useSelectJobStyles();

  return (
    <List>
      {jobs.map((j, idx) => (
        <React.Fragment key={idx}>
          <ListItem
            button
            onClick={() => {
              if (props.selected_job) props.onSelectJob(undefined);
              setSelectedJob(selectedJob === j.job.unique ? null : j.job.unique);
            }}
          >
            <ListItemIcon>
              <ExpandMoreIcon
                className={clsx(classes.expand, {
                  [classes.expandOpen]: selectedJob === j.job.unique,
                })}
              />
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
                title={props.queue !== undefined && !p.queues.contains(props.queue) ? "Not used in " + props.queue : ""}
                key={idx}
              >
                <div>
                  <ListItem
                    button
                    className={classes.nested}
                    disabled={props.queue !== undefined && !p.queues.contains(props.queue)}
                    selected={
                      props.selected_job?.job.unique === j.job.unique && props.selected_job?.last_proc === p.lastProc
                    }
                    onClick={() => props.onSelectJob({ job: j.job, last_proc: p.lastProc })}
                  >
                    <ListItemText
                      primary={p.lastProc === 0 ? "Raw Material" : "Last machined process " + p.lastProc.toString()}
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

interface AddNewMaterialProps {
  readonly queueNames: ReadonlyArray<string>;
  readonly not_found_serial?: string;
  readonly addToQueue?: string;
  readonly onClose: () => void;
}

interface AddNewJobProcessState {
  readonly job: Readonly<api.IActiveJob>;
  readonly last_proc: number;
}

function AddNewMaterialBody(props: AddNewMaterialProps) {
  const selectedOperator = useRecoilValue(currentOperator);
  const fmsInfo = useRecoilValue(fmsInformation);
  const [selected_job, setSelectedJob] = React.useState<AddNewJobProcessState | undefined>(undefined);
  const [selected_queue, setSelectedQueue] = React.useState<string | null>(null);
  const [enteredOperator, setEnteredOperator] = React.useState<string | null>(null);
  const [addAssigned, addingAssigned] = matDetails.useAddNewMaterialToQueue();

  function addMaterial(queue: string | null) {
    if (queue === null) {
      return;
    }

    if (selected_job !== undefined) {
      addAssigned({
        jobUnique: selected_job.job.unique,
        lastCompletedProcess: selected_job.last_proc,
        queue: queue,
        queuePosition: -1,
        serial: props.not_found_serial,
        operator: fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? enteredOperator || null : selectedOperator,
      });
      setSelectedJob(undefined);
      setSelectedQueue(null);
      setEnteredOperator(null);
    }
  }

  let queue: string | null;
  let promptForQueue = false;
  if (props.queueNames.length === 1) {
    queue = props.queueNames[0];
  } else if (props.addToQueue) {
    queue = props.addToQueue;
  } else {
    queue = selected_queue;
    promptForQueue = true;
  }

  const allowAdd =
    selected_job !== undefined &&
    addingAssigned === false &&
    (!fmsInfo.requireOperatorNamePromptWhenAddingMaterial || (enteredOperator && enteredOperator !== ""));

  return (
    <>
      <DialogTitle>{props.not_found_serial ? props.not_found_serial : "Add New Material"}</DialogTitle>
      <DialogContent>
        {props.not_found_serial ? (
          <p>The serial {props.not_found_serial} was not found. Specify the job and process to add to the queue.</p>
        ) : undefined}
        <div style={{ display: "flex" }}>
          {promptForQueue ? (
            <div style={{ marginRight: "1em" }}>
              <p>Select a queue.</p>
              <List>
                {props.queueNames.map((q, idx) => (
                  <MenuItem key={idx} selected={q === selected_queue} onClick={() => setSelectedQueue(q)}>
                    {q}
                  </MenuItem>
                ))}
              </List>
            </div>
          ) : undefined}
          <SelectJob selected_job={selected_job} onSelectJob={setSelectedJob} queue={queue ?? undefined} />
          {fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? (
            <div style={{ marginLeft: "1em" }}>
              <TextField
                fullWidth
                label="Operator"
                value={enteredOperator || ""}
                onChange={(e) => setEnteredOperator(e.target.value)}
              />
            </div>
          ) : undefined}
        </div>
      </DialogContent>
      <DialogActions>
        <Button color="primary" onClick={() => addMaterial(queue)} disabled={!allowAdd}>
          Add To {queue}
        </Button>
        <Button onClick={props.onClose} color="primary">
          Cancel
        </Button>
      </DialogActions>
    </>
  );
}

function MaterialMissingError() {
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);
  const mat = useRecoilValue(matDetails.materialDetail);
  return (
    <>
      <DialogContent>
        {mat && mat.serial ? <p>The serial {mat.serial} was not found.</p> : <p>The material was not found.</p>}
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setMatToShow(null)} color="primary">
          Close
        </Button>
      </DialogActions>
    </>
  );
}

type CurrentlyInQueue =
  | { readonly type: "NotInQueue" }
  | { readonly type: "OnPallet"; readonly inProcMat: Readonly<api.IInProcessMaterial> }
  | { readonly type: "InRegularQueue"; readonly inProcMat: Readonly<api.IInProcessMaterial> }
  | { readonly type: "InQuarantine"; readonly inProcMat: Readonly<api.IInProcessMaterial>; readonly queue: string };

function matCurrentlyInQueue(
  mat: matDetails.MaterialDetail | null,
  currentSt: Readonly<api.ICurrentStatus>
): CurrentlyInQueue {
  if (mat === null || !currentSt || !currentSt.material) {
    return { type: "NotInQueue" };
  }
  if (mat.materialID < 0) {
    return { type: "NotInQueue" };
  }
  const activeQueues = LazySeq.ofObject(currentSt.jobs)
    .flatMap(([_, job]) => job.procsAndPaths)
    .flatMap((proc) => proc.paths)
    .flatMap((path) => {
      const q: string[] = [];
      if (path.inputQueue !== undefined) q.push(path.inputQueue);
      if (path.outputQueue !== undefined) q.push(path.outputQueue);
      return q;
    })
    .toSet((x) => x);
  for (const inProcMat of currentSt.material) {
    if (inProcMat.materialID === mat.materialID) {
      if (inProcMat.location.type === api.LocType.InQueue && !!inProcMat.location.currentQueue) {
        if (activeQueues.contains(inProcMat.location.currentQueue)) {
          return { type: "InRegularQueue", inProcMat };
        } else {
          return { type: "InQuarantine", inProcMat, queue: inProcMat.location.currentQueue };
        }
      } else if (inProcMat.location.type === api.LocType.OnPallet) {
        return { type: "OnPallet", inProcMat };
      } else {
        return { type: "NotInQueue" };
      }
    }
  }
  return { type: "NotInQueue" };
}

export interface QueueMaterialDialogProps {
  readonly queueNames: ReadonlyArray<string>;
}

export const QueueMaterialDialog = React.memo(function QueueMaterialDialog(props: QueueMaterialDialogProps) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const currentSt = useRecoilValue(currentStatus);
  const displayMat = useRecoilValue(matDetails.materialDetail);
  const [matToShow, setMatToShow] = useRecoilState(matDetails.materialToShowInDialog);

  const matInQueue = React.useMemo(() => matCurrentlyInQueue(displayMat, currentSt), [displayMat, currentSt]);

  let body: JSX.Element | undefined;

  if (displayMat === null) {
    body = <p>None</p>;
  } else {
    if (matInQueue.type === "InRegularQueue" || matInQueue.type === "OnPallet") {
      body = <ExistingMatInQueueDialogBody display_material={displayMat} in_proc_material={matInQueue.inProcMat} />;
    } else if (displayMat.materialID >= 0 || displayMat.loading_events) {
      body = (
        <AddSerialFound
          current_quarantine_queue={matInQueue.type === "InQuarantine" ? matInQueue.queue : null}
          display_material={displayMat}
          queueNames={props.queueNames}
        />
      );
    } else if (fmsInfo.requireExistingMaterialWhenAddingToQueue) {
      body = <MaterialMissingError />;
    } else {
      body = (
        <AddNewMaterialBody
          not_found_serial={displayMat.serial}
          addToQueue={matToShow && matToShow.type === "Serial" ? matToShow.addToQueue : undefined}
          onClose={() => setMatToShow(null)}
          queueNames={props.queueNames}
        />
      );
    }
  }

  return (
    <Dialog open={displayMat !== null} onClose={() => setMatToShow(null)} maxWidth="lg">
      {body}
    </Dialog>
  );
});

export const addMaterialBySerial = atom<string | null>({
  key: "add-by-serial-dialog",
  default: null,
});

export const AddBySerialDialog = React.memo(function AddBySerialDialog() {
  const [queue, setQueue] = useRecoilState(addMaterialBySerial);
  const [serial, setSerial] = React.useState<string | undefined>(undefined);
  const setMatToDisplay = useSetRecoilState(matDetails.materialToShowInDialog);

  function lookup() {
    if (serial && serial !== "") {
      setMatToDisplay({ type: "Serial", serial, addToQueue: queue ?? undefined });
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

export const addMaterialWithoutSerial = atom<string | null>({
  key: "add-material-without-serial-dialog",
  default: null,
});

export const AddWithoutSerialDialog = React.memo(function AddWithoutSerialDialog(props: {
  readonly queueNames: ReadonlyArray<string>;
}) {
  const [queue, setQueue] = useRecoilState(addMaterialWithoutSerial);
  return (
    <Dialog open={queue !== null} onClose={() => setQueue(null)} maxWidth="md">
      <AddNewMaterialBody
        addToQueue={queue ?? undefined}
        onClose={() => setQueue(null)}
        queueNames={props.queueNames}
      />
    </Dialog>
  );
});

export const bulkAddCastingToQueue = atom<string | null>({
  key: "bulk-add-casting-dialog",
  default: null,
});

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
  const [materialToPrint, setMaterialToPrint] =
    React.useState<ReadonlyArray<Readonly<api.IInProcessMaterial>> | null>(null);
  const printRef = React.useRef(null);
  const [adding, setAdding] = React.useState<boolean>(false);
  const castingNames = useSelector((s) => s.Events.last30.sim_use.castingNames);
  const castings: ReadonlyArray<[string, number]> = React.useMemo(
    () =>
      LazySeq.ofObject(currentSt.jobs)
        .flatMap(([, j]) => j.procsAndPaths[0].paths)
        .filter((p) => p.casting !== undefined && p.casting !== "")
        .map((p) => ({ casting: p.casting as string, cnt: 1 }))
        .appendAll(LazySeq.ofIterable(castingNames).map((c) => ({ casting: c, cnt: 0 })))
        .toMap(
          (c) => [c.casting, c.cnt],
          (q1, q2) => q1 + q2
        )
        .toVector()
        .sortBy(([c1, q1], [c2, q2]) => {
          if (q1 === 0 && q2 != 0) {
            return 1; // put non-zero quantities first
          } else if (q1 !== 0 && q2 == 0) {
            return -1;
          } else {
            return c1.localeCompare(c2);
          }
        })
        .toArray(),
    [currentSt.jobs, castingNames]
  );

  function close() {
    setQueue(null);
    setSelectedCasting(null);
    setEnteredOperator(null);
    setMaterialToPrint(null);
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

  function addAndPrint(): Promise<void> {
    return new Promise((resolve, reject) => {
      if (queue !== null && selectedCasting !== null && qty !== null && !isNaN(qty)) {
        setAdding(true);
        addNewCasting({
          casting: selectedCasting,
          quantity: qty,
          queue: queue,
          operator: fmsInfo.requireOperatorNamePromptWhenAddingMaterial ? enteredOperator : operator,
          onNewMaterial: (mats) => {
            setMaterialToPrint(mats);
            resolve();
          },
          onError: (reason) => {
            close();
            reject(reason);
          },
        });
      } else {
        close();
      }
    });
  }

  return (
    <>
      <Dialog open={queue !== null} onClose={() => (adding && printOnAdd ? undefined : close())}>
        <DialogTitle>Add Raw Material</DialogTitle>
        <DialogContent>
          <FormControl>
            <InputLabel id="select-casting-label">Raw Material</InputLabel>
            <Select
              style={{ minWidth: "15em" }}
              labelId="select-casting-label"
              value={selectedCasting || ""}
              onChange={(e) => setSelectedCasting(e.target.value as string)}
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
                    secondary={jobCnt === 0 ? "Not used by any current jobs" : `Used by ${jobCnt} current jobs`}
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
            <ReactToPrint
              onBeforeGetContent={addAndPrint}
              onAfterPrint={close}
              copyStyles={false}
              content={() => printRef.current}
              trigger={() => (
                <Button
                  color="primary"
                  disabled={
                    selectedCasting === null ||
                    adding ||
                    qty === null ||
                    isNaN(qty) ||
                    (fmsInfo.requireOperatorNamePromptWhenAddingMaterial &&
                      (enteredOperator === null || enteredOperator === ""))
                  }
                >
                  {adding ? <CircularProgress size={10} /> : undefined}
                  Add to {queue}
                </Button>
              )}
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
      <div style={{ display: "none" }}>
        <div ref={printRef}>
          <PrintedLabel
            materialName={selectedCasting}
            material={materialToPrint}
            operator={enteredOperator}
            oneJobPerPage={true}
          />
        </div>
      </div>
    </>
  );
});
