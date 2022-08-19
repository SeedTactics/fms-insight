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
import { addMonths, addDays, startOfToday } from "date-fns";

import AnalysisSelectToolbar from "./AnalysisSelectToolbar.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { InspectionSankey } from "./InspectionSankey.js";
import { DataTableActionZoomType } from "./DataTable.js";
import { useRecoilValue } from "recoil";
import { last30Inspections, specificMonthInspections } from "../../cell-status/inspections.js";

function ConnectedInspection() {
  const period = useRecoilValue(selectedAnalysisPeriod);

  const inspectionlogs = useRecoilValue(
    period.type === "Last30" ? last30Inspections : specificMonthInspections
  );
  const zoomType =
    period.type === "Last30" ? DataTableActionZoomType.Last30Days : DataTableActionZoomType.ZoomIntoRange;
  const default_date_range =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  return (
    <InspectionSankey
      inspectionlogs={inspectionlogs}
      zoomType={zoomType}
      default_date_range={default_date_range}
      defaultToTable={false}
    />
  );
}

// --------------------------------------------------------------------------------
// Efficiency
// --------------------------------------------------------------------------------

export function QualityCards(): JSX.Element {
  return (
    <>
      <div style={{ marginTop: "3em" }}>
        <ConnectedInspection />
      </div>
    </>
  );
}

export function QualityPage(): JSX.Element {
  React.useEffect(() => {
    document.title = "Efficiency - FMS Insight";
  }, []);
  return (
    <>
      <AnalysisSelectToolbar />
      <main style={{ padding: "24px" }}>
        <QualityCards />
      </main>
    </>
  );
}
