/* Copyright (c) 2019, John Lenz

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
import { createSelector } from "reselect";
import { Last30Days } from "../../data/events";
import { addDays, startOfToday } from "date-fns";
import { ScheduledJobDisplay, buildScheduledJobs, copyScheduledJobsToClipboard } from "../../data/results.schedules";
import { ICurrentStatus, IInProcessJob } from "../../data/api";
import { PartIdenticon } from "../station-monitor/Material";
import { makeStyles, createStyles } from "@material-ui/core/styles";
import { ConnectedEditNoteDialog } from "../station-monitor/Queues";
import { MoreHoriz } from "@material-ui/icons";
import { JobDetailDialog, JobPlanDialog } from "../station-monitor/JobDetails";

interface JobsTableProps {
  readonly jobs: ReadonlyArray<ScheduledJobDisplay>;
  readonly showMaterial: boolean;
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
    darkRow: {
      backgroundColor: "#E0E0E0",
    },
    highlightedCell: {
      backgroundColor: "#FF8A65",
    },
  })
);

function JobsTable(props: JobsTableProps) {
  const classes = useTableStyles();
  const [curEditNoteJob, setCurEditNoteJob] = React.useState<ScheduledJobDisplay | null>(null);
  const [jobDetailsToShow, setJobDetailsToShow] = React.useState<Readonly<IInProcessJob> | null>(null);
  const [jobPlanToLoad, setJobPlanToLoad] = React.useState<string | null>(null);
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
                onClick={() => copyScheduledJobsToClipboard(props.jobs, props.showMaterial)}
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
              <TableCell>Note</TableCell>
              <TableCell align="right">Scheduled</TableCell>
              <TableCell align="right">Removed</TableCell>
              <TableCell align="right">Completed</TableCell>
              <TableCell align="right">In Process</TableCell>
              <TableCell align="right">Remaining To Run</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {props.jobs.map((job, jobIdx) => (
              <TableRow key={jobIdx} className={job.darkRow ? classes.darkRow : undefined}>
                <TableCell>{job.startingTime.toLocaleString()}</TableCell>
                <TableCell>
                  <div className={classes.labelContainer}>
                    <div className={classes.identicon}>
                      <PartIdenticon part={job.partName} size={25} />
                    </div>
                    <div>
                      <Typography variant="body2" component="span" display="block">
                        {job.partName}
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
                <TableCell>
                  {job.comment}

                  <Tooltip title="Edit">
                    <IconButton size="small" onClick={() => setCurEditNoteJob(job)}>
                      <EditIcon />
                    </IconButton>
                  </Tooltip>
                </TableCell>
                <TableCell align="right">{job.scheduledQty}</TableCell>
                <TableCell align="right" className={job.decrementedQty > 0 ? classes.highlightedCell : undefined}>
                  {job.decrementedQty}
                </TableCell>
                <TableCell align="right">{job.completedQty}</TableCell>
                <TableCell align="right">{job.inProcessQty}</TableCell>
                <TableCell align="right">{job.remainingQty}</TableCell>
                <TableCell>
                  <Tooltip title="Show Details">
                    <IconButton
                      size="small"
                      onClick={() =>
                        job.inProcJob !== null ? setJobDetailsToShow(job.inProcJob) : setJobPlanToLoad(job.unique)
                      }
                    >
                      <MoreHoriz />
                    </IconButton>
                  </Tooltip>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <ConnectedEditNoteDialog job={curEditNoteJob} closeDialog={() => setCurEditNoteJob(null)} />
        <JobDetailDialog job={jobDetailsToShow} close={() => setJobDetailsToShow(null)} />
        <JobPlanDialog unique={jobPlanToLoad} close={() => setJobPlanToLoad(null)} />
      </CardContent>
    </Card>
  );
}

const scheduledJobsSelector = createSelector(
  (last30: Last30Days, _s: Readonly<ICurrentStatus>, _: Date) => last30.cycles.part_cycles,
  (last30: Last30Days, _s: Readonly<ICurrentStatus>, _: Date) => last30.scheduled_jobs.jobs,
  (_: Last30Days, st: Readonly<ICurrentStatus>, _d: Date) => st,
  (_: Last30Days, _s: Readonly<ICurrentStatus>, today: Date) => today,
  (cycles, jobs, st, today): ReadonlyArray<ScheduledJobDisplay> => {
    const start = addDays(today, -6);
    const end = addDays(today, 1);
    return buildScheduledJobs(start, end, cycles, jobs, st);
  }
);

const ConnectedJobsTable = connect((st) => ({
  jobs: scheduledJobsSelector(st.Events.last30, st.Current.current_status, startOfToday()),
  showMaterial: st.Events.last30.scheduled_jobs.someJobHasCasting,
}))(JobsTable);

export function CompletedParts() {
  React.useEffect(() => {
    document.title = "Scheduled Jobs - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <div data-testid="scheduled-jobs">
        <ConnectedJobsTable />
      </div>
    </main>
  );
}
