/* Copyright (c) 2023, John Lenz

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
import { Box, Typography } from "@mui/material";
import { addDays, startOfToday } from "date-fns";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { ImportExport } from "@mui/icons-material";

import StationDataTable from "../analysis/StationDataTable.js";
import { outlierMachineCycles, outlierLoadCycles, copyCyclesToClipboard } from "../../data/results.cycles.js";
import { useRecoilValue } from "recoil";
import { last30MaterialSummary } from "../../cell-status/material-summary.js";
import { last30EstimatedCycleTimes } from "../../cell-status/estimated-cycle-times.js";
import { last30StationCycles } from "../../cell-status/station-cycles.js";
import { useSetTitle } from "../routes.js";

// -----------------------------------------------------------------------------------
// Outliers
// -----------------------------------------------------------------------------------

export type OutlierType = "labor" | "machine";

export function OutlierCycles({ outlierTy }: { outlierTy: OutlierType }) {
  useSetTitle(outlierTy === "machine" ? "Machine Outliers" : "L/U Outliers");
  const matSummary = useRecoilValue(last30MaterialSummary);
  const today = startOfToday();
  const estimatedCycleTimes = useRecoilValue(last30EstimatedCycleTimes);
  const allCycles = useRecoilValue(last30StationCycles);
  const points = React.useMemo(() => {
    const today = startOfToday();
    if (outlierTy === "labor") {
      return outlierLoadCycles(
        allCycles.valuesToLazySeq(),
        addDays(today, -4),
        addDays(today, 1),
        estimatedCycleTimes
      );
    } else {
      return outlierMachineCycles(
        allCycles.valuesToLazySeq(),
        addDays(today, -4),
        addDays(today, 1),
        estimatedCycleTimes
      );
    }
  }, [outlierTy, estimatedCycleTimes, allCycles]);

  return (
    <Box paddingLeft="24px" paddingRight="24px" paddingTop="10px">
      <Box
        component="nav"
        sx={{
          display: "flex",
          minHeight: "2.5em",
          alignItems: "center",
          maxWidth: "calc(100vw - 24px - 24px)",
        }}
      >
        <Typography variant="subtitle1">
          {outlierTy === "labor" ? "Load/Unload" : "Machine"} cycles from the past 5 days statistically
          outside expected range
        </Typography>
        <Box flexGrow={1} />
        <Tooltip title="Copy to Clipboard">
          <IconButton
            style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
            onClick={() => copyCyclesToClipboard(points, matSummary.matsById, undefined)}
            size="large"
          >
            <ImportExport />
          </IconButton>
        </Tooltip>
      </Box>
      <main>
        <StationDataTable
          points={points.data}
          matsById={matSummary.matsById}
          current_date_zoom={{ start: addDays(today, -4), end: addDays(today, 1) }}
          set_date_zoom_range={undefined}
          period={{ type: "Last30" }}
          showWorkorderAndInspect={false}
          defaultSortDesc
        />
      </main>
    </Box>
  );
}
