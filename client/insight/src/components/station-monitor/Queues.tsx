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

/* eslint-disable react/prop-types */
import * as React from "react";
import { Table, Box, styled } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableBody } from "@mui/material";
import { Button } from "@mui/material";
import { Tooltip } from "@mui/material";
import { Typography } from "@mui/material";
import { Dialog } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { DialogContent } from "@mui/material";
import { TextField } from "@mui/material";
import { DialogActions } from "@mui/material";
import { Fab } from "@mui/material";
import { IconButton } from "@mui/material";
import { CircularProgress } from "@mui/material";

import {
  Add as AddIcon,
  AssignmentReturn as AssignIcon,
  KeyboardArrowDown as KeyboardArrowDownIcon,
  Edit as EditIcon,
  KeyboardArrowUp as KeyboardArrowUpIcon,
} from "@mui/icons-material";

import {
  SortableInProcMaterial,
  PartIdenticon,
  MultiMaterial,
  InProcMaterial,
  DragOverlayInProcMaterial,
} from "./Material.js";
import * as api from "../../network/api.js";
import {
  BulkAddCastingWithoutSerialDialog,
  bulkAddCastingToQueue,
  enterSerialForNewMaterialDialog,
  AddBySerialDialog,
} from "./QueuesAddMaterial.js";
import { selectQueueData, extractJobRawMaterial, JobRawMaterialData } from "../../data/queue-material.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentOperator } from "../../data/operators.js";
import { JobDetails } from "./JobDetails.js";
import { fmsInformation } from "../../network/server-settings.js";
import { addWorkorderComment, currentStatus, setJobComment } from "../../cell-status/current-status.js";
import { Collapse } from "@mui/material";
import { rawMaterialQueues } from "../../cell-status/names.js";
import { SortableRegion } from "./Whiteboard.js";
import { MultiMaterialDialog, QueuedMaterialDialog } from "./QueuesMatDialog.js";
import { useSetTitle } from "../routes.js";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";

const JobTableRow = styled(TableRow, { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })<{
  $noBorderBottom?: boolean;
  $highlightedRow?: boolean;
  $noncompletedRow?: boolean;
}>(({ $noBorderBottom, $highlightedRow, $noncompletedRow }) => ({
  ...($noBorderBottom && {
    "& > *": {
      borderBottom: "unset",
    },
  }),
  backgroundColor: $highlightedRow ? "#FF8A65" : $noncompletedRow ? "#E0E0E0" : undefined,
}));

const highlightedComments = [/\bhold\b/, /\bmissing\b/, /\bwait\b/, /\bwaiting\b/, /\bnone\b/];

function highlightRow(j: Readonly<api.IActiveJob>): boolean {
  const comment = j.comment;
  if (!comment || comment === "") return false;
  return LazySeq.of(highlightedComments).anyMatch((r) => r.test(comment));
}

export interface RawMaterialJobRowProps {
  readonly job: JobRawMaterialData;
  readonly editNote: (job: Readonly<api.IActiveJob>) => void;
  readonly editQty: (job: JobRawMaterialData) => void;
}

function RawMaterialJobRow(props: RawMaterialJobRowProps) {
  const allowEditQty = (useAtomValue(fmsInformation).allowEditJobPlanQuantityFromQueuesPage ?? null) != null;
  const [open, setOpen] = React.useState<boolean>(false);

  const j = props.job;
  const highlRow = highlightRow(j.job);

  return (
    <>
      <JobTableRow
        $noBorderBottom
        $highlightedRow={highlRow}
        $noncompletedRow={j.remainingToStart - j.assignedRaw > 0}
      >
        <TableCell>
          <Box
            sx={{
              display: "flex",
              alignItems: "center",
            }}
          >
            <Box sx={{ mr: "0.2em" }}>
              <PartIdenticon part={j.job.partName} size={25} />
            </Box>
            <div>
              <Typography variant="body2" component="span" display="block">
                {j.job.unique}
              </Typography>
            </div>
          </Box>
        </TableCell>
        <TableCell>{j.startingTime ? j.startingTime.toLocaleString() : ""}</TableCell>
        <TableCell>
          {j.rawMatName === j.job.partName ? (
            j.rawMatName
          ) : (
            <Box
              sx={{
                display: "flex",
                alignItems: "center",
              }}
            >
              <Box sx={{ mr: "0.2em" }}>
                <PartIdenticon part={j.rawMatName} size={25} />
              </Box>
              <Typography variant="body2" display="block">
                {j.rawMatName}
              </Typography>
            </Box>
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
          {j.job.cycles ?? 0}
          {allowEditQty ? (
            <Tooltip title="Edit">
              <IconButton size="small" onClick={() => props.editQty(j)}>
                <EditIcon />
              </IconButton>
            </Tooltip>
          ) : undefined}
        </TableCell>
        <TableCell align="right">{j.remainingToStart}</TableCell>
        <TableCell align="right">{j.assignedRaw}</TableCell>
        <TableCell align="right">
          <Tooltip
            title={
              j.remainingToStart > 0 || j.assignedRaw > 0 ? `${j.remainingToStart} - ${j.assignedRaw}` : ""
            }
          >
            <span>{Math.max(j.remainingToStart - j.assignedRaw, 0)}</span>
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
      </JobTableRow>
      <JobTableRow $highlightedRow={highlRow} $noncompletedRow={j.remainingToStart - j.assignedRaw > 0}>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={10}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <JobDetails job={j.job} checkAnalysisMonth={false} />
          </Collapse>
        </TableCell>
      </JobTableRow>
    </>
  );
}

interface RawMaterialJobTableProps {
  readonly queue: string;
  readonly editNote: (job: Readonly<api.IActiveJob>) => void;
  readonly editQty: (job: JobRawMaterialData) => void;
}

function RawMaterialJobTable(props: RawMaterialJobTableProps) {
  const currentSt = useAtomValue(currentStatus);
  const jobs = React.useMemo(
    () => extractJobRawMaterial(props.queue, currentSt.jobs, currentSt.material),
    [props.queue, currentSt]
  );

  if (jobs.length === 0) return null;

  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          <TableCell>Job</TableCell>
          <TableCell>Starting Time</TableCell>
          <TableCell>Material</TableCell>
          <TableCell>Note</TableCell>
          <TableCell align="right">Planned Quantity</TableCell>
          <TableCell align="right">Remaining To Start</TableCell>
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

const RawMaterialWorkorderRow = React.memo(function RawMaterialWorkorderRow({
  workorder,
  inProc,
}: {
  workorder: Readonly<api.IActiveWorkorder>;
  inProc: number;
}) {
  const setDialog = useSetAtom(workorderCommentDialogAtom);
  return (
    <TableRow>
      <TableCell>{workorder.workorderId}</TableCell>
      <TableCell>
        <Box
          sx={{
            display: "flex",
            alignItems: "center",
          }}
        >
          <Box sx={{ mr: "0.2em" }}>
            <PartIdenticon part={workorder.part} size={25} />
          </Box>
          <Typography variant="body2" display="block">
            {workorder.part}
          </Typography>
        </Box>
      </TableCell>
      <TableCell>
        {workorder.comments && workorder.comments.length > 0
          ? workorder.comments[workorder.comments.length - 1].comment
          : ""}

        <Tooltip title="Edit">
          <IconButton size="small" onClick={() => setDialog(workorder)}>
            <EditIcon />
          </IconButton>
        </Tooltip>
      </TableCell>
      <TableCell>{workorder.dueDate.toLocaleDateString()}</TableCell>
      <TableCell>{workorder.priority}</TableCell>
      <TableCell>{workorder.plannedQuantity}</TableCell>
      <TableCell>{workorder.completedQuantity}</TableCell>
      <TableCell>{inProc}</TableCell>
      <TableCell>{Math.max(0, workorder.plannedQuantity - workorder.completedQuantity - inProc)}</TableCell>
    </TableRow>
  );
});

const RawMaterialWorkorderTable = React.memo(function RawMaterialWorkorderTable() {
  const curSt = useAtomValue(currentStatus);
  if (!curSt.workorders || curSt.workorders.length === 0) return null;

  const sorted = LazySeq.of(curSt.workorders).sortBy(
    (w) => w.dueDate,
    (w) => w.priority
  );

  const inProcByWorkorder = LazySeq.of(curSt.material)
    .filter((m) => !!m.workorderId && m.workorderId !== "")
    .toLookupMap<string, string, number>(
      (m) => m.workorderId ?? "",
      (m) => m.partName,
      () => 1,
      (a, b) => a + b
    );

  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          <TableCell>Workorder</TableCell>
          <TableCell>Part</TableCell>
          <TableCell>Comment</TableCell>
          <TableCell>Due Date</TableCell>
          <TableCell>Priority</TableCell>
          <TableCell>Planned Qty</TableCell>
          <TableCell>Completed Qty</TableCell>
          <TableCell>In Process</TableCell>
          <TableCell>Remaining To Assign</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {sorted.map((w) => (
          <RawMaterialWorkorderRow
            key={`${w.workorderId}-${w.part}`}
            workorder={w}
            inProc={inProcByWorkorder.get(w.workorderId)?.get(w.part) ?? 0}
          />
        ))}
      </TableBody>
    </Table>
  );
});

interface EditNoteDialogProps {
  readonly job: {
    readonly unique: string;
    readonly partName: string;
    readonly comment?: string | null;
  } | null;
  readonly closeDialog: () => void;
}

export const EditNoteDialog = React.memo(function EditNoteDialog(props: EditNoteDialogProps) {
  const [note, setNote] = React.useState<string | null>(null);
  const setComment = useSetAtom(setJobComment);

  function close() {
    props.closeDialog();
    setNote(null);
  }

  function save() {
    if (note === null || props.job === null || note === props.job.comment) return;
    setComment(props.job.unique, note);
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
  const allowEditQtyUrl = useAtomValue(fmsInformation).allowEditJobPlanQuantityFromQueuesPage ?? null;

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
      await fetch(allowEditQtyUrl, {
        method: "PUT",
        headers: new Headers({
          "Content-Type": "application/json",
        }),
        body: JSON.stringify({
          Unique: props.job.job.unique,
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
              <div style={{ marginLeft: "1em", flexGrow: 1 }}>
                Edit Planned Quantity For {props.job.job.unique}
              </div>
            </div>
          </DialogTitle>
          <DialogContent>
            <p>
              {props.job.job.cycles} currently planned, {props.job.remainingToStart} remaining to start
            </p>
            <TextField
              variant="outlined"
              fullWidth
              autoFocus
              inputProps={{
                style: { textAlign: "right" },
                min: (props.job.job.cycles ?? 0) - props.job.remainingToStart,
              }}
              type="number"
              value={newQty === null ? "" : newQty}
              onChange={(e) => setNewQty(parseInt(e.target.value))}
            />
          </DialogContent>
          <DialogActions>
            <Button
              color="primary"
              disabled={running || newQty === null || isNaN(newQty)}
              onClick={() => void setQty()}
            >
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

const workorderCommentDialogAtom = atom<Readonly<api.IActiveWorkorder> | null>(null);

function WorkorderCommentDialog() {
  const [workorder, setWorkorder] = useAtom(workorderCommentDialogAtom);
  const [comment, setComment] = React.useState<string | null>(null);
  const addComment = useSetAtom(addWorkorderComment);

  function close() {
    setWorkorder(null);
    setComment(null);
  }

  function save() {
    if (workorder && comment && comment !== "") {
      addComment({ workorder: workorder.workorderId, comment: comment });
    }
    close();
  }

  return (
    <Dialog open={workorder !== null} onClose={close}>
      {workorder !== null ? (
        <>
          <DialogTitle>
            <div style={{ display: "flex", alignItems: "center" }}>
              <div>
                <PartIdenticon part={workorder.part} size={40} />
              </div>
              <div style={{ marginLeft: "1em", flexGrow: 1 }}>Add Comment For {workorder.workorderId}</div>
            </div>
          </DialogTitle>
          <DialogContent>
            <ul>
              {(workorder.comments ?? []).map((c, idx) => (
                <li key={idx}>
                  <Typography variant="body1">
                    {c.timeUTC.toLocaleString()}: {c.comment}
                  </Typography>
                </li>
              ))}
            </ul>
            <TextField
              variant="outlined"
              sx={{ mt: "1em" }}
              fullWidth
              autoFocus
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && comment !== null && comment !== "") {
                  save();
                  e.preventDefault();
                }
              }}
            />
          </DialogContent>
          <DialogActions>
            <Button color="primary" onClick={save}>
              Save Comment
            </Button>
            <Button color="primary" onClick={close}>
              Cancel
            </Button>
          </DialogActions>
        </>
      ) : undefined}
    </Dialog>
  );
}

interface AddMaterialButtonsProps {
  readonly label: string;
  readonly rawMatQueue: boolean;
}

const AddMaterialButtons = React.memo(function AddMaterialButtons(props: AddMaterialButtonsProps) {
  const currentJobs = useAtomValue(currentStatus).jobs;
  const fmsInfo = useAtomValue(fmsInformation);
  const setBulkAddCastings = useSetAtom(bulkAddCastingToQueue);
  const setAddBySerial = useSetAtom(enterSerialForNewMaterialDialog);

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
            switch (fmsInfo.addRawMaterial) {
              case api.AddRawMaterialType.AddAsUnassigned:
                setBulkAddCastings(props.label);
                break;
              case api.AddRawMaterialType.AddAndSpecifyJob:
              case api.AddRawMaterialType.RequireExistingMaterial:
                setAddBySerial(props.label);
                break;
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
      <Tooltip title="Add Material">
        <Fab
          color="secondary"
          onClick={() => setAddBySerial(props.label)}
          size="medium"
          style={{ marginBottom: "-30px", zIndex: 1 }}
        >
          <AssignIcon fontSize="inherit" />
        </Fab>
      </Tooltip>
    );
  } else {
    return null;
  }
});

interface QueueProps {
  readonly queues: ReadonlyArray<string>;
}

export const Queues = (props: QueueProps) => {
  const operator = useAtomValue(currentOperator);
  const currentSt = useAtomValue(currentStatus);
  const rawMatQueues = useAtomValue(rawMaterialQueues);
  const data = React.useMemo(
    () => selectQueueData(props.queues, currentSt, rawMatQueues),
    [currentSt, props.queues, rawMatQueues]
  );
  const hasJobs = !LazySeq.ofObject(currentSt.jobs).isEmpty();

  const [changeNoteForJob, setChangeNoteForJob] = React.useState<Readonly<api.IActiveJob> | null>(null);
  const closeChangeNoteDialog = React.useCallback(() => setChangeNoteForJob(null), []);
  const [editQtyForJob, setEditQtyForJob] = React.useState<JobRawMaterialData | null>(null);
  const closeEditJobQtyDialog = React.useCallback(() => setEditQtyForJob(null), []);
  const [multiMaterialDialog, setMultiMaterialDialog] = React.useState<ReadonlyArray<
    Readonly<api.IInProcessMaterial>
  > | null>(null);
  const closeMultiMatDialog = React.useCallback(() => setMultiMaterialDialog(null), []);

  return (
    <Box
      sx={{
        padding: "8px",
        width: "100%",
      }}
    >
      {data.map((region, idx) => (
        <div style={idx < data.length - 1 ? { borderBottom: "1px solid black" } : undefined} key={idx}>
          <SortableRegion
            matIds={region.material.map((m) => m.materialID)}
            direction="rect"
            queueName={region.label}
            renderDragOverlay={(mat) => (
              <DragOverlayInProcMaterial
                mat={mat}
                hideEmptySerial
                displayJob={hasJobs && region.rawMaterialQueue}
                fsize="normal"
              />
            )}
          >
            <Box minHeight="134px">
              <Box display="flex" margin="4px">
                <Typography variant="h4" flexGrow={1}>
                  {region.label}
                </Typography>
                {region.free ? undefined : (
                  <AddMaterialButtons label={region.label} rawMatQueue={region.rawMaterialQueue} />
                )}
              </Box>
              <Box justifyContent="flex-start" display="flex" flexWrap="wrap">
                {region.material.map((m, matIdx) => (
                  <SortableInProcMaterial
                    key={matIdx}
                    mat={m}
                    hideEmptySerial
                    fsize="normal"
                    displayJob={hasJobs && region.rawMaterialQueue}
                  />
                ))}
                {region.groupedRawMat && region.groupedRawMat.length > 0
                  ? region.groupedRawMat.map((matGroup, idx) =>
                      matGroup.material.length === 1 ? (
                        <InProcMaterial
                          key={idx}
                          mat={matGroup.material[0]}
                          hideEmptySerial
                          fsize="normal"
                          displayJob={hasJobs && region.rawMaterialQueue}
                        />
                      ) : (
                        <MultiMaterial
                          key={idx}
                          partOrCasting={matGroup.partOrCasting}
                          assignedJobUnique={matGroup.assignedJobUnique}
                          material={matGroup.material}
                          fsize="normal"
                          onOpen={() => setMultiMaterialDialog(matGroup.material)}
                        />
                      )
                    )
                  : undefined}
                {region.rawMaterialQueue ? (
                  hasJobs ? (
                    <RawMaterialJobTable
                      queue={region.label}
                      editNote={setChangeNoteForJob}
                      editQty={setEditQtyForJob}
                    />
                  ) : (
                    <RawMaterialWorkorderTable />
                  )
                ) : undefined}
              </Box>
            </Box>
          </SortableRegion>
        </div>
      ))}
      <QueuedMaterialDialog queueNames={props.queues} />
      <AddBySerialDialog />
      <BulkAddCastingWithoutSerialDialog />
      <EditNoteDialog job={changeNoteForJob} closeDialog={closeChangeNoteDialog} />
      <EditJobPlanQtyDialog job={editQtyForJob} closeDialog={closeEditJobQtyDialog} />
      <WorkorderCommentDialog />
      <MultiMaterialDialog
        material={multiMaterialDialog}
        closeDialog={closeMultiMatDialog}
        operator={operator}
      />
    </Box>
  );
};

export default function QueuesPage(props: QueueProps): JSX.Element {
  useSetTitle("Queues");

  return (
    <Box
      component="main"
      sx={{
        backgroundColor: "#F8F8F8",
        minHeight: {
          xs: "calc(100vh - 64px - 32px)",
          sm: "calc(100vh - 64px - 40px)",
          md: "calc(100vh - 64px)",
        },
      }}
    >
      <Queues {...props} />
    </Box>
  );
}
