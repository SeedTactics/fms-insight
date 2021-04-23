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
import Card from "@material-ui/core/Card";
import CardHeader from "@material-ui/core/CardHeader";
import CardContent from "@material-ui/core/CardContent";
import IconButton from "@material-ui/core/IconButton";
import Tooltip from "@material-ui/core/Tooltip";
import ImportExport from "@material-ui/icons/ImportExport";
import ExtensionIcon from "@material-ui/icons/Extension";
import EditIcon from "@material-ui/icons/Edit";
import Typography from "@material-ui/core/Typography";
import Table from "@material-ui/core/Table";
import TableRow from "@material-ui/core/TableRow";
import TableCell from "@material-ui/core/TableCell";
import TableHead from "@material-ui/core/TableHead";
import TableBody from "@material-ui/core/TableBody";
import { connect } from "../../store/store";
import { addDays, startOfToday } from "date-fns";
import { ScheduledJobDisplay, buildScheduledJobs, copyScheduledJobsToClipboard } from "../../data/results.schedules";
import { IHistoricJob } from "../../data/api";
import { PartIdenticon } from "../station-monitor/Material";
import { makeStyles, createStyles } from "@material-ui/core/styles";
import { ConnectedEditNoteDialog } from "../station-monitor/Queues";
import KeyboardArrowDownIcon from "@material-ui/icons/KeyboardArrowDown";
import KeyboardArrowUpIcon from "@material-ui/icons/KeyboardArrowUp";
import { JobDetails } from "../station-monitor/JobDetails";
import Collapse from "@material-ui/core/Collapse";
import clsx from "clsx";
import { HashMap } from "prelude-ts";
import { useRecoilValue } from "recoil";
import { currentStatus } from "../../data/current-status";
import { MaterialSummaryAndCompletedData } from "../../data/events.matsummary";

export interface JobsTableProps {
  readonly schJobs: HashMap<string, Readonly<IHistoricJob>>;
  readonly matIds: HashMap<number, MaterialSummaryAndCompletedData>;
  readonly showMaterial: boolean;
  readonly showInProcCnt: boolean;
  readonly start: Date;
  readonly end: Date;
}

const useTableStyles = makeStyles((theme) =>
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
    darkRow: {
      backgroundColor: "#F5F5F5",
    },
    highlightedCell: {
      backgroundColor: "#FF8A65",
    },
    collapseCell: {
      paddingBottom: 0,
      paddingTop: 0,
    },
  })
);

interface JobsRowProps {
  readonly job: ScheduledJobDisplay;
  readonly showMaterial: boolean;
  readonly showInProcCnt: boolean;
  readonly setCurEditNoteJob: (j: ScheduledJobDisplay) => void;
}

function JobsRow(props: JobsRowProps) {
  const classes = useTableStyles();
  const [open, setOpen] = React.useState<boolean>(false);

  let colCnt = 6;
  if (props.showMaterial) colCnt += 1;
  if (props.showInProcCnt) colCnt += 3;

  const job = props.job;
  return (
    <>
      <TableRow className={clsx({ [classes.mainRow]: true, [classes.darkRow]: job.darkRow })}>
        <TableCell>{job.historicJob.routeStartUTC.toLocaleString()}</TableCell>
        <TableCell>
          <div className={classes.labelContainer}>
            <div className={classes.identicon}>
              <PartIdenticon part={job.historicJob.partName} size={25} />
            </div>
            <div>
              <Typography variant="body2" component="span" display="block">
                {job.historicJob.partName}
              </Typography>
            </div>
          </div>
        </TableCell>
        {props.showMaterial ? (
          <TableCell>
            {job.casting ? (
              <div className={classes.labelContainer}>
                <div className={classes.identicon}>
                  <PartIdenticon part={job.casting} size={25} />
                </div>
                <Typography variant="body2" display="block">
                  {job.casting}
                </Typography>
              </div>
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
        <TableCell align="right" className={job.decrementedQty > 0 ? classes.highlightedCell : undefined}>
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
      </TableRow>
      <TableRow className={job.darkRow ? classes.darkRow : undefined}>
        <TableCell className={classes.collapseCell} colSpan={colCnt}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <JobDetails
              job={job.inProcJob ? job.inProcJob : job.historicJob}
              checkAnalysisMonth={!props.showInProcCnt}
            />
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

export function JobsTable(props: JobsTableProps) {
  const [curEditNoteJob, setCurEditNoteJob] = React.useState<ScheduledJobDisplay | null>(null);
  const currentSt = useRecoilValue(currentStatus);

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
                onClick={() => copyScheduledJobsToClipboard(jobs, props.showMaterial)}
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
              {props.showMaterial ? <TableCell>Material</TableCell> : undefined}
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
                showMaterial={props.showMaterial}
                setCurEditNoteJob={setCurEditNoteJob}
                showInProcCnt={props.showInProcCnt}
              />
            ))}
          </TableBody>
        </Table>
        <ConnectedEditNoteDialog
          job={curEditNoteJob?.historicJob ?? null}
          closeDialog={() => setCurEditNoteJob(null)}
        />
      </CardContent>
    </Card>
  );
}

export const RecentSchedules = connect((st) => ({
  matIds: st.Events.last30.mat_summary.matsById,
  schJobs: st.Events.last30.scheduled_jobs.jobs,
  showMaterial: st.Events.last30.scheduled_jobs.someJobHasCasting,
  start: addDays(startOfToday(), -6),
  end: addDays(startOfToday(), 1),
}))(JobsTable);

export function CompletedParts() {
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
