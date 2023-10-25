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
import { last30Jobs, specificMonthJobs } from "../../cell-status/scheduled-jobs.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { last30MaterialSummary, specificMonthMaterialSummary } from "../../cell-status/material-summary.js";
import {
  Column,
  DataTableActions,
  DataTableHead,
  useColSort,
  useTablePage,
  useTableZoomForPeriod,
} from "./DataTable.js";
import { buildScheduledJobs, ScheduledJobDisplay } from "../../data/results.schedules.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { Box, Collapse, IconButton, Table, TableBody, TableCell, Tooltip } from "@mui/material";
import {
  KeyboardArrowDown as KeyboardArrowDownIcon,
  KeyboardArrowUp as KeyboardArrowUpIcon,
} from "@mui/icons-material";
import { JobDetailRow, JobTableRow } from "../operations/RecentSchedules.js";
import { JobDetails } from "../station-monitor/JobDetails.js";
import { PartIdenticon } from "../station-monitor/Material.js";
import { useSetTitle } from "../routes.js";
import { useAtomValue } from "jotai";

enum ScheduleCols {
  Date,
  Part,
  Scheduled,
  Removed,
  Completed,
}

const cols: ReadonlyArray<Column<ScheduleCols, ScheduledJobDisplay>> = [
  {
    id: ScheduleCols.Date,
    label: "Date",
    numeric: false,
    getDisplay: (j) => j.routeStartTime.toLocaleString(),
    getForSort: (j) => j.routeStartTime.getTime(),
    getForExport: (j) => j.routeStartTime.toISOString(),
  },
  { id: ScheduleCols.Part, label: "Part", numeric: false, getDisplay: (j) => j.partName },
  {
    id: ScheduleCols.Scheduled,
    label: "Scheduled",
    numeric: true,
    getDisplay: (j) => j.scheduledQty.toString(),
    getForSort: (j) => j.scheduledQty,
  },
  {
    id: ScheduleCols.Removed,
    label: "Removed",
    numeric: true,
    getDisplay: (j) => j.decrementedQty.toString(),
    getForSort: (j) => j.decrementedQty,
  },
  {
    id: ScheduleCols.Completed,
    label: "Completed",
    numeric: true,
    getDisplay: (j) => j.completedQty.toString(),
  },
];

function JobRow({ job }: { readonly job: ScheduledJobDisplay }) {
  const [open, setOpen] = React.useState<boolean>(false);
  return (
    <>
      <JobTableRow>
        {cols.map((col) => (
          <TableCell key={col.id} align={col.numeric ? "right" : "left"}>
            {col.id === ScheduleCols.Part ? (
              <Box sx={{ display: "flex", alignItems: "center" }}>
                <Box sx={{ mr: "0.2em" }}>
                  <PartIdenticon part={job.partName} size={25} />
                </Box>
                <div>{job.partName}</div>
              </Box>
            ) : (
              col.getDisplay(job)
            )}
          </TableCell>
        ))}
        <TableCell>
          <Tooltip title="Show Details">
            <IconButton size="small" onClick={() => setOpen(!open)}>
              {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
            </IconButton>
          </Tooltip>
        </TableCell>
      </JobTableRow>
      <JobDetailRow>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={cols.length + 1}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <JobDetails job={job.inProcJob ? job.inProcJob : job.historicJob} checkAnalysisMonth={true} />
          </Collapse>
        </TableCell>
      </JobDetailRow>
    </>
  );
}

export function ScheduleTable() {
  const period = useAtomValue(selectedAnalysisPeriod);
  const tpage = useTablePage();
  const zoom = useTableZoomForPeriod(period);
  const sort = useColSort(ScheduleCols.Date, cols);

  const matIds = useAtomValue(
    period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary,
  );
  const schJobs = useAtomValue(period.type === "Last30" ? last30Jobs : specificMonthJobs);

  const jobs = React.useMemo(
    () => buildScheduledJobs(zoom.zoomRange, matIds.matsById, schJobs, null),
    [zoom, matIds.matsById, schJobs],
  );
  const page = React.useMemo(
    () =>
      LazySeq.of(jobs)
        .sortBy(sort.sortOn)
        .drop(tpage.page * tpage.rowsPerPage)
        .take(tpage.rowsPerPage),
    [jobs, sort, tpage],
  );

  return (
    <div>
      <Table>
        <DataTableHead columns={cols} sort={sort} showDetailsCol={true} copyToClipboardRows={jobs} />
        <TableBody>
          {page.map((j, jIdx) => (
            <JobRow key={j.historicJob?.unique ?? j.inProcJob?.unique ?? jIdx} job={j} />
          ))}
        </TableBody>
      </Table>
      <DataTableActions tpage={tpage} zoom={zoom.zoom} count={jobs.length} />
    </div>
  );
}

export function ScheduleHistory(): JSX.Element {
  useSetTitle("Scheduled Jobs");
  return (
    <>
      <main style={{ padding: "24px" }}>
        <ScheduleTable />
      </main>
    </>
  );
}
