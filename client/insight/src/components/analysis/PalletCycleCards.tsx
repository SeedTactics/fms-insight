/* Copyright (c) 2025, John Lenz

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
import { addMonths, addDays, startOfToday } from "date-fns";
import { Box, FormControl, Typography } from "@mui/material";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { ImportExport } from "@mui/icons-material";

import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { CycleChart, CycleChartPoint, ExtraTooltip, YZoomRange } from "./CycleChart.js";
import { copyPalletCyclesToClipboard, PartAndProcess } from "../../data/results.cycles.js";
import { isDemoAtom, useSetTitle } from "../routes.js";
import {
  last30PalletCycles,
  PalletCycleData,
  specificMonthPalletCycles,
} from "../../cell-status/pallet-cycles.js";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";
import { atomWithDefault } from "jotai/utils";
import { LazySeq } from "@seedtactics/immutable-collections";
import { PartIdenticon } from "../station-monitor/Material.js";
import { materialDialogOpen } from "../../cell-status/material-details.js";
import { useCallback } from "react";

const selectedPalletAtom = atomWithDefault<number | undefined>((get) => (get(isDemoAtom) ? 3 : undefined));
const selectedPartAtom = atom<PartAndProcess | undefined>(undefined);
const zoomDateRangeAtom = atom<{ start: Date; end: Date } | undefined>(undefined);
const yZoomAtom = atom<YZoomRange | null>(null);

const filteredPoints = atom((get) => {
  const selPal = get(selectedPalletAtom);
  const selPart = get(selectedPartAtom);
  if (!selPal && !selPart) return new Map<string, ReadonlyArray<CycleChartPoint>>();

  const period = get(selectedAnalysisPeriod);
  const palletCycles = get(period.type === "Last30" ? last30PalletCycles : specificMonthPalletCycles);

  if (selPal) {
    // Pick out the single pallet and optionally filter it by part
    let cyclesForPal = palletCycles.get(selPal)?.valuesToLazySeq() ?? LazySeq.of([]);
    if (selPart) {
      cyclesForPal = cyclesForPal.filter((c) =>
        c.mats.some((m) => m.part === selPart.part && m.proc === selPart.proc),
      );
    }
    return new Map<string, ReadonlyArray<CycleChartPoint>>([[selPal.toString(), Array.from(cyclesForPal)]]);
  } else if (selPart) {
    // go through all the pallets, filter each by part, and keep those that have any cycles
    return LazySeq.of(palletCycles)
      .collect(([pal, cycles]) => {
        const cyclesForPart = cycles
          .valuesToLazySeq()
          .filter((c) => c.mats.some((m) => m.part === selPart.part && m.proc === selPart.proc))
          .toRArray();
        if (cyclesForPart.length > 0) {
          return [pal, cyclesForPart] as const;
        } else {
          return null;
        }
      })
      .toRMap(([pal, cycles]) => [pal.toString(), cycles]);
  } else {
    return new Map<string, ReadonlyArray<CycleChartPoint>>();
  }
});

const allPartNames = atom((get) => {
  const period = get(selectedAnalysisPeriod);
  const palletCycles = get(period.type === "Last30" ? last30PalletCycles : specificMonthPalletCycles);

  return LazySeq.of(palletCycles)
    .flatMap(([, cycles]) => cycles)
    .flatMap(([, c]) => c.mats)
    .distinctAndSortBy(
      (m) => m.part,
      (m) => m.proc,
    )
    .map((m) => ({ part: m.part, proc: m.proc }))
    .toRArray();
});

export function PalletCycleChart() {
  useSetTitle("Pallet Cycles");
  const [selectedPallet, setSelectedPallet] = useAtom(selectedPalletAtom);
  const [selectedPart, setSelectedPart] = useAtom(selectedPartAtom);
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
  const points = useAtomValue(filteredPoints);
  const partNames = useAtomValue(allPartNames);

  const setMatToShow = useSetAtom(materialDialogOpen);
  const extraPalletTooltip = useCallback(
    function extraPalletTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
      const palC = point as PalletCycleData;
      return palC.mats.map((mat) => ({
        title: mat.part + "-" + mat.proc,
        value: mat.serial ?? "",
        link: mat.serial ? () => setMatToShow({ type: "LogMat", logMat: mat }) : undefined,
      }));
    },
    [setMatToShow],
  );

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
            value={selectedPallet ?? -1}
            onChange={(e) =>
              setSelectedPallet(e.target.value === -1 ? undefined : (e.target.value as number))
            }
          >
            <MenuItem key={0} value={-1}>
              <em>Any Pallet</em>
            </MenuItem>
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
        {partNames.length > 0 ? (
          <FormControl size="small">
            <Select
              autoWidth
              displayEmpty
              value={
                selectedPart
                  ? partNames.findIndex((o) => selectedPart.part === o.part && selectedPart.proc === o.proc)
                  : -1
              }
              style={{ marginLeft: "1em" }}
              onChange={(e) => {
                setSelectedPart(e.target.value === -1 ? undefined : partNames[e.target.value as number]);
              }}
            >
              <MenuItem key={0} value={-1}>
                <em>Any Part</em>
              </MenuItem>
              {partNames.map((n, idx) => (
                <MenuItem key={idx} value={idx}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <PartIdenticon part={n.part} size={20} />
                    <span style={{ marginRight: "1em" }}>
                      {n.part}-{n.proc}
                    </span>
                  </div>
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        ) : undefined}
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
          extra_tooltip={extraPalletTooltip}
        />
      </main>
    </Box>
  );
}
