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
import * as api from "../../data/api";
import {
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
} from "@material-ui/core";
import MoreHoriz from "@material-ui/icons/MoreHoriz";
import { MaterialDetailTitle } from "./Material";
import { duration } from "moment";
import { format } from "date-fns";
import { JobsBackend, LogBackend } from "../../data/backend";
import { MaterialSummary, MaterialSummaryAndCompletedData } from "../../data/events.matsummary";
import { LazySeq } from "../../data/lazyseq";
import { connect } from "../../store/store";
import { openMaterialDialog } from "../../data/material-details";
import { HashMap } from "prelude-ts";

interface JobDisplayProps {
  readonly job: Readonly<api.IInProcessJob>;
}

function displayDate(d: Date) {
  return format(d, "MMM d, yyyy h:mm aa");
}

function JobCompleted(props: JobDisplayProps & { procIdx: number; pathIdx: number }) {
  const val = props.job.completed?.[props.procIdx]?.[props.pathIdx];
  if (val !== undefined) {
    return <div>Completed: {val}</div>;
  }
  return null;
}

function JobDisplay(props: JobDisplayProps) {
  return (
    <div>
      <dl>
        <dt>Time</dt>
        <dd>
          {displayDate(props.job.routeStartUTC)} to {displayDate(props.job.routeEndUTC)}
        </dd>
        {props.job.comment !== undefined && props.job.comment !== "" ? (
          <>
            <dt>Comment</dt>
            <dd>{props.job.comment}</dd>
          </>
        ) : undefined}
        {props.job.assignedWorkorders && props.job.assignedWorkorders.length > 0 ? (
          <>
            <dt>Workorders</dt>
            <dd>{props.job.assignedWorkorders.join(", ")}</dd>
          </>
        ) : undefined}
        {props.job.procsAndPaths.map((proc, procIdx) => (
          <React.Fragment key={procIdx}>
            {proc.paths.map((path, pathIdx) => (
              <React.Fragment key={pathIdx}>
                <dt>
                  Process {procIdx + 1}
                  {proc.paths.length > 1 ? ", Path " + (pathIdx + 1).toString() : undefined}
                </dt>
                <dd>
                  <JobCompleted job={props.job} procIdx={procIdx} pathIdx={pathIdx} />
                  <div>Estimated Start: {displayDate(path.simulatedStartingUTC)}</div>
                  <div>Pallets: {path.pallets.join(",")}</div>
                  {path.fixture ? (
                    <div>
                      Fixture: {path.fixture} {path.face !== undefined ? ", Face: " + path.face.toString() : undefined}
                    </div>
                  ) : undefined}
                  {path.inputQueue ? <div>Input Queue: {path.inputQueue}</div> : undefined}
                  {path.casting ? <div>Raw Material: {path.casting}</div> : undefined}
                  <div>
                    Load Stations: {path.load.join(",")} | {duration(path.expectedLoadTime).minutes().toFixed(1)} mins
                  </div>
                  {path.stops.map((stop, stopIdx) => (
                    <React.Fragment key={stopIdx}>
                      <div>
                        {stop.stationGroup}: {(stop.stationNums ?? []).join(",")} | Program: {stop.program}
                        {stop.programRevision ? " rev" + stop.programRevision.toString() : undefined} |{" "}
                        {duration(stop.expectedCycleTime).minutes().toFixed(1)} mins
                      </div>
                    </React.Fragment>
                  ))}
                  <div>
                    Unload Stations: {path.unload.join(",")} | {duration(path.expectedUnloadTime).minutes().toFixed(1)}{" "}
                    mins
                  </div>
                  {path.outputQueue ? <div>Output Queue: {path.outputQueue}</div> : undefined}
                  {path.inspections && path.inspections.length > 0 ? (
                    <div>Inspections: {path.inspections.map((i) => i.inspectionType).join(",")}</div>
                  ) : undefined}
                </dd>
              </React.Fragment>
            ))}
          </React.Fragment>
        ))}
      </dl>
    </div>
  );
}

interface MaterialStatusProps {
  readonly matSummary: MaterialSummaryAndCompletedData | null;
  readonly inProcMat: Readonly<api.IInProcessMaterial> | null;
}

function MaterialStatus(props: MaterialStatusProps) {
  if (props.inProcMat !== null && props.inProcMat.location.type === api.LocType.OnPallet) {
    return <span>On pallet {props.inProcMat.location.pallet ?? ""}</span>;
  } else if (props.inProcMat !== null && props.inProcMat.location.type === api.LocType.InQueue) {
    return <span>In queue {props.inProcMat.location.currentQueue ?? ""}</span>;
  } else if (props.matSummary?.completed_machining) {
    return <span>Completed</span>;
  } else if (props.matSummary !== null && props.matSummary.completed_procs.length === 0) {
    return <span>Not yet started</span>;
  } else {
    return <span />;
  }
}

interface JobMaterialProps {
  readonly unique: string;
  readonly currentMaterial: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  readonly matsFromEvents: HashMap<number, MaterialSummaryAndCompletedData>;
  readonly openDetails: (mat: Readonly<MaterialSummary>) => void;
}

function JobMaterial(props: JobMaterialProps) {
  const [mats, setMats] = React.useState<ReadonlyArray<Readonly<api.IMaterialDetails>> | null>(null);
  const [loading, setLoading] = React.useState<boolean>(false);

  React.useEffect(() => {
    if (props.unique === null) {
      setMats(null);
      setLoading(false);
    } else {
      setLoading(true);
      LogBackend.materialDetailsForJob(props.unique)
        .then((m) => {
          setMats(m);
        })
        .finally(() => setLoading(false));
    }
  }, [props.unique]);

  if (mats === null || mats.length === 0) {
    return <div />;
  }

  if (loading) {
    return (
      <div style={{ display: "flex", justifyContent: "center", marginTop: "1em" }}>
        <CircularProgress /> Loading material
      </div>
    );
  }

  const matsById = LazySeq.ofIterable(props.currentMaterial).toMap(
    (m) => [m.materialID, m],
    (m1, _m2) => m1
  );

  const anyWorkorder = LazySeq.ofIterable(props.currentMaterial).anyMatch(
    (m) => m.workorderId !== undefined && m.workorderId !== "" && m.workorderId !== m.serial
  );

  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          {anyWorkorder ? <TableCell>Workorder</TableCell> : undefined}
          <TableCell>Serial</TableCell>
          <TableCell>Status</TableCell>
          <TableCell padding="checkbox" />
        </TableRow>
      </TableHead>
      <TableBody>
        {mats.map((mat) => (
          <TableRow key={mat.materialID}>
            {anyWorkorder ? <TableCell>{mat.workorder ?? ""}</TableCell> : undefined}
            <TableCell>{mat.serial ?? ""}</TableCell>
            <TableCell>
              <MaterialStatus
                matSummary={props.matsFromEvents.get(mat.materialID).getOrNull()}
                inProcMat={matsById.get(mat.materialID).getOrNull()}
              />
            </TableCell>
            <TableCell padding="checkbox">
              <IconButton
                onClick={() =>
                  props.openDetails(
                    props.matsFromEvents.get(mat.materialID).getOrElse({
                      materialID: mat.materialID,
                      jobUnique: mat.jobUnique ?? "",
                      partName: mat.partName ?? "",
                      completed_procs: [],
                      serial: mat.serial,
                      workorderId: mat.workorder,
                      signaledInspections: [],
                    })
                  )
                }
              >
                <MoreHoriz fontSize="inherit" />
              </IconButton>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

const ConnectedJobMaterial = connect(
  (st) => ({
    currentMaterial: st.Current.current_status.material,
    matsFromEvents: st.Events.last30.mat_summary.matsById,
  }),
  {
    openDetails: openMaterialDialog,
  }
)(JobMaterial);

export interface JobPlanDialogProps {
  readonly unique: string | null;
  readonly close: () => void;
}

export function JobPlanDialog(props: JobPlanDialogProps) {
  const [job, setJob] = React.useState<Readonly<api.IJobPlan> | null>(null);
  const [loading, setLoading] = React.useState<boolean>(false);

  React.useEffect(() => {
    if (props.unique === null) {
      setJob(null);
      setLoading(false);
    } else if (job === null || props.unique !== job.unique) {
      setLoading(true);
      JobsBackend.getJobPlan(props.unique)
        .then((j) => {
          setJob(j);
        })
        .finally(() => setLoading(false));
    }
  }, [props.unique]);

  function close() {
    setJob(null);
    setLoading(false);
    props.close();
  }

  return (
    <Dialog open={props.unique !== null} onClose={close}>
      <DialogTitle disableTypography>
        {job !== null ? <MaterialDetailTitle partName={job.partName} subtitle={job.unique} /> : undefined}
      </DialogTitle>
      <DialogContent>
        {loading ? (
          <div style={{ display: "flex", justifyContent: "center" }}>
            <CircularProgress />
          </div>
        ) : undefined}
        {job !== null && props.unique !== null ? (
          <>
            <JobDisplay job={job} />
            <ConnectedJobMaterial unique={props.unique} />
          </>
        ) : undefined}
      </DialogContent>
      <DialogActions>
        <Button onClick={close} color="primary">
          Close
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export interface JobDetailDialogProps {
  readonly job: Readonly<api.IInProcessJob> | null;
  readonly close: () => void;
}

export function JobDetailDialog(props: JobDetailDialogProps) {
  return (
    <Dialog open={props.job !== null} onClose={props.close}>
      <DialogTitle disableTypography>
        {props.job !== null ? (
          <MaterialDetailTitle partName={props.job.partName} subtitle={props.job.unique} />
        ) : undefined}
      </DialogTitle>
      <DialogContent>
        {props.job !== null ? (
          <>
            <JobDisplay job={props.job} />
            <ConnectedJobMaterial unique={props.job.unique} />
          </>
        ) : undefined}
      </DialogContent>
      <DialogActions>
        <Button onClick={props.close} color="primary">
          Close
        </Button>
      </DialogActions>
    </Dialog>
  );
}
