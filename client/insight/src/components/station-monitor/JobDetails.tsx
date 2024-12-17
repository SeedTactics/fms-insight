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

import { Fragment, ReactNode } from "react";
import * as api from "../../network/api.js";
import { IconButton } from "@mui/material";
import { Table } from "@mui/material";
import { TableBody } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableRow } from "@mui/material";
import { durationToMinutes } from "../../util/parseISODuration.js";
import { MaterialSummaryAndCompletedData } from "../../cell-status/material-summary.js";
import { materialDialogOpen } from "../../cell-status/material-details.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { last30MaterialSummary, specificMonthMaterialSummary } from "../../cell-status/material-summary.js";
import { LazySeq, HashMap, HashSet } from "@seedtactics/immutable-collections";

import { MoreHoriz } from "@mui/icons-material";
import { useAtomValue, useSetAtom } from "jotai";

interface JobDisplayProps {
  readonly job: Readonly<api.IActiveJob>;
}

function displayDate(d: Date) {
  return d.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
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
        <dt>Job ID</dt>
        <dd>{props.job.unique}</dd>
        <dt>Time</dt>
        <dd>
          {displayDate(props.job.routeStartUTC)} to {displayDate(props.job.routeEndUTC)}
        </dd>
        {props.job.cycles ? (
          <>
            <dt>Quantity</dt>
            <dd>{props.job.cycles}</dd>
          </>
        ) : undefined}
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
          <Fragment key={procIdx}>
            {proc.paths.map((path, pathIdx) => (
              <Fragment key={pathIdx}>
                <dt>
                  Process {procIdx + 1}
                  {proc.paths.length > 1 ? ", Path " + (pathIdx + 1).toString() : undefined}
                </dt>
                <dd>
                  <JobCompleted job={props.job} procIdx={procIdx} pathIdx={pathIdx} />
                  <div>Estimated Start: {displayDate(path.simulatedStartingUTC)}</div>
                  <div>Pallets: {(path.palletNums ?? []).map((p) => p.toString()).join(",")}</div>
                  {path.fixture ? (
                    <div>
                      Fixture: {path.fixture}{" "}
                      {path.face !== undefined ? ", Face: " + path.face.toString() : undefined}
                    </div>
                  ) : undefined}
                  {path.inputQueue ? <div>Input Queue: {path.inputQueue}</div> : undefined}
                  {path.casting ? <div>Raw Material: {path.casting}</div> : undefined}
                  <div>
                    Load Stations: {path.load.join(",")} |{" "}
                    {durationToMinutes(path.expectedLoadTime).toFixed(1)} mins
                    {path.partsPerPallet > 1 ? " per piece" : ""}
                  </div>
                  {path.stops.map((stop, stopIdx) => (
                    <Fragment key={stopIdx}>
                      <div>
                        {stop.stationGroup}: {(stop.stationNums ?? []).join(",")} | Program: {stop.program}
                        {stop.programRevision ? " rev" + stop.programRevision.toString() : undefined} |{" "}
                        {(durationToMinutes(stop.expectedCycleTime) / path.partsPerPallet).toFixed(1)} mins
                        {path.partsPerPallet > 1 ? " per piece" : ""}
                      </div>
                    </Fragment>
                  ))}
                  <div>
                    Unload Stations: {path.unload.join(",")} |{" "}
                    {durationToMinutes(path.expectedUnloadTime).toFixed(1)} mins
                    {path.partsPerPallet > 1 ? " per piece" : ""}
                  </div>
                  {path.outputQueue ? <div>Output Queue: {path.outputQueue}</div> : undefined}
                  {path.inspections && path.inspections.length > 0 ? (
                    <div>Inspections: {path.inspections.map((i) => i.inspectionType).join(",")}</div>
                  ) : undefined}
                </dd>
              </Fragment>
            ))}
          </Fragment>
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
    return <span>On pallet {props.inProcMat.location.palletNum ?? ""}</span>;
  } else if (props.inProcMat !== null && props.inProcMat.location.type === api.LocType.InQueue) {
    return <span>In queue {props.inProcMat.location.currentQueue ?? ""}</span>;
  } else if (props.matSummary?.completed_last_proc_machining) {
    return <span>Completed</span>;
  } else if (props.matSummary !== null && props.matSummary.startedProcess1 === false) {
    return <span>Not yet started</span>;
  } else {
    return <span />;
  }
}

interface JobMaterialProps {
  readonly unique: string;
  readonly matsFromEvents: HashMap<number, MaterialSummaryAndCompletedData>;
  readonly matIdsForJob: HashMap<string, HashSet<number>>;
  readonly fullWidth: boolean;
}

function JobMaterial(props: JobMaterialProps) {
  const currentMaterial = useAtomValue(currentStatus).material;
  const setMatToShow = useSetAtom(materialDialogOpen);

  const mats = LazySeq.of(props.matIdsForJob.get(props.unique) ?? HashSet.empty<number>())
    .collect((matId) => props.matsFromEvents.get(matId))
    .toRArray();

  if (mats === null || mats.length === 0) {
    return <div />;
  }

  const matsById = LazySeq.of(currentMaterial).toHashMap(
    (m) => [m.materialID, m],
    (m1, _m2) => m1,
  );

  const anyWorkorder = LazySeq.of(mats).some(
    (m) => m.workorderId !== undefined && m.workorderId !== "" && m.workorderId !== m.serial,
  );

  return (
    <div>
      <Table size="small" style={{ width: props.fullWidth ? "100%" : "auto" }}>
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
              {anyWorkorder ? <TableCell>{mat.workorderId ?? ""}</TableCell> : undefined}
              <TableCell>{mat.serial ?? ""}</TableCell>
              <TableCell>
                <MaterialStatus
                  matSummary={props.matsFromEvents.get(mat.materialID) ?? null}
                  inProcMat={matsById.get(mat.materialID) ?? null}
                />
              </TableCell>
              <TableCell padding="checkbox">
                <IconButton
                  onClick={() =>
                    setMatToShow({
                      type: "MatSummary",
                      summary: mat,
                    })
                  }
                  size="large"
                >
                  <MoreHoriz fontSize="inherit" />
                </IconButton>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

export interface JobDetailsProps {
  readonly job: Readonly<api.IActiveJob> | null;
  readonly checkAnalysisMonth: boolean;
}

export function JobDetails(props: JobDetailsProps): ReactNode {
  const period = useAtomValue(selectedAnalysisPeriod);
  const matsFromEvents = useAtomValue(
    props.checkAnalysisMonth && period.type === "SpecificMonth"
      ? specificMonthMaterialSummary
      : last30MaterialSummary,
  );

  return (
    <div style={{ display: "flex", justifyContent: "space-evenly" }}>
      {props.job !== null ? (
        <>
          <JobDisplay job={props.job} />
          <JobMaterial
            unique={props.job.unique}
            fullWidth={false}
            matsFromEvents={matsFromEvents.matsById}
            matIdsForJob={matsFromEvents.matIdsForJob}
          />
        </>
      ) : undefined}
    </div>
  );
}
