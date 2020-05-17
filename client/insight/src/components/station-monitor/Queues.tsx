/* Copyright (c) 2020, John Lenz

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
import { WithStyles, createStyles, withStyles, makeStyles } from "@material-ui/core/styles";
import { createSelector } from "reselect";
import { SortEnd } from "react-sortable-hoc";
import { HashSet } from "prelude-ts";
import Table from "@material-ui/core/Table";
import TableHead from "@material-ui/core/TableHead";
import TableCell from "@material-ui/core/TableCell";
import TableRow from "@material-ui/core/TableRow";
import TableBody from "@material-ui/core/TableBody";
import TableFooter from "@material-ui/core/TableFooter";
import Button from "@material-ui/core/Button";
import Tooltip from "@material-ui/core/Tooltip";
import Typography from "@material-ui/core/Typography";
import EditIcon from "@material-ui/icons/Edit";
import Dialog from "@material-ui/core/Dialog";
import DialogTitle from "@material-ui/core/DialogTitle";
import DialogContent from "@material-ui/core/DialogContent";
import TextField from "@material-ui/core/TextField";
import DialogActions from "@material-ui/core/DialogActions";
import Fab from "@material-ui/core/Fab";
import IconButton from "@material-ui/core/IconButton";
import CircularProgress from "@material-ui/core/CircularProgress";
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
import * as api from "../../data/api";
import * as routes from "../../data/routes";
import * as guiState from "../../data/gui-state";
import * as currentSt from "../../data/current-status";
import { Store, connect, AppActionBeforeMiddleware, useSelector } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { MaterialSummary } from "../../data/events.matsummary";
import {
  ConnectedMaterialDialog,
  ConnectedChooseSerialOrDirectJobDialog,
  ConnectedAddCastingDialog,
} from "./QueuesAddMaterial";
import { QueueData, selectQueueData, extractJobRawMaterial, loadRawMaterialEvents } from "../../data/queue-material";
import { LogEntries } from "../LogEntry";
import { JobsBackend } from "../../data/backend";
import { LazySeq } from "../../data/lazyseq";
import { currentOperator } from "../../data/operators";
import ReactToPrint from "react-to-print";
import { PrintedLabel } from "./PrintedLabel";

interface RawMaterialJobTableProps {
  readonly queue: string;
  readonly addCastings: () => void;
  readonly editNote: (job: Readonly<api.IInProcessJob>) => void;
}

const useTableStyles = makeStyles((theme) =>
  createStyles({
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
  })
);

const highlightedComments = [/\bhold\b/, /\bmissing\b/, /\bwait\b/, /\bwaiting\b/, /\bnone\b/];

function highlightRow(j: Readonly<api.IInProcessJob>): boolean {
  const comment = j.comment;
  if (!comment || comment === "") return false;
  return LazySeq.ofIterable(highlightedComments).anyMatch((r) => r.test(comment));
}

function RawMaterialJobTable(props: RawMaterialJobTableProps) {
  const currentJobs = useSelector((s) => s.Current.current_status.jobs);
  const mats = useSelector((s) => s.Current.current_status.material);
  const hasOldCastings = useSelector((s) => !s.Events.last30.sim_use.castingNames.isEmpty());
  const jobs = React.useMemo(() => extractJobRawMaterial(props.queue, currentJobs, mats), [
    props.queue,
    currentJobs,
    mats,
  ]);
  const hasCastings = React.useMemo(() => {
    return hasOldCastings || jobs.findIndex((j) => j.rawMatName !== j.job.partName) >= 0;
  }, [hasOldCastings, jobs]);
  const classes = useTableStyles();

  return (
    <Table style={{ margin: "1em 5em 0 5em" }} size="small">
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
        </TableRow>
      </TableHead>
      <TableBody>
        {jobs.map((j, idx) => (
          <TableRow
            key={idx}
            className={
              highlightRow(j.job)
                ? classes.highlightedRow
                : j.plannedQty - j.startedQty - j.assignedRaw > 0
                ? classes.noncompletedRow
                : undefined
            }
          >
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
            <TableCell align="right">{j.plannedQty}</TableCell>
            <TableCell align="right">{j.startedQty}</TableCell>
            <TableCell align="right">{j.assignedRaw}</TableCell>
            <TableCell align="right">
              <Tooltip
                title={
                  j.startedQty > 0 || j.assignedRaw > 0 ? `${j.plannedQty} - ${j.startedQty} - ${j.assignedRaw}` : ""
                }
              >
                <span>{j.plannedQty - j.startedQty - j.assignedRaw}</span>
              </Tooltip>
            </TableCell>
            <TableCell align="right">{j.availableUnassigned}</TableCell>
          </TableRow>
        ))}
      </TableBody>
      {hasCastings ? (
        <TableFooter>
          <TableRow>
            <TableCell colSpan={8} />
            <TableCell align="right">
              <Button color="secondary" variant="outlined" onClick={props.addCastings}>
                Add Raw Material
              </Button>
            </TableCell>
          </TableRow>
        </TableFooter>
      ) : undefined}
    </Table>
  );
}

interface EditNoteDialogProps {
  readonly job: Readonly<api.IInProcessJob> | null;
  readonly closeDialog: () => void;
  readonly saveNote: (uniq: string, comment: string) => void;
}

const EditNoteDialog = React.memo(function EditNoteDialog(props: EditNoteDialogProps) {
  const [note, setNote] = React.useState<string | null>(null);

  function close() {
    props.closeDialog();
    setNote(null);
  }

  function save() {
    if (note === null || props.job === null || note === props.job.comment) return;
    props.saveNote(props.job.unique, note);
    close();
  }

  return (
    <Dialog open={props.job !== null} onClose={close}>
      {props.job !== null ? (
        <>
          <DialogTitle>Edit Note For {props.job.unique}</DialogTitle>
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

const ConnectedEditNoteDialog = connect((s) => ({}), {
  saveNote: currentSt.setJobComment,
})(EditNoteDialog);

interface MultiMaterialDialogProps {
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>> | null;
  readonly closeDialog: () => void;
  readonly usingLabelPrinter: boolean;
  readonly printFromClient: boolean;
  readonly operator: string | null;
  readonly printLabel: (matId: number, proc: number, loadStation: number | null, queue: string | null) => void;
}

const MultiMaterialDialog = React.memo(function MultiMaterialDialog(props: MultiMaterialDialogProps) {
  const [loading, setLoading] = React.useState(false);
  const [events, setEvents] = React.useState<ReadonlyArray<Readonly<api.ILogEntry>>>([]);
  const [showRemove, setShowRemove] = React.useState(false);
  const [removeCnt, setRemoveCnt] = React.useState<number>(NaN);
  const printRef = React.useRef(null);

  React.useEffect(() => {
    if (props.material === null) return;
    let isSubscribed = true;
    setLoading(true);
    loadRawMaterialEvents(props.material)
      .then((events) => {
        if (isSubscribed) {
          setEvents(events);
        }
      })
      .finally(() => setLoading(false));
    return () => {
      isSubscribed = false;
    };
  }, [props.material]);

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
          LazySeq.ofIterable(props.material || [])
            .take(removeCnt)
            .map((m) => m.materialID)
            .toArray(),
          props.operator || undefined
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
        {props.material && props.material.length > 0 && props.usingLabelPrinter ? (
          props.printFromClient ? (
            <>
              <ReactToPrint
                content={() => printRef.current}
                trigger={() => <Button color="primary">Print Label</Button>}
              />
              <div style={{ display: "none" }}>
                <div ref={printRef}>
                  <PrintedLabel material={props.material || []} />
                </div>
              </div>
            </>
          ) : (
            <Button
              color="primary"
              onClick={() =>
                props.material && props.material.length > 0
                  ? props.printLabel(
                      props.material[0].materialID,
                      0,
                      null,
                      props.material[0].location.currentQueue || null
                    )
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
  openAddToQueue(label: string): void;
  openAddCasting(label: string): void;
}

const AddMaterialButtons = React.memo(function AddMaterialButtons(props: AddMaterialButtonsProps) {
  const currentJobs = useSelector((s) => s.Current.current_status.jobs);
  const hasOldCastings = useSelector((s) => !s.Events.last30.sim_use.castingNames.isEmpty());
  const bttnsToShow = React.useMemo(() => {
    return {
      hasCastings:
        (props.rawMatQueue && hasOldCastings) ||
        LazySeq.ofObject(currentJobs)
          .flatMap(([, j]) => j.procsAndPaths[0]?.paths || [])
          .anyMatch((p) => p.inputQueue === props.label && p.casting !== undefined && p.casting !== ""),
      hasMatInput: LazySeq.ofObject(currentJobs)
        .flatMap(([, j]) => j.procsAndPaths)
        .flatMap((p) => p.paths)
        .anyMatch((p) => p.inputQueue === props.label && (p.casting === undefined || p.casting === "")),
    };
  }, [hasOldCastings, currentJobs]);

  return (
    <>
      {bttnsToShow.hasCastings ? (
        <Tooltip title="Add Raw Material">
          <Fab
            color="secondary"
            onClick={() => props.openAddCasting(props.label)}
            size="large"
            style={{ marginBottom: "-30px", zIndex: 1 }}
          >
            <AddIcon />
          </Fab>
        </Tooltip>
      ) : undefined}
      {bttnsToShow.hasMatInput ? (
        <Tooltip title="Add Assigned Material">
          <IconButton
            onClick={() => props.openAddToQueue(props.label)}
            size="medium"
            style={{ marginBottom: "-20px", zIndex: 1 }}
          >
            <AssignIcon fontSize="inherit" />
          </IconButton>
        </Tooltip>
      ) : undefined}
    </>
  );
});

const queueStyles = createStyles({
  mainScrollable: {
    padding: "8px",
    width: "100%",
  },
});

interface QueueProps {
  readonly data: ReadonlyArray<QueueData>;
  openMat: (m: Readonly<MaterialSummary>) => void;
  openAddToQueue: (queueName: string) => void;
  moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => void;
  readonly usingLabelPrinter: boolean;
  readonly printFromClient: boolean;
  readonly operator: string | null;
  readonly printLabel: (matId: number, proc: number, loadStation: number | null, queue: string | null) => void;
}

const Queues = withStyles(queueStyles)((props: QueueProps & WithStyles<typeof queueStyles>) => {
  React.useEffect(() => {
    document.title = "Material Queues - FMS Insight";
  }, []);
  const [addCastingQueue, setAddCastingQueue] = React.useState<string | null>(null);
  const closeAddCastingDialog = React.useCallback(() => setAddCastingQueue(null), []);
  const [changeNoteForJob, setChangeNoteForJob] = React.useState<Readonly<api.IInProcessJob> | null>(null);
  const closeChangeNoteDialog = React.useCallback(() => setChangeNoteForJob(null), []);
  const [multiMaterialDialog, setMultiMaterialDialog] = React.useState<ReadonlyArray<
    Readonly<api.IInProcessMaterial>
  > | null>(null);
  const closeMultiMatDialog = React.useCallback(() => setMultiMaterialDialog(null), []);

  return (
    <main data-testid="stationmonitor-queues" className={props.classes.mainScrollable}>
      {props.data.map((region, idx) => (
        <div style={idx < props.data.length - 1 ? { borderBottom: "1px solid rgba(0,0,0,0.12)" } : undefined} key={idx}>
          <SortableWhiteboardRegion
            axis="xy"
            label={region.label}
            flexStart
            addMaterialButton={
              region.free ? undefined : (
                <AddMaterialButtons
                  label={region.label}
                  rawMatQueue={region.rawMaterialQueue}
                  openAddToQueue={props.openAddToQueue}
                  openAddCasting={setAddCastingQueue}
                />
              )
            }
            distance={5}
            shouldCancelStart={() => false}
            onSortEnd={(se: SortEnd) =>
              props.moveMaterialInQueue({
                materialId: region.material[se.oldIndex].materialID,
                queue: region.label,
                queuePosition: se.newIndex,
                operator: props.operator,
              })
            }
          >
            {region.material.map((m, matIdx) => (
              <SortableInProcMaterial
                key={matIdx}
                index={matIdx}
                mat={m}
                hideEmptySerial
                onOpen={props.openMat}
                displayJob={region.rawMaterialQueue}
              />
            ))}
            {region.groupedRawMat && region.groupedRawMat.length > 0
              ? region.groupedRawMat.map((matGroup, idx) =>
                  matGroup.material.length === 1 ? (
                    <InProcMaterial
                      key={idx}
                      mat={matGroup.material[0]}
                      onOpen={props.openMat}
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
              <RawMaterialJobTable
                queue={region.label}
                addCastings={() => setAddCastingQueue(region.label)}
                editNote={setChangeNoteForJob}
              />
            ) : undefined}
          </SortableWhiteboardRegion>
        </div>
      ))}
      <ConnectedMaterialDialog />
      <ConnectedChooseSerialOrDirectJobDialog />
      <ConnectedAddCastingDialog queue={addCastingQueue} closeDialog={closeAddCastingDialog} />
      <ConnectedEditNoteDialog job={changeNoteForJob} closeDialog={closeChangeNoteDialog} />
      <MultiMaterialDialog
        material={multiMaterialDialog}
        closeDialog={closeMultiMatDialog}
        usingLabelPrinter={props.usingLabelPrinter}
        printFromClient={props.printFromClient}
        operator={props.operator}
        printLabel={props.printLabel}
      />
    </main>
  );
});

const buildQueueData = createSelector(
  (st: Store) => st.Current.current_status,
  (st: Store) => st.Events.last30.sim_use.rawMaterialQueues,
  (st: Store) => st.Route,
  (
    curStatus: Readonly<api.ICurrentStatus>,
    rawMatQueues: HashSet<string>,
    route: routes.State
  ): ReadonlyArray<QueueData> => {
    return selectQueueData(route.standalone_free_material, route.standalone_queues, curStatus, rawMatQueues);
  }
);

export default connect(
  (st: Store) => ({
    data: buildQueueData(st),
    usingLabelPrinter: st.ServerSettings.fmsInfo ? st.ServerSettings.fmsInfo.usingLabelPrinterForSerials : false,
    printFromClient: st.ServerSettings.fmsInfo?.useClientPrinterForLabels ?? false,
    operator: currentOperator(st),
  }),
  {
    openAddToQueue: (queueName: string) =>
      [
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: true,
        },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: queueName },
      ] as AppActionBeforeMiddleware,
    openMat: matDetails.openMaterialDialog,
    moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => [
      {
        type: currentSt.ActionType.ReorderQueuedMaterial,
        queue: d.queue,
        materialId: d.materialId,
        newIdx: d.queuePosition,
      },
      matDetails.addExistingMaterialToQueue(d),
    ],
    printLabel: matDetails.printLabel,
  }
)(Queues);
