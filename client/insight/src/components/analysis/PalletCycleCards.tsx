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
import { useMemo } from "react";
import { addMonths, addDays, startOfToday } from "date-fns";
import { Box, FormControl, Typography } from "@mui/material";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { ImportExport } from "@mui/icons-material";

import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { CycleChart, CycleChartPoint, YZoomRange } from "./CycleChart.js";
import { copyPalletCyclesToClipboard } from "../../data/results.cycles.js";
import { isDemoAtom, useSetTitle } from "../routes.js";
import { last30PalletCycles, specificMonthPalletCycles } from "../../cell-status/pallet-cycles.js";
import { atom, useAtom, useAtomValue } from "jotai";
import { atomWithDefault } from "jotai/utils";

const selectedPalletAtom = atomWithDefault<number | undefined>((get) => (get(isDemoAtom) ? 3 : undefined));
const zoomDateRangeAtom = atom<{ start: Date; end: Date } | undefined>(undefined);
const yZoomAtom = atom<YZoomRange | null>(null);

export function PalletCycleChart() {
  useSetTitle("Pallet Cycles");
  const [selectedPallet, setSelectedPallet] = useAtom(selectedPalletAtom);
  const [zoomDateRange, setZoomRange] = useAtom(zoomDateRangeAtom);
  const [yZoom, setYZoom] = useAtom(yZoomAtom);

  const period = useAtomValue(selectedAnalysisPeriod);
  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  const palletCycles = useAtomValue(
    period.type === "Last30" ? last30PalletCycles : specificMonthPalletCycles,
  );
  const points = useMemo(() => {
    if (selectedPallet) {
      const palData = palletCycles.get(selectedPallet);
      if (palData !== undefined) {
        return new Map<string, ReadonlyArray<CycleChartPoint>>([
          [selectedPallet.toString(), Array.from(palData.valuesToLazySeq())],
        ]);
      }
    }
    return new Map<string, ReadonlyArray<CycleChartPoint>>();
  }, [selectedPallet, palletCycles]);
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
        <Typography variant="subtitle1">Pallet Cycles</Typography>
        <Box flexGrow={1} />
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={selectedPallet || ""}
            onChange={(e) => setSelectedPallet(e.target.value as number)}
          >
            {selectedPallet !== undefined ? undefined : (
              <MenuItem key={0} value="">
                <em>Select Pallet</em>
              </MenuItem>
            )}
            {palletCycles
              .keysToLazySeq()
              .sortBy((x) => x)
              .map((n) => (
                <MenuItem key={n} value={n}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <span style={{ marginRight: "1em" }}>{n}</span>
                  </div>
                </MenuItem>
              ))}
          </Select>
        </FormControl>
        <Tooltip title="Copy to Clipboard">
          <IconButton
            onClick={() => copyPalletCyclesToClipboard(palletCycles)}
            style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
            size="large"
          >
            <ImportExport />
          </IconButton>
        </Tooltip>
      </Box>
      <main>
        <CycleChart
          points={points}
          series_label="Pallet"
          default_date_range={defaultDateRange}
          current_date_zoom={zoomDateRange}
          set_date_zoom_range={(z) => setZoomRange(z.zoom)}
          yZoom={yZoom}
          setYZoom={setYZoom}
        />
      </main>
    </Box>
  );
}
