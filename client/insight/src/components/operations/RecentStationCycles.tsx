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
import { Box, FormControl, Typography } from "@mui/material";
import { addDays, startOfToday } from "date-fns";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { ImportExport } from "@mui/icons-material";

import StationDataTable from "../analysis/StationDataTable.js";
import { PartIdenticon } from "../station-monitor/Material.js";
import {
  filterStationCycles,
  FilterAnyMachineKey,
  copyCyclesToClipboard,
  plannedOperationMinutes,
  loadOccupancyCycles,
  LoadCycleData,
  FilterAnyLoadKey,
  PartAndProcess,
} from "../../data/results.cycles.js";
import * as matDetails from "../../cell-status/material-details.js";
import { CycleChart, CycleChartPoint, ExtraTooltip } from "../analysis/CycleChart.js";
import { useRecoilValue } from "recoil";
import { last30MaterialSummary } from "../../cell-status/material-summary.js";
import {
  last30EstimatedCycleTimes,
  PartAndStationOperation,
} from "../../cell-status/estimated-cycle-times.js";
import { last30StationCycles } from "../../cell-status/station-cycles.js";
import { LazySeq } from "@seedtactics/immutable-collections";

export type CycleType = "labor" | "machine";

export function RecentStationCycleChart({ ty }: { ty: CycleType }) {
  const setMatToShow = matDetails.useSetMaterialToShowInDialog();
  const extraStationCycleTooltip = React.useCallback(
    function extraStationCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
      const partC = point as LoadCycleData;
      const ret = [];
      if (partC.operations) {
        for (const mat of partC.operations) {
          ret.push({
            title: (mat.mat.serial ? mat.mat.serial : "Material") + " " + mat.operation,
            value: "Open Card",
            link: () => setMatToShow({ type: "LogMat", logMat: mat.mat }),
          });
        }
      } else {
        for (const mat of partC.material) {
          ret.push({
            title: mat.serial ? mat.serial : "Material",
            value: "Open Card",
            link: () => setMatToShow({ type: "LogMat", logMat: mat }),
          });
        }
      }
      return ret;
    },
    [setMatToShow]
  );

  const [showGraph, setShowGraph] = React.useState(true);
  const [chartZoom, setChartZoom] = React.useState<{ zoom?: { start: Date; end: Date } }>({});
  const [selectedPart, setSelectedPart] = React.useState<PartAndProcess>();
  const [selectedOperation, setSelectedOperation] = React.useState<PartAndStationOperation>();
  const [selectedPallet, setSelectedPallet] = React.useState<string>();

  const estimatedCycleTimes = useRecoilValue(last30EstimatedCycleTimes);
  const default_date_range = [addDays(startOfToday(), -4), addDays(startOfToday(), 1)];

  const cycles = useRecoilValue(last30StationCycles);
  const matSummary = useRecoilValue(last30MaterialSummary);
  const points = React.useMemo(() => {
    const today = startOfToday();
    if (selectedOperation) {
      return filterStationCycles(cycles.valuesToLazySeq(), {
        zoom: { start: addDays(today, -4), end: addDays(today, 1) },
        pallet: selectedPallet,
        operation: selectedOperation,
      });
    } else if (ty === "labor" && showGraph) {
      return loadOccupancyCycles(cycles.valuesToLazySeq(), {
        zoom: { start: addDays(today, -4), end: addDays(today, 1) },
        partAndProc: selectedPart,
        pallet: selectedPallet,
      });
    } else {
      return filterStationCycles(cycles.valuesToLazySeq(), {
        zoom: { start: addDays(today, -4), end: addDays(today, 1) },
        partAndProc: selectedPart,
        pallet: selectedPallet,
        station: ty === "labor" ? FilterAnyLoadKey : FilterAnyMachineKey,
      });
    }
  }, [cycles, ty, selectedPart, selectedPallet, selectedOperation, showGraph]);
  const curOperation = selectedPart ? selectedOperation ?? points.allMachineOperations[0] : undefined;
  const plannedMinutes = React.useMemo(() => {
    if (curOperation) {
      return plannedOperationMinutes(points, false);
    } else {
      return undefined;
    }
  }, [points, curOperation]);

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
          Recent {ty === "labor" ? "Load/Unload Occupancy" : "Machine Cycles"}
        </Typography>
        <Box flexGrow={1} />
        <FormControl size="small">
          <Select
            name="Station-Cycles-chart-or-table-select"
            autoWidth
            value={showGraph ? "graph" : "table"}
            onChange={(e) => setShowGraph(e.target.value === "graph")}
          >
            <MenuItem key="graph" value="graph">
              Graph
            </MenuItem>
            <MenuItem key="table" value="table">
              Table
            </MenuItem>
          </Select>
        </FormControl>
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={
              selectedPart
                ? points.allPartAndProcNames.findIndex(
                    (o) => selectedPart.part === o.part && selectedPart.proc === o.proc
                  )
                : -1
            }
            style={{ marginLeft: "1em" }}
            onChange={(e) => {
              setSelectedPart(
                e.target.value === -1 ? undefined : points.allPartAndProcNames[e.target.value as number]
              );
              setSelectedOperation(undefined);
            }}
          >
            <MenuItem key={0} value={-1}>
              <em>Any Part</em>
            </MenuItem>
            {points.allPartAndProcNames.map((n, idx) => (
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
        {ty === "machine" ? (
          <FormControl size="small">
            <Select
              name="Station-Cycles-cycle-chart-station-select"
              autoWidth
              displayEmpty
              value={
                curOperation
                  ? points.allMachineOperations.findIndex((o) => curOperation.compare(o) === 0)
                  : -1
              }
              style={{ marginLeft: "1em" }}
              onChange={(e) => setSelectedOperation(points.allMachineOperations[e.target.value as number])}
            >
              {points.allMachineOperations.length === 0 ? (
                <MenuItem value={-1}>
                  <em>Any Operation</em>
                </MenuItem>
              ) : (
                points.allMachineOperations.map((oper, idx) => (
                  <MenuItem key={idx} value={idx}>
                    {oper.statGroup} {oper.operation}
                  </MenuItem>
                ))
              )}
            </Select>
          </FormControl>
        ) : undefined}
        <FormControl size="small">
          <Select
            name="Station-Cycles-cycle-chart-station-pallet"
            autoWidth
            displayEmpty
            value={selectedPallet || ""}
            style={{ marginLeft: "1em" }}
            onChange={(e) => setSelectedPallet(e.target.value === "" ? undefined : e.target.value)}
          >
            <MenuItem key={0} value="">
              <em>Any Pallet</em>
            </MenuItem>
            {points.allPalletNames.map((n) => (
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
            onClick={() => copyCyclesToClipboard(points, matSummary.matsById, undefined, ty === "labor")}
            style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
            size="large"
          >
            <ImportExport />
          </IconButton>
        </Tooltip>
      </Box>
      <main>
        {showGraph ? (
          <CycleChart
            points={points.data}
            series_label={points.seriesLabel}
            default_date_range={default_date_range}
            extra_tooltip={extraStationCycleTooltip}
            current_date_zoom={chartZoom.zoom}
            set_date_zoom_range={setChartZoom}
            stats={curOperation ? estimatedCycleTimes.get(curOperation) : undefined}
            partCntPerPoint={
              curOperation ? LazySeq.of(points.data).head()?.[1]?.[0]?.material?.length : undefined
            }
            plannedTimeMinutes={plannedMinutes}
          />
        ) : (
          <StationDataTable
            points={points.data}
            matsById={matSummary.matsById}
            period={{ type: "Last30" }}
            current_date_zoom={undefined}
            set_date_zoom_range={undefined}
            showWorkorderAndInspect={true}
            hideMedian={ty === "labor"}
          />
        )}
      </main>
    </Box>
  );
}
