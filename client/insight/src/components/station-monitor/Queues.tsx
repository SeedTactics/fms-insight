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

/* eslint-disable react/prop-types */
import * as React from "react";
import { makeStyles } from "@material-ui/core";
import { withStyles } from "@material-ui/core";
import { createStyles } from "@material-ui/core";
import { WithStyles } from "@material-ui/core";
import { SortEnd } from "react-sortable-hoc";
import { Table } from "@material-ui/core";
import { TableHead } from "@material-ui/core";
import { TableCell } from "@material-ui/core";
import { TableRow } from "@material-ui/core";
import { TableBody } from "@material-ui/core";
import { Button } from "@material-ui/core";
import { Tooltip } from "@material-ui/core";
import { Typography } from "@material-ui/core";
import EditIcon from "@material-ui/icons/Edit";
import { Dialog } from "@material-ui/core";
import { DialogTitle } from "@material-ui/core";
import { DialogContent } from "@material-ui/core";
import KeyboardArrowDownIcon from "@material-ui/icons/KeyboardArrowDown";
import KeyboardArrowUpIcon from "@material-ui/icons/KeyboardArrowUp";
import { TextField } from "@material-ui/core";
import { DialogActions } from "@material-ui/core";
import { Fab } from "@material-ui/core";
import { IconButton } from "@material-ui/core";
import { CircularProgress } from "@material-ui/core";
import AddIcon from "@material-ui/icons/Add";
import AssignIcon from "@material-ui/icons/AssignmentReturn";

import {
  SortableInProcMaterial,
  SortableWhiteboardRegion,
  PartIdenticon,
  MultiMaterial,
  InProcMaterial,
  MaterialDetailTitle,
} from "./Material";
import * as api from "../../network/api";
import {
  QueueMaterialDialog,
  AddBySerialDialog,
  BulkAddCastingWithoutSerialDialog,
  AddWithoutSerialDialog,
  addMaterialBySerial,
  bulkAddCastingToQueue,
  addMaterialWithoutSerial,
} from "./QueuesAddMaterial";
import {
  selectQueueData,
  extractJobRawMaterial,
  loadRawMaterialEvents,
  JobRawMaterialData,
} from "../../data/queue-material";
import { LogEntries } from "../LogEntry";
import { JobsBackend, BackendUrl } from "../../network/backend";
import { LazySeq } from "../../util/lazyseq";
import { currentOperator } from "../../data/operators";
import ReactToPrint from "react-to-print";
import { PrintedLabel } from "./PrintedLabel";
import { JobDetails } from "./JobDetails";
import { atom, useRecoilValue, useSetRecoilState } from "recoil";
import { fmsInformation } from "../../network/server-settings";
import {
  currentStatus,
  currentStatusJobComment,
  reorderQueuedMatInCurrentStatus,
} from "../../cell-status/current-status";
import { useAddExistingMaterialToQueue, usePrintLabel } from "../../cell-status/material-details";
import { Collapse } from "@material-ui/core";
import { rawMaterialQueues } from "../../cell-status/names";
import { useRecoilConduit } from "../../util/recoil-util";

const useTableStyles = makeStyles(() =>
  createStyles({
    mainRow: {
      "& > *": {
        borderBottom: "unset",
      },
    },
    labelContainer: {
      display: "flex",
      alignItems: "center",
    },
    identicon: {
      marginRight: "0.2em",
    },
    pathDetails: {
      maxWidth: "20em",
    },
    highlightedRow: {
      backgroundColor: "#FF8A65",
    },
    noncompletedRow: {
      backgroundColor: "#E0E0E0",
    },
    collapseCell: {
      paddingBottom: 0,
      paddingTop: 0,
    },
  })
);

const highlightedComments = [/\bhold\b/, /\bmissing\b/, /\bwait\b/, /\bwaiting\b/, /\bnone\b/];

function highlightRow(j: Readonly<api.IActiveJob>): boolean {
  const comment = j.comment;
  if (!comment || comment === "") return false;
  return LazySeq.ofIterable(highlightedComments).anyMatch((r) => r.test(comment));
}

export interface RawMaterialJobRowProps {
  readonly job: JobRawMaterialData;
  readonly editNote: (job: Readonly<api.IActiveJob>) => void;
  readonly editQty: (job: JobRawMaterialData) => void;
}

function RawMaterialJobRow(props: RawMaterialJobRowProps) {
  const classes = useTableStyles();
  const allowEditQty = (useRecoilValue(fmsInformation).allowEditJobPlanQuantityFromQueuesPage ?? null) != null;
  const [open, setOpen] = React.useState<boolean>(false);

  const j = props.job;
  const bgClass = highlightRow(j.job)
    ? classes.highlightedRow
    : j.plannedQty - j.startedQty - j.assignedRaw > 0
    ? classes.noncompletedRow
    : undefined;

  return (
    <>
      <TableRow className={bgClass ? classes.mainRow + " " + bgClass : classes.mainRow}>
        <TableCell>
          <div className={classes.labelContainer}>
            <div className={classes.identicon}>
              <PartIdenticon part={j.job.partName} size={j.pathDetails === null ? 25 : 40} />
            </div>
            <div>
              <Typography variant="body2" component="span" display="block">
                {j.job.unique}
              </Typography>
              {j.pathDetails !== null ? (
                <Typography variant="body2" color="textSecondary" display="block" className={classes.pathDetails}>
                  {j.pathDetails}
                </Typography>
              ) : undefined}
            </div>
          </div>
        </TableCell>
        <TableCell>{j.path.simulatedStartingUTC.toLocaleString()}</TableCell>
        <TableCell>
          {j.rawMatName === j.job.partName ? (
            j.rawMatName
          ) : (
            <div className={classes.labelContainer}>
              <div className={classes.identicon}>
                <PartIdenticon part={j.rawMatName} size={25} />
              </div>
              <Typography variant="body2" display="block">
                {j.rawMatName}
              </Typography>
            </div>
          )}
        </TableCell>
        <TableCell>
          {j.job.comment}

          <Tooltip title="Edit">
            <IconButton size="small" onClick={() => props.editNote(j.job)}>
              <EditIcon />
            </IconButton>
          </Tooltip>
        </TableCell>
        <TableCell align="right">
          {j.plannedQty}
          {allowEditQty ? (
            <Tooltip title="Edit">
              <IconButton size="small" onClick={() => props.editQty(j)}>
                <EditIcon />
              </IconButton>
            </Tooltip>
          ) : undefined}
        </TableCell>
        <TableCell align="right">{j.startedQty}</TableCell>
        <TableCell align="right">{j.assignedRaw}</TableCell>
        <TableCell align="right">
          <Tooltip
            title={j.startedQty > 0 || j.assignedRaw > 0 ? `${j.plannedQty} - ${j.startedQty} - ${j.assignedRaw}` : ""}
          >
            <span>{j.plannedQty - j.startedQty - j.assignedRaw}</span>
          </Tooltip>
        </TableCell>
        <TableCell align="right">{j.availableUnassigned}</TableCell>
        <TableCell>
          <Tooltip title="Show Details">
            <IconButton size="small" onClick={() => setOpen(!open)}>
              {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
            </IconButton>
          </Tooltip>
        </TableCell>
      </TableRow>
      <TableRow className={bgClass}>
        <TableCell className={classes.collapseCell} colSpan={10}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <JobDetails job={j.job} checkAnalysisMonth={false} />
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

interface RawMaterialJobTableProps {
  readonly queue: string;
  readonly editNote: (job: Readonly<api.IActiveJob>) => void;
  readonly editQty: (job: JobRawMaterialData) => void;
}

function RawMaterialJobTable(props: RawMaterialJobTableProps) {
  const currentSt = useRecoilValue(currentStatus);
  const jobs = React.useMemo(
    () => extractJobRawMaterial(props.queue, currentSt.jobs, currentSt.material),
    [props.queue, currentSt]
  );

  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          <TableCell>Job</TableCell>
          <TableCell>Starting Time</TableCell>
          <TableCell>Material</TableCell>
          <TableCell>Note</TableCell>
          <TableCell align="right">Planned Quantity</TableCell>
          <TableCell align="right">Started Quantity</TableCell>
          <TableCell align="right">Assigned Raw Material</TableCell>
          <TableCell align="right">Required</TableCell>
          <TableCell align="right">Available Unassigned</TableCell>
          <TableCell />
        </TableRow>
      </TableHead>
      <TableBody>
        {jobs.map((j, idx) => (
          <RawMaterialJobRow key={idx} job={j} editNote={props.editNote} editQty={props.editQty} />
        ))}
      </TableBody>
    </Table>
  );
}

interface EditNoteDialogProps {
  readonly job: { readonly unique: string; readonly partName: string; readonly comment?: string | null } | null;
  readonly closeDialog: () => void;
}

const nullCommentAtom = atom<string | null>({ key: "null-comment-atom", default: null });

export const EditNoteDialog = React.memo(function EditNoteDialog(props: EditNoteDialogProps) {
  const [note, setNote] = React.useState<string | null>(null);
  const setJobComment = useSetRecoilState(props.job ? currentStatusJobComment(props.job.unique) : nullCommentAtom);

  function close() {
    props.closeDialog();
    setNote(null);
  }

  function save() {
    if (note === null || props.job === null || note === props.job.comment) return;
    setJobComment(note);
    close();
  }

  return (
    <Dialog open={props.job !== null} onClose={close}>
      {props.job !== null ? (
        <>
          <DialogTitle>
            <div style={{ display: "flex", alignItems: "center" }}>
              <div>
                <PartIdenticon part={props.job.partName} size={40} />
              </div>
              <div style={{ marginLeft: "1em", flexGrow: 1 }}>Edit Note For {props.job.unique}</div>
            </div>
          </DialogTitle>
          <DialogContent>
            <TextField
              variant="outlined"
              fullWidth
              autoFocus
              value={note === null ? props.job.comment || "" : note}
              onChange={(e) => setNote(e.target.value)}
              onKeyDown={(e) => {
                if (e.keyCode === 13 && note !== null) {
                  save();
                  e.preventDefault();
                }
              }}
            />
          </DialogContent>
          <DialogActions>
            <Button color="primary" disabled={note === null} onClick={save}>
              Save Note
            </Button>
            <Button color="primary" onClick={close}>
              Cancel
            </Button>
          </DialogActions>
        </>
      ) : undefined}
    </Dialog>
  );
});

interface EditJobPlanQtyProps {
  readonly job: JobRawMaterialData | null;
  readonly closeDialog: () => void;
}

const EditJobPlanQtyDialog = React.memo(function EditJobPlanQtyProps(props: EditJobPlanQtyProps) {
  const [running, setRunning] = React.useState(false);
  const [newQty, setNewQty] = React.useState<number | null>(null);
  const allowEditQtyUrl = useRecoilValue(fmsInformation).allowEditJobPlanQuantityFromQueuesPage ?? null;

  function close() {
    if (running) return;
    props.closeDialog();
    setNewQty(null);
  }

  async function setQty() {
    if (allowEditQtyUrl === null || props.job === null || newQty == null || isNaN(newQty)) {
      return;
    }
    setRunning(true);
    try {
      await fetch((BackendUrl ?? "") + allowEditQtyUrl, {
        method: "PUT",
        headers: new Headers({
          "Content-Type": "application/json",
        }),
        body: JSON.stringify({
          Unique: props.job.job.unique,
          Proc1Path: props.job.proc1Path,
          Quantity: newQty,
        }),
      });
      props.closeDialog();
      setNewQty(null);
    } finally {
      setRunning(false);
    }
  }

  return (
    <Dialog open={allowEditQtyUrl != null && props.job !== null} onClose={close}>
      {props.job !== null ? (
        <>
          <DialogTitle>
            <div style={{ display: "flex", alignItems: "center" }}>
              <div>
                <PartIdenticon part={props.job.job.partName} size={40} />
              </div>
              <div style={{ marginLeft: "1em", flexGrow: 1 }}>Edit Planned Quantity For {props.job.job.unique}</div>
            </div>
          </DialogTitle>
          <DialogContent>
            <p>
              {props.job.plannedQty} currently planned, {props.job.startedQty} started
            </p>
            <TextField
              variant="outlined"
              fullWidth
              autoFocus
              inputProps={{ style: { textAlign: "right" }, min: props.job.startedQty }}
              type="number"
              value={newQty === null ? "" : newQty}
              onChange={(e) => setNewQty(parseInt(e.target.value))}
            />
          </DialogContent>
          <DialogActions>
            <Button color="primary" disabled={running || newQty === null || isNaN(newQty)} onClick={setQty}>
              {running ? <CircularProgress size={10} /> : undefined}
              Set Quantity
            </Button>
            <Button color="primary" disabled={running} onClick={close}>
              Cancel
            </Button>
          </DialogActions>
        </>
      ) : undefined}
    </Dialog>
  );
});

interface MultiMaterialDialogProps {
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>> | null;
  readonly closeDialog: () => void;
  readonly operator: string | null;
}

const MultiMaterialDialog = React.memo(function MultiMaterialDialog(props: MultiMaterialDialogProps) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const jobs = useRecoilValue(currentStatus).jobs;
  const [printLabel, printingLabel] = usePrintLabel();

  const [loading, setLoading] = React.useState(false);
  const [events, setEvents] = React.useState<ReadonlyArray<Readonly<api.ILogEntry>>>([]);
  const [showRemove, setShowRemove] = React.useState(false);
  const [removeCnt, setRemoveCnt] = React.useState<number>(NaN);
  const [lastOperator, setLastOperator] = React.useState<string | undefined>(undefined);
  const printRef = React.useRef(null);

  React.useEffect(() => {
    if (props.material === null) return;
    let isSubscribed = true;
    setLoading(true);
    loadRawMaterialEvents(props.material)
      .then((events) => {
        if (isSubscribed) {
          setEvents(events);
          setLastOperator(
            LazySeq.ofIterable(events)
              .filter((e) => e.type === api.LogType.AddToQueue && e.details?.["operator"] !== undefined)
              .last()
              .map((e) => e.details?.["operator"] ?? undefined)
              .getOrUndefined()
          );
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
    return LazySeq.ofIterable(jobs[uniq].procsAndPaths[0].paths)
      .filter((p) => p.casting !== undefined && p.casting !== "")
      .head()
      .map((p) => p.casting)
      .getOrUndefined();
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
          LazySeq.ofIterable(props.material || [])
            .take(removeCnt)
            .map((m) => m.materialID)
            .toArray()
        ).finally(close);
      }
    } else {
      setShowRemove(true);
    }
  }

  const mat1 = props.material?.[0];
  return (
    <Dialog open={props.material !== null} onClose={close} maxWidth="md">
      <DialogTitle disableTypography>
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
            <>
              <ReactToPrint
                content={() => printRef.current}
                trigger={() => <Button color="primary">Print Label</Button>}
                copyStyles={false}
              />
              <div style={{ display: "none" }}>
                <div ref={printRef}>
                  <PrintedLabel
                    materialName={rawMatName}
                    material={props.material || []}
                    operator={lastOperator}
                    oneJobPerPage={false}
                  />
                </div>
              </div>
            </>
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

interface AddMaterialButtonsProps {
  readonly label: string;
  readonly rawMatQueue: boolean;
}

const AddMaterialButtons = React.memo(function AddMaterialButtons(props: AddMaterialButtonsProps) {
  const currentJobs = useRecoilValue(currentStatus).jobs;
  const fmsInfo = useRecoilValue(fmsInformation);
  const setAddBySerial = useSetRecoilState(addMaterialBySerial);
  const setBulkAddCastings = useSetRecoilState(bulkAddCastingToQueue);
  const setAddWithoutSerial = useSetRecoilState(addMaterialWithoutSerial);

  const jobExistsWithInputQueue = React.useMemo(() => {
    return LazySeq.ofObject(currentJobs)
      .flatMap(([, j]) => j.procsAndPaths)
      .flatMap((p) => p.paths)
      .anyMatch((p) => p.inputQueue === props.label);
  }, [currentJobs]);

  if (props.rawMatQueue) {
    return (
      <Tooltip title="Add Raw Material">
        <Fab
          color="secondary"
          onClick={() => {
            if (fmsInfo.requireSerialWhenAddingMaterialToQueue) {
              setAddBySerial(props.label);
            } else if (fmsInfo.addRawMaterialAsUnassigned) {
              setBulkAddCastings(props.label);
            } else {
              setAddWithoutSerial(props.label);
            }
          }}
          size="large"
          style={{ marginBottom: "-30px", zIndex: 1 }}
        >
          <AddIcon />
        </Fab>
      </Tooltip>
    );
  } else if (jobExistsWithInputQueue) {
    return (
      <>
        {fmsInfo.requireSerialWhenAddingMaterialToQueue ? undefined : (
          <Tooltip title="Add Material Without Serial">
            <IconButton
              onClick={() => setAddWithoutSerial(props.label)}
              size="medium"
              style={{ marginBottom: "-20px", zIndex: 1 }}
            >
              <AssignIcon fontSize="inherit" />
            </IconButton>
          </Tooltip>
        )}
        <Tooltip title="Add By Serial">
          <IconButton
            onClick={() => setAddBySerial(props.label)}
            size="medium"
            style={{ marginBottom: "-20px", zIndex: 1 }}
          >
            <AddIcon fontSize="inherit" />
          </IconButton>
        </Tooltip>
      </>
    );
  } else {
    return null;
  }
});

const queueStyles = createStyles({
  mainScrollable: {
    padding: "8px",
    width: "100%",
  },
});

interface QueueProps {
  readonly queues: ReadonlyArray<string>;
  readonly showFree: boolean;
}

export const Queues = withStyles(queueStyles)((props: QueueProps & WithStyles<typeof queueStyles>) => {
  const operator = useRecoilValue(currentOperator);
  const currentSt = useRecoilValue(currentStatus);
  const reorderQueuedMat = useRecoilConduit(reorderQueuedMatInCurrentStatus);
  const rawMatQueues = useRecoilValue(rawMaterialQueues);
  const data = React.useMemo(
    () => selectQueueData(props.showFree, props.queues, currentSt, rawMatQueues),
    [currentSt, props.queues, props.showFree, rawMatQueues]
  );

  const [changeNoteForJob, setChangeNoteForJob] = React.useState<Readonly<api.IActiveJob> | null>(null);
  const closeChangeNoteDialog = React.useCallback(() => setChangeNoteForJob(null), []);
  const [editQtyForJob, setEditQtyForJob] = React.useState<JobRawMaterialData | null>(null);
  const closeEditJobQtyDialog = React.useCallback(() => setEditQtyForJob(null), []);
  const [multiMaterialDialog, setMultiMaterialDialog] = React.useState<ReadonlyArray<
    Readonly<api.IInProcessMaterial>
  > | null>(null);
  const closeMultiMatDialog = React.useCallback(() => setMultiMaterialDialog(null), []);
  const [addExistingMatToQueue] = useAddExistingMaterialToQueue();

  return (
    <div data-testid="stationmonitor-queues" className={props.classes.mainScrollable}>
      {data.map((region, idx) => (
        <div style={idx < data.length - 1 ? { borderBottom: "1px solid rgba(0,0,0,0.12)" } : undefined} key={idx}>
          <SortableWhiteboardRegion
            axis="xy"
            label={region.label}
            flexStart
            addMaterialButton={
              region.free ? undefined : (
                <AddMaterialButtons label={region.label} rawMatQueue={region.rawMaterialQueue} />
              )
            }
            distance={5}
            shouldCancelStart={() => false}
            onSortEnd={(se: SortEnd) => {
              addExistingMatToQueue({
                materialId: region.material[se.oldIndex].materialID,
                queue: region.label,
                queuePosition: se.newIndex,
                operator: operator,
              });
              reorderQueuedMat({
                queue: region.label,
                matId: region.material[se.oldIndex].materialID,
                newIdx: se.newIndex,
              });
            }}
          >
            {region.material.map((m, matIdx) => (
              <SortableInProcMaterial
                key={matIdx}
                index={matIdx}
                mat={m}
                hideEmptySerial
                displayJob={region.rawMaterialQueue}
              />
            ))}
            {region.groupedRawMat && region.groupedRawMat.length > 0
              ? region.groupedRawMat.map((matGroup, idx) =>
                  matGroup.material.length === 1 ? (
                    <InProcMaterial
                      key={idx}
                      mat={matGroup.material[0]}
                      hideEmptySerial
                      displayJob={region.rawMaterialQueue}
                    />
                  ) : (
                    <MultiMaterial
                      key={idx}
                      partOrCasting={matGroup.partOrCasting}
                      assignedJobUnique={matGroup.assignedJobUnique}
                      material={matGroup.material}
                      onOpen={() => setMultiMaterialDialog(matGroup.material)}
                    />
                  )
                )
              : undefined}
            {region.rawMaterialQueue ? (
              <div style={{ margin: "1em 5em 1em 5em", width: "100%" }}>
                <RawMaterialJobTable queue={region.label} editNote={setChangeNoteForJob} editQty={setEditQtyForJob} />
              </div>
            ) : undefined}
          </SortableWhiteboardRegion>
        </div>
      ))}
      <QueueMaterialDialog queueNames={props.queues} />
      <AddBySerialDialog />
      <AddWithoutSerialDialog queueNames={props.queues} />
      <BulkAddCastingWithoutSerialDialog />
      <EditNoteDialog job={changeNoteForJob} closeDialog={closeChangeNoteDialog} />
      <EditJobPlanQtyDialog job={editQtyForJob} closeDialog={closeEditJobQtyDialog} />
      <MultiMaterialDialog material={multiMaterialDialog} closeDialog={closeMultiMatDialog} operator={operator} />
    </div>
  );
});

export default function QueuesPage(props: QueueProps): JSX.Element {
  React.useEffect(() => {
    document.title = "Material Queues - FMS Insight";
  }, []);

  return (
    <main>
      <Queues {...props} />
    </main>
  );
}
