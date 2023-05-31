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
import * as api from "../../network/api.js";
import { IconButton } from "@mui/material";
import { Table } from "@mui/material";
import { TableBody } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableRow } from "@mui/material";
import { format } from "date-fns";
import { useRecoilValue } from "recoil";
import { durationToMinutes } from "../../util/parseISODuration.js";
import { MaterialSummaryAndCompletedData } from "../../cell-status/material-summary.js";
import { useSetMaterialToShowInDialog } from "../../cell-status/material-details.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { last30MaterialSummary, specificMonthMaterialSummary } from "../../cell-status/material-summary.js";
import { LazySeq, HashMap, HashSet } from "@seedtactics/immutable-collections";

import { MoreHoriz } from "@mui/icons-material";
import { useAtomValue } from "jotai";

interface JobDisplayProps {
  readonly job: Readonly<api.IActiveJob>;
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
                      Fixture: {path.fixture}{" "}
                      {path.face !== undefined ? ", Face: " + path.face.toString() : undefined}
                    </div>
                  ) : undefined}
                  {path.inputQueue ? <div>Input Queue: {path.inputQueue}</div> : undefined}
                  {path.casting ? <div>Raw Material: {path.casting}</div> : undefined}
                  <div>
                    Load Stations: {path.load.join(",")} |{" "}
                    {durationToMinutes(path.expectedLoadTime).toFixed(1)} mins
                  </div>
                  {path.stops.map((stop, stopIdx) => (
                    <React.Fragment key={stopIdx}>
                      <div>
                        {stop.stationGroup}: {(stop.stationNums ?? []).join(",")} | Program: {stop.program}
                        {stop.programRevision ? " rev" + stop.programRevision.toString() : undefined} |{" "}
                        {durationToMinutes(stop.expectedCycleTime).toFixed(1)} mins
                      </div>
                    </React.Fragment>
                  ))}
                  <div>
                    Unload Stations: {path.unload.join(",")} |{" "}
                    {durationToMinutes(path.expectedUnloadTime).toFixed(1)} mins
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
  const setMatToShow = useSetMaterialToShowInDialog();

  const mats = LazySeq.of(props.matIdsForJob.get(props.unique) ?? HashSet.empty<number>())
    .collect((matId) => props.matsFromEvents.get(matId))
    .toRArray();

  if (mats === null || mats.length === 0) {
    return <div />;
  }

  const matsById = LazySeq.of(currentMaterial).toHashMap(
    (m) => [m.materialID, m],
    (m1, _m2) => m1
  );

  const anyWorkorder = LazySeq.of(mats).anyMatch(
    (m) => m.workorderId !== undefined && m.workorderId !== "" && m.workorderId !== m.serial
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
                      summary: props.matsFromEvents.get(mat.materialID) ?? {
                        materialID: mat.materialID,
                        jobUnique: mat.jobUnique ?? "",
                        partName: mat.partName ?? "",
                        startedProcess1: false,
                        serial: mat.serial,
                        workorderId: mat.workorderId,
                        signaledInspections: [],
                      },
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

export function JobDetails(props: JobDetailsProps): JSX.Element {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const matsFromEvents = useRecoilValue(
    props.checkAnalysisMonth && period.type === "SpecificMonth"
      ? specificMonthMaterialSummary
      : last30MaterialSummary
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
