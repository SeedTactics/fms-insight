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

import { MaterialSummaryAndCompletedData } from "../cell-status/material-summary.js";
import { ICurrentStatus, IHistoricJob, IActiveJob } from "../network/api.js";
import copy from "copy-to-clipboard";
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";

export interface ScheduledJobDisplay {
  readonly historicJob: Readonly<IHistoricJob>;
  readonly inProcJob: Readonly<IActiveJob> | null;
  readonly casting: string;
  readonly scheduledQty: number;
  readonly decrementedQty: number;
  readonly completedQty: number;
  readonly inProcessQty: number;
  readonly remainingQty: number;
  readonly darkRow: boolean;
}

type WritableScheduledJob = { -readonly [K in keyof ScheduledJobDisplay]: ScheduledJobDisplay[K] };

export function buildScheduledJobs(
  start: Date,
  end: Date,
  matIds: HashMap<number, MaterialSummaryAndCompletedData>,
  schJobs: HashMap<string, Readonly<IHistoricJob>>,
  currentSt: Readonly<ICurrentStatus>
): ReadonlyArray<ScheduledJobDisplay> {
  const completedMats = LazySeq.of(matIds)
    .flatMap(([matId, summary]) =>
      LazySeq.ofObject(summary.unloaded_processes ?? {}).map(([proc, _]) => ({
        matId: matId,
        proc: parseInt(proc),
        uniq: summary.jobUnique,
      }))
    )
    .toLookupMap(
      (m) => m.uniq,
      (m) => m.proc,
      () => 1,
      (a, b) => a + b
    );

  const result = new Map<string, WritableScheduledJob>();

  for (const [uniq, job] of schJobs) {
    if (job.routeStartUTC >= start && job.routeStartUTC <= end) {
      const casting = LazySeq.of(job.procsAndPaths[0]?.paths ?? [])
        .collect((p) => (p.casting === "" ? null : p.casting))
        .head();

      result.set(uniq, {
        ...job,
        historicJob: job,
        inProcJob: null,
        casting: casting ?? "",
        scheduledQty: job.cycles ?? 0,
        decrementedQty: LazySeq.of(job.decrements || []).sumBy((d) => d.quantity),
        completedQty: completedMats.get(uniq)?.get(job.procsAndPaths.length) ?? 0,
        inProcessQty: 0,
        darkRow: false,
        remainingQty: 0,
      });
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
      const plannedQty = curJob.cycles ?? 0;
      const completedQty = LazySeq.of(curJob.completed?.[curJob.completed?.length - 1] ?? []).sumBy((c) => c);
      job.remainingQty = plannedQty - job.inProcessQty - completedQty;
      job.inProcJob = curJob;
      if (plannedQty < job.scheduledQty) {
        job.decrementedQty = job.scheduledQty - plannedQty;
      }
    }
  }

  const sorted = Array.from(result.values()).sort((j1, j2) => {
    // sort starting time high to low, then by part
    const timeDiff = j2.historicJob.routeStartUTC.getTime() - j1.historicJob.routeStartUTC.getTime();
    if (timeDiff == 0) {
      return j1.historicJob.partName.localeCompare(j2.historicJob.partName);
    } else {
      return timeDiff;
    }
  });

  let lastSchId: string | null = null;
  let curDark = true;
  for (const job of sorted) {
    if (lastSchId != (job.historicJob.scheduleId ?? null)) {
      curDark = !curDark;
      lastSchId = job.historicJob.scheduleId ?? null;
    }
    job.darkRow = curDark;
  }

  return sorted;
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

export function buildScheduledJobsTable(
  jobs: ReadonlyArray<ScheduledJobDisplay>,
  showMaterial: boolean
): string {
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
    table += "<tr><td>" + s.historicJob.routeStartUTC.toString() + "</td>";
    table += "<td>" + s.historicJob.partName + "</td>";
    if (showMaterial) {
      table += "<td>" + (s.casting ?? s.historicJob.partName) + "</td>";
    }
    table += "<td>" + (s.historicJob.comment ?? "") + "</td>";
    table += "<td>" + s.scheduledQty.toFixed(0) + "</td>";
    table += "<td>" + s.decrementedQty.toFixed(0) + "</td>";
    table += "<td>" + s.completedQty.toFixed(0) + "</td>";
    table += "<td>" + s.inProcessQty.toFixed(0) + "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyScheduledJobsToClipboard(
  jobs: ReadonlyArray<ScheduledJobDisplay>,
  showMaterial: boolean
): void {
  copy(buildScheduledJobsTable(jobs, showMaterial));
}
