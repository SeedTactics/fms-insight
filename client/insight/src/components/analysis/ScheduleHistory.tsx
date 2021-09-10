/* Copyright (c) 2021, John Lenz

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
import { addDays, addMonths, startOfToday } from "date-fns";
import * as React from "react";
import { useRecoilValue } from "recoil";
import { last30Jobs, specificMonthJobs } from "../../cell-status/scheduled-jobs";
import { useSelector } from "../../store/store";
import { JobsTable } from "../operations/CompletedParts";
import AnalysisSelectToolbar, { selectedAnalysisPeriod } from "./AnalysisSelectToolbar";

function ConnectedSchedules() {
  const period = useRecoilValue(selectedAnalysisPeriod);

  const matIds = useSelector((st) =>
    period.type === "Last30" ? st.Events.last30.mat_summary.matsById : st.Events.selected_month.mat_summary.matsById
  );
  const schJobs = useRecoilValue(period.type === "Last30" ? last30Jobs : specificMonthJobs);
  const start = period.type === "Last30" ? addDays(startOfToday(), -29) : period.month;
  const end = period.type === "Last30" ? addDays(startOfToday(), 1) : addMonths(period.month, 1);

  return <JobsTable matIds={matIds} schJobs={schJobs} showInProcCnt={false} start={start} end={end} />;
}

export function ScheduleHistory(): JSX.Element {
  React.useEffect(() => {
    document.title = "Scheduled Jobs - FMS Insight";
  }, []);
  return (
    <>
      <AnalysisSelectToolbar />
      <main style={{ padding: "24px" }}>
        <ConnectedSchedules />
      </main>
    </>
  );
}
