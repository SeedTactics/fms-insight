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
import { Box, Card, styled } from "@mui/material";
import { CardHeader } from "@mui/material";
import { CardContent } from "@mui/material";
import { IconButton } from "@mui/material";
import { Tooltip } from "@mui/material";
import ImportExport from "@mui/icons-material/ImportExport";
import ExtensionIcon from "@mui/icons-material/Extension";
import EditIcon from "@mui/icons-material/Edit";
import { Typography } from "@mui/material";
import { Table } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableBody } from "@mui/material";
import { addDays, startOfToday } from "date-fns";
import { ScheduledJobDisplay, buildScheduledJobs, copyScheduledJobsToClipboard } from "../../data/results.schedules";
import { IHistoricJob } from "../../network/api";
import { PartIdenticon } from "../station-monitor/Material";
import { EditNoteDialog } from "../station-monitor/Queues";
import KeyboardArrowDownIcon from "@mui/icons-material/KeyboardArrowDown";
import KeyboardArrowUpIcon from "@mui/icons-material/KeyboardArrowUp";
import { JobDetails } from "../station-monitor/JobDetails";
import { Collapse } from "@mui/material";
import { HashMap } from "prelude-ts";
import { useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status";
import { last30Jobs } from "../../cell-status/scheduled-jobs";
import { last30MaterialSummary, MaterialSummaryAndCompletedData } from "../../cell-status/material-summary";

export interface JobsTableProps {
  readonly schJobs: HashMap<string, Readonly<IHistoricJob>>;
  readonly matIds: HashMap<number, MaterialSummaryAndCompletedData>;
  readonly showInProcCnt: boolean;
  readonly start: Date;
  readonly end: Date;
}

const JobTableRow = styled(TableRow)((props: { darkRow?: boolean }) => ({
  "& > *": {
    borderBottom: "unset",
  },
  backgroundColor: props.darkRow ? "#F5F5F5" : "unset",
}));

const JobDetailRow = styled(TableRow)((props: { darkRow?: boolean }) => ({
  backgroundColor: props.darkRow ? "#F5F5F5" : "unset",
}));

interface JobsRowProps {
  readonly job: ScheduledJobDisplay;
  readonly showMaterial: boolean;
  readonly showInProcCnt: boolean;
  readonly setCurEditNoteJob: (j: ScheduledJobDisplay) => void;
}

function JobsRow(props: JobsRowProps) {
  const [open, setOpen] = React.useState<boolean>(false);

  let colCnt = 6;
  if (props.showMaterial) colCnt += 1;
  if (props.showInProcCnt) colCnt += 3;

  const job = props.job;
  return (
    <>
      <JobTableRow darkRow={job.darkRow}>
        <TableCell>{job.historicJob.routeStartUTC.toLocaleString()}</TableCell>
        <TableCell>
          <Box
            sx={{
              display: "flex",
              alignItems: "center",
            }}
          >
            <Box sx={{ mr: "0.2em" }}>
              <PartIdenticon part={job.historicJob.partName} size={25} />
            </Box>
            <div>
              <Typography variant="body2" component="span" display="block">
                {job.historicJob.partName}
              </Typography>
            </div>
          </Box>
        </TableCell>
        {props.showMaterial ? (
          <TableCell>
            {job.casting ? (
              <Box
                sx={{
                  display: "flex",
                  alignItems: "center",
                }}
              >
                <Box sx={{ mr: "0.2em" }}>
                  <PartIdenticon part={job.casting} size={25} />
                </Box>
                <Typography variant="body2" display="block">
                  {job.casting}
                </Typography>
              </Box>
            ) : undefined}
          </TableCell>
        ) : undefined}
        {props.showInProcCnt ? (
          <TableCell>
            {job.historicJob.comment}

            <Tooltip title="Edit">
              <IconButton size="small" onClick={() => props.setCurEditNoteJob(job)}>
                <EditIcon />
              </IconButton>
            </Tooltip>
          </TableCell>
        ) : undefined}
        <TableCell align="right">{job.scheduledQty}</TableCell>
        <TableCell align="right" sx={{ backgroundColor: job.decrementedQty > 0 ? "#FF8A65" : undefined }}>
          {job.decrementedQty}
        </TableCell>
        <TableCell align="right">{job.completedQty}</TableCell>
        {props.showInProcCnt ? (
          <>
            <TableCell align="right">{job.inProcessQty}</TableCell>
            <TableCell align="right">{job.remainingQty}</TableCell>
          </>
        ) : undefined}
        <TableCell>
          <Tooltip title="Show Details">
            <IconButton size="small" onClick={() => setOpen(!open)}>
              {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
            </IconButton>
          </Tooltip>
        </TableCell>
      </JobTableRow>
      <JobDetailRow darkRow={job.darkRow}>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={colCnt}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <JobDetails
              job={job.inProcJob ? job.inProcJob : job.historicJob}
              checkAnalysisMonth={!props.showInProcCnt}
            />
          </Collapse>
        </TableCell>
      </JobDetailRow>
    </>
  );
}

export function JobsTable(props: JobsTableProps): JSX.Element {
  const [curEditNoteJob, setCurEditNoteJob] = React.useState<ScheduledJobDisplay | null>(null);
  const currentSt = useRecoilValue(currentStatus);

  const showMaterial = React.useMemo(() => {
    for (const [, newJob] of props.schJobs) {
      for (const p of newJob.procsAndPaths[0]?.paths ?? []) {
        if (p.casting !== null && p.casting !== undefined && p.casting !== "") {
          return true;
        }
      }
    }
    return false;
  }, [props.schJobs]);

  const jobs = React.useMemo(() => {
    return buildScheduledJobs(props.start, props.end, props.matIds, props.schJobs, currentSt);
  }, [props.matIds, props.schJobs, currentSt, props.start, props.end]);

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <ExtensionIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Scheduled Parts</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyScheduledJobsToClipboard(jobs, showMaterial)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
      />
      <CardContent>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Date</TableCell>
              <TableCell>Part</TableCell>
              {showMaterial ? <TableCell>Material</TableCell> : undefined}
              {props.showInProcCnt ? <TableCell>Note</TableCell> : undefined}
              <TableCell align="right">Scheduled</TableCell>
              <TableCell align="right">Removed</TableCell>
              <TableCell align="right">Completed</TableCell>
              {props.showInProcCnt ? (
                <>
                  <TableCell align="right">In Process</TableCell>
                  <TableCell align="right">Remaining To Run</TableCell>
                </>
              ) : undefined}
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {jobs.map((job, jobIdx) => (
              <JobsRow
                key={jobIdx}
                job={job}
                showMaterial={showMaterial}
                setCurEditNoteJob={setCurEditNoteJob}
                showInProcCnt={props.showInProcCnt}
              />
            ))}
          </TableBody>
        </Table>
        <EditNoteDialog job={curEditNoteJob?.historicJob ?? null} closeDialog={() => setCurEditNoteJob(null)} />
      </CardContent>
    </Card>
  );
}

export const RecentSchedules = React.memo(function RecentSchedules({
  showInProcCnt,
}: {
  readonly showInProcCnt: boolean;
}) {
  const matIds = useRecoilValue(last30MaterialSummary);
  const schJobs = useRecoilValue(last30Jobs);
  const start = addDays(startOfToday(), -6);
  const end = addDays(startOfToday(), 1);

  return <JobsTable matIds={matIds.matsById} schJobs={schJobs} start={start} end={end} showInProcCnt={showInProcCnt} />;
});

export function CompletedParts(): JSX.Element {
  React.useEffect(() => {
    document.title = "Scheduled Jobs - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <div data-testid="scheduled-jobs">
        <RecentSchedules showInProcCnt={true} />
      </div>
    </main>
  );
}
