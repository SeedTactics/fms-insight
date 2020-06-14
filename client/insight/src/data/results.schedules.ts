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

import { HashMap } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { PartCycleData } from "./events.cycles";
import { ScheduledJob } from "./events.scheduledjobs";
import { ICurrentStatus } from "./api";
// eslint-disable-next-line @typescript-eslint/no-var-requires
const copy = require("copy-to-clipboard");

export interface ScheduledJobDisplay extends ScheduledJob {
  readonly completedQty: number;
  readonly inProcessQty: number;
  readonly remainingQty: number;
  readonly darkRow: boolean;
}

type WritableScheduledJob = { -readonly [K in keyof ScheduledJobDisplay]: ScheduledJobDisplay[K] };

export function buildScheduledJobs(
  start: Date,
  end: Date,
  cycles: Iterable<PartCycleData>,
  schJobs: HashMap<string, ScheduledJob>,
  currentSt: Readonly<ICurrentStatus>
): ReadonlyArray<ScheduledJobDisplay> {
  const filteredCycles = LazySeq.ofIterable(cycles).filter((e) => e.x >= start && e.x <= end);

  const result = new Map<string, WritableScheduledJob>();

  for (const [uniq, job] of schJobs) {
    if (job.startingTime >= start && job.startingTime <= end) {
      result.set(uniq, { ...job, completedQty: 0, inProcessQty: 0, darkRow: false, remainingQty: 0 });
    }
  }

  for (const cycle of filteredCycles) {
    if (cycle.completed) {
      for (const mat of cycle.material) {
        if (mat.uniq) {
          const job = result.get(mat.uniq);
          if (job) {
            job.completedQty += 1;
          }
        }
      }
    }
  }

  for (const mat of currentSt.material) {
    if (mat.jobUnique) {
      const job = result.get(mat.jobUnique);
      if (job) {
        job.inProcessQty += 1;
      }
    }
  }

  for (const [uniq, curJob] of LazySeq.ofObject(currentSt.jobs)) {
    const job = result.get(uniq);
    if (job) {
      const plannedQty = LazySeq.ofIterable(curJob.cyclesOnFirstProcess).sumOn((c) => c);
      const startedQty = LazySeq.ofIterable(curJob.completed?.[0] ?? []).sumOn((c) => c);
      job.remainingQty += plannedQty - startedQty;
      if (plannedQty < job.scheduledQty) {
        job.decrementedQty += job.scheduledQty - plannedQty;
      }
    }
  }

  const sorted = Array.from(result.values()).sort((j1, j2) => {
    // sort starting time high to low, then by part
    const timeDiff = j2.startingTime.getTime() - j1.startingTime.getTime();
    if (timeDiff == 0) {
      return j1.partName.localeCompare(j2.partName);
    } else {
      return timeDiff;
    }
  });

  let lastSchId: string | null = null;
  let curDark = true;
  for (const job of sorted) {
    if (lastSchId != job.scheduleId) {
      curDark = !curDark;
      lastSchId = job.scheduleId;
    }
    job.darkRow = curDark;
  }

  return sorted;
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

export function buildScheduledJobsTable(jobs: ReadonlyArray<ScheduledJobDisplay>, showMaterial: boolean) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Date</th>";
  table += "<th>Part</th>";
  if (showMaterial) {
    table += "<th>Material</th>";
  }
  table += "<th>Note</th>";
  table += "<th>Scheduled</th>";
  table += "<th>Removed</th>";
  table += "<th>Completed</th>";
  table += "<th>In Process</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (const s of jobs) {
    table += "<tr><td>" + s.startingTime.toString() + "</td>";
    table += "<td>" + s.partName + "</td>";
    if (showMaterial) {
      table += "<td>" + (s.casting ?? s.partName) + "</td>";
    }
    table += "<td>" + (s.comment ?? "") + "</td>";
    table += "<td>" + s.scheduledQty.toFixed(0) + "</td>";
    table += "<td>" + s.decrementedQty.toFixed(0) + "</td>";
    table += "<td>" + s.completedQty.toFixed(0) + "</td>";
    table += "<td>" + s.inProcessQty.toFixed(0) + "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyScheduledJobsToClipboard(jobs: ReadonlyArray<ScheduledJobDisplay>, showMaterial: boolean): void {
  copy(buildScheduledJobsTable(jobs, showMaterial));
}
