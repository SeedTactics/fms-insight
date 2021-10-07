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
import { addDays, startOfToday } from "date-fns";
import { InspectionSankey } from "../analysis/InspectionSankey";
import { useRecoilValue } from "recoil";
import { last30Inspections } from "../../cell-status/inspections";

const SelectedInspections = React.memo(function SelectedInspections() {
  const inspections = useRecoilValue(last30Inspections);
  const filtered = React.useMemo(() => {
    const today = startOfToday();
    const start = addDays(today, -6);
    const end = addDays(today, 1);
    return inspections.mapValues((log) => log.filter((e) => e.time >= start && e.time <= end));
  }, [inspections]);

  return (
    <InspectionSankey
      inspectionlogs={filtered}
      default_date_range={[addDays(startOfToday(), -6), addDays(startOfToday(), 1)]}
      defaultToTable={false}
      subtitle="Paths from the last 7 days"
    />
  );
});

export function QualityPaths(): JSX.Element {
  React.useEffect(() => {
    document.title = "Paths - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <div data-testid="failed-parts">
        <SelectedInspections />
      </div>
    </main>
  );
}
