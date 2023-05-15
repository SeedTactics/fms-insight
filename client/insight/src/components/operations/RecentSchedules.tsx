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
import * as React from "react";
import { Box, styled, TableSortLabel } from "@mui/material";
import { IconButton } from "@mui/material";
import { Tooltip } from "@mui/material";
import { Typography } from "@mui/material";
import { Table } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableBody } from "@mui/material";
import { addDays, startOfToday } from "date-fns";
import {
  ScheduledJobDisplay,
  buildScheduledJobs,
  copyScheduledJobsToClipboard,
} from "../../data/results.schedules.js";
import { PartIdenticon } from "../station-monitor/Material.js";
import { EditNoteDialog } from "../station-monitor/Queues.js";
import {
  KeyboardArrowDown as KeyboardArrowDownIcon,
  KeyboardArrowUp as KeyboardArrowUpIcon,
  Edit as EditIcon,
  ImportExport,
} from "@mui/icons-material";
import { JobDetails } from "../station-monitor/JobDetails.js";
import { Collapse } from "@mui/material";
import { selector, useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status.js";
import { last30Jobs } from "../../cell-status/scheduled-jobs.js";
import { last30MaterialSummary } from "../../cell-status/material-summary.js";
import { LazySeq, ToComparableBase } from "@seedtactics/immutable-collections";

export const JobTableRow = styled(TableRow, { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })(
  (props: { $darkRow?: boolean }) => ({
    "& > *": {
      borderBottom: "unset",
    },
    ...(props.$darkRow && { backgroundColor: "#F5F5F5" }),
  })
);

export const JobDetailRow = styled(TableRow, { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })(
  (props: { $darkRow?: boolean }) => ({
    ...(props.$darkRow && { backgroundColor: "#F5F5F5" }),
  })
);

interface JobsRowProps {
  readonly job: ScheduledJobDisplay;
  readonly showDarkRow: boolean;
  readonly showMaterial: boolean;
  readonly setCurEditNoteJob: (j: ScheduledJobDisplay) => void;
}

function JobsRow(props: JobsRowProps) {
  const [open, setOpen] = React.useState<boolean>(false);

  let colCnt = 10;
  if (props.showMaterial) colCnt += 1;

  const job = props.job;
  return (
    <>
      <JobTableRow $darkRow={props.showDarkRow && job.darkRow}>
        <TableCell>{job.routeStartTime.toLocaleString()}</TableCell>
        <TableCell>
          <Box
            sx={{
              display: "flex",
              alignItems: "center",
            }}
          >
            <Box sx={{ mr: "0.2em" }}>
              <PartIdenticon part={job.partName} size={25} />
            </Box>
            <div>
              <Typography variant="body2" component="span" display="block">
                {job.partName}
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
        <TableCell>
          {job.comment}

          <Tooltip title="Edit">
            <IconButton size="small" onClick={() => props.setCurEditNoteJob(job)}>
              <EditIcon />
            </IconButton>
          </Tooltip>
        </TableCell>
        <TableCell>{job.inProcJob === null ? "Archived" : "Active"}</TableCell>
        <TableCell align="right">{job.scheduledQty}</TableCell>
        <TableCell align="right" sx={{ backgroundColor: job.decrementedQty > 0 ? "#FF8A65" : undefined }}>
          {job.decrementedQty}
        </TableCell>
        <TableCell align="right">{job.completedQty}</TableCell>
        <TableCell align="right">{job.inProcessQty}</TableCell>
        <TableCell align="right">{job.remainingQty}</TableCell>
        <TableCell>
          <Tooltip title="Show Details">
            <IconButton size="small" onClick={() => setOpen(!open)}>
              {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
            </IconButton>
          </Tooltip>
        </TableCell>
      </JobTableRow>
      <JobDetailRow $darkRow={props.showDarkRow && job.darkRow}>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={colCnt}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <JobDetails job={job.inProcJob ? job.inProcJob : job.historicJob} checkAnalysisMonth={false} />
          </Collapse>
        </TableCell>
      </JobDetailRow>
    </>
  );
}

enum SortColumn {
  Date,
  Part,
  Material,
  Note,
  Active,
  Scheduled,
  Removed,
  Completed,
  InProc,
  RemainingToRun,
}

function sortJobs(
  jobs: ReadonlyArray<ScheduledJobDisplay>,
  sortBy: SortColumn,
  order: "asc" | "desc"
): ReadonlyArray<ScheduledJobDisplay> {
  let sortCol: ToComparableBase<ScheduledJobDisplay>;
  switch (sortBy) {
    case SortColumn.Date:
      sortCol = (j) => j.routeStartTime;
      break;
    case SortColumn.Part:
      sortCol = (j) => j.partName;
      break;
    case SortColumn.Material:
      sortCol = (j) => j.casting;
      break;
    case SortColumn.Note:
      sortCol = (j) => j.comment ?? null;
      break;
    case SortColumn.Active:
      sortCol = (j) => j.inProcJob === null;
      break;
    case SortColumn.Scheduled:
      sortCol = (j) => j.scheduledQty;
      break;
    case SortColumn.Removed:
      sortCol = (j) => j.decrementedQty;
      break;
    case SortColumn.Completed:
      sortCol = (j) => j.completedQty;
      break;
    case SortColumn.InProc:
      sortCol = (j) => j.inProcessQty;
      break;
    case SortColumn.RemainingToRun:
      sortCol = (j) => j.remainingQty;
      break;
  }
  return LazySeq.of(jobs).toSortedArray(order === "asc" ? { asc: sortCol } : { desc: sortCol });
}

function SortColHeader(props: {
  readonly col: SortColumn;
  readonly align: "left" | "right";
  readonly order: "asc" | "desc";
  readonly setOrder: (o: "asc" | "desc") => void;
  readonly sortBy: SortColumn;
  readonly setSortBy: (c: SortColumn) => void;
  readonly children: React.ReactNode;
}) {
  return (
    <Tooltip title="Sort" enterDelay={300}>
      <TableCell align={props.align} sortDirection={props.sortBy === props.col ? props.order : false}>
        <TableSortLabel
          active={props.sortBy === props.col}
          direction={props.order}
          onClick={() => {
            if (props.col === props.sortBy) {
              props.setOrder(props.order === "asc" ? "desc" : "asc");
            } else {
              props.setSortBy(props.col);
              props.setOrder("asc");
            }
          }}
        >
          {props.children}
        </TableSortLabel>
      </TableCell>
    </Tooltip>
  );
}

const JobsHeader = React.memo(function JobsHeader(props: {
  readonly jobs: ReadonlyArray<ScheduledJobDisplay>;
  readonly showMaterial: boolean;
  readonly order: "asc" | "desc";
  readonly setOrder: (o: "asc" | "desc") => void;
  readonly sortBy: SortColumn;
  readonly setSortBy: (c: SortColumn) => void;
}) {
  const sort = {
    sortBy: props.sortBy,
    setSortBy: props.setSortBy,
    order: props.order,
    setOrder: props.setOrder,
  };
  return (
    <TableHead>
      <TableRow>
        <SortColHeader align="left" col={SortColumn.Date} {...sort}>
          Date
        </SortColHeader>
        <SortColHeader align="left" col={SortColumn.Part} {...sort}>
          Part
        </SortColHeader>
        {props.showMaterial ? (
          <SortColHeader align="left" col={SortColumn.Material} {...sort}>
            Material
          </SortColHeader>
        ) : undefined}
        <SortColHeader align="left" col={SortColumn.Note} {...sort}>
          Note
        </SortColHeader>
        <SortColHeader align="left" col={SortColumn.Active} {...sort}>
          Active
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.Scheduled} {...sort}>
          Scheduled
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.Removed} {...sort}>
          Removed
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.Completed} {...sort}>
          Completed
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.InProc} {...sort}>
          In Process
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.RemainingToRun} {...sort}>
          Remaining To Run
        </SortColHeader>
        <TableCell>
          <Tooltip title="Copy to Clipboard">
            <IconButton
              style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
              onClick={() => copyScheduledJobsToClipboard(props.jobs, props.showMaterial)}
              size="large"
            >
              <ImportExport />
            </IconButton>
          </Tooltip>
        </TableCell>
      </TableRow>
    </TableHead>
  );
});

export const RecentSchedulesCard = React.memo(function RecentSchedules(): JSX.Element {
  const [curEditNoteJob, setCurEditNoteJob] = React.useState<ScheduledJobDisplay | null>(null);
  const [sortBy, setSortBy] = React.useState<SortColumn>(SortColumn.Date);
  const [order, setOrder] = React.useState<"asc" | "desc">("desc");

  const matIds = useRecoilValue(last30MaterialSummary);
  const schJobs = useRecoilValue(last30Jobs);
  const currentSt = useRecoilValue(currentStatus);

  const showMaterial = React.useMemo(() => {
    for (const [, newJob] of schJobs) {
      for (const p of newJob.procsAndPaths[0]?.paths ?? []) {
        if (p.casting !== null && p.casting !== undefined && p.casting !== "") {
          return true;
        }
      }
    }
    return false;
  }, [schJobs]);

  const jobs = React.useMemo(() => {
    return buildScheduledJobs(
      { start: addDays(startOfToday(), -6), end: addDays(startOfToday(), 1) },
      matIds.matsById,
      schJobs,
      currentSt
    );
  }, [matIds.matsById, schJobs, currentSt]);

  const sorted = React.useMemo(() => sortJobs(jobs, sortBy, order), [jobs, sortBy, order]);

  return (
    <>
      <Table>
        <JobsHeader
          jobs={jobs}
          showMaterial={showMaterial}
          sortBy={sortBy}
          setSortBy={setSortBy}
          order={order}
          setOrder={setOrder}
        />
        <TableBody>
          {sorted.map((job, jobIdx) => (
            <JobsRow
              key={job.inProcJob?.unique ?? job.historicJob?.unique ?? jobIdx}
              job={job}
              showDarkRow={sortBy === SortColumn.Date}
              showMaterial={showMaterial}
              setCurEditNoteJob={setCurEditNoteJob}
            />
          ))}
        </TableBody>
      </Table>
      <EditNoteDialog job={curEditNoteJob?.historicJob ?? null} closeDialog={() => setCurEditNoteJob(null)} />
    </>
  );
});

export const existRecentScheduledJobs = selector<boolean>({
  key: "insight-exist-recent-scheduled-jobs",
  get: ({ get }) => Object.keys(get(currentStatus).jobs).length > 0 || get(last30Jobs).size > 0,
});

export function RecentSchedulesPage(): JSX.Element {
  React.useEffect(() => {
    document.title = "Scheduled Jobs - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <RecentSchedulesCard />
    </main>
  );
}
