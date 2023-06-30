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
import { Box, FormControl, Typography } from "@mui/material";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { ImportExport } from "@mui/icons-material";

import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { CycleChart, CycleChartPoint, ExtraTooltip, YZoomRange } from "./CycleChart.js";
import * as matDetails from "../../cell-status/material-details.js";
import {
  filterStationCycles,
  FilterAnyMachineKey,
  copyCyclesToClipboard,
  estimateLulOperations,
  plannedOperationMinutes,
  LoadCycleData,
  loadOccupancyCycles,
  FilterAnyLoadKey,
  emptyStationCycles,
  PartAndProcess,
} from "../../data/results.cycles.js";
import { PartIdenticon } from "../station-monitor/Material.js";
import StationDataTable from "./StationDataTable.js";
import { useSetTitle, isDemoAtom } from "../routes.js";
import { last30MaterialSummary, specificMonthMaterialSummary } from "../../cell-status/material-summary.js";
import {
  last30EstimatedCycleTimes,
  PartAndStationOperation,
  specificMonthEstimatedCycleTimes,
} from "../../cell-status/estimated-cycle-times.js";
import {
  last30StationCycles,
  PartCycleData,
  specificMonthStationCycles,
} from "../../cell-status/station-cycles.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";
import { atomWithDefault } from "jotai/utils";

// --------------------------------------------------------------------------------
// Machine Cycles
// --------------------------------------------------------------------------------

const machineShowGraph = atom<boolean>(true);
const machineSelectedPart = atomWithDefault<PartAndProcess | undefined>((get) =>
  get(isDemoAtom) ? { part: "aaa", proc: 2 } : undefined
);
const machineSelectedMachine = atom<string>(FilterAnyMachineKey);
const machineSelectedOperation = atom<PartAndStationOperation | undefined>(undefined);
const machineSelectedPallet = atom<number | undefined>(undefined);
const machineZoomDateRange = atom<{ start: Date; end: Date } | undefined>(undefined);
const machineYZoom = atom<YZoomRange | null>(null);

export function PartMachineCycleChart() {
  useSetTitle("Machine Cycles");
  const setMatToShow = useSetAtom(matDetails.materialDialogOpen);
  const extraStationCycleTooltip = React.useCallback(
    function extraStationCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
      const partC = point as PartCycleData;
      const ret = [];
      for (const mat of partC.material) {
        ret.push({
          title: mat.serial ? mat.serial : "Material",
          value: "Open Card",
          link: () => setMatToShow({ type: "LogMat", logMat: mat }),
        });
      }
      return ret;
    },
    [setMatToShow]
  );

  // values which user can select to be filtered on
  const period = useAtomValue(selectedAnalysisPeriod);
  const estimatedCycleTimes = useAtomValue(
    period.type === "Last30" ? last30EstimatedCycleTimes : specificMonthEstimatedCycleTimes
  );
  const matSummary = useAtomValue(
    period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary
  );

  // filter/display state
  const [showGraph, setShowGraph] = useAtom(machineShowGraph);
  const [selectedPart, setSelectedPart] = useAtom(machineSelectedPart);
  const [selectedMachine, setSelectedMachine] = useAtom(machineSelectedMachine);
  const [selectedOperation, setSelectedOperation] = useAtom(machineSelectedOperation);
  const [selectedPallet, setSelectedPallet] = useAtom(machineSelectedPallet);
  const [zoomDateRange, setZoomRange] = useAtom(machineZoomDateRange);
  const [yZoom, setYZoom] = useAtom(machineYZoom);

  // calculate points
  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];
  const cycles = useAtomValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const points = React.useMemo(() => {
    if (selectedPart) {
      if (selectedOperation) {
        return filterStationCycles(cycles.valuesToLazySeq(), {
          operation: selectedOperation,
          pallet: selectedPallet,
        });
      } else {
        return filterStationCycles(cycles.valuesToLazySeq(), {
          partAndProc: selectedPart,
          pallet: selectedPallet,
          station: FilterAnyMachineKey,
        });
      }
    } else {
      if (selectedPallet || selectedMachine !== FilterAnyMachineKey) {
        return filterStationCycles(cycles.valuesToLazySeq(), {
          pallet: selectedPallet,
          station: selectedMachine,
        });
      } else {
        return emptyStationCycles(cycles.valuesToLazySeq());
      }
    }
  }, [selectedPart, selectedPallet, selectedMachine, selectedOperation, cycles]);
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
        <Typography variant="subtitle1">Machine Cycles</Typography>
        <Box flexGrow={1} />
        <FormControl size="small">
          <Select
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
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={
              selectedPart
                ? curOperation
                  ? points.allMachineOperations.findIndex((o) => curOperation.compare(o) === 0)
                  : -1
                : selectedMachine
            }
            style={{ marginLeft: "1em" }}
            onChange={(e) => {
              if (selectedPart) {
                setSelectedOperation(points.allMachineOperations[e.target.value as number]);
              } else {
                setSelectedMachine(e.target.value as string);
              }
            }}
          >
            {selectedPart ? (
              points.allMachineOperations.length === 0 ? (
                <MenuItem value={-1}>
                  <em>Any Operation</em>
                </MenuItem>
              ) : (
                points.allMachineOperations.map((oper, idx) => (
                  <MenuItem key={idx} value={idx}>
                    {oper.statGroup} {oper.operation}
                  </MenuItem>
                ))
              )
            ) : (
              [
                <MenuItem key={-1} value={FilterAnyMachineKey}>
                  <em>Any Machine</em>
                </MenuItem>,
                points.allMachineNames.map((n) => (
                  <MenuItem key={n} value={n}>
                    <div style={{ display: "flex", alignItems: "center" }}>
                      <span style={{ marginRight: "1em" }}>{n}</span>
                    </div>
                  </MenuItem>
                )),
              ]
            )}
          </Select>
        </FormControl>
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={selectedPallet || ""}
            style={{ marginLeft: "1em" }}
            onChange={(e) =>
              setSelectedPallet(e.target.value === "" ? undefined : (e.target.value as number))
            }
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
        {points.data.size > 0 ? (
          <Tooltip title="Copy to Clipboard">
            <IconButton
              onClick={() => copyCyclesToClipboard(points, matSummary.matsById, zoomDateRange)}
              style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
              size="large"
            >
              <ImportExport />
            </IconButton>
          </Tooltip>
        ) : undefined}
      </Box>
      <main>
        {showGraph ? (
          <CycleChart
            points={points.data}
            series_label={points.seriesLabel}
            default_date_range={defaultDateRange}
            extra_tooltip={extraStationCycleTooltip}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            yZoom={yZoom}
            setYZoom={setYZoom}
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
            period={period}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            showWorkorderAndInspect={true}
            emptyMessage="Select part, station, or pallet to see cycles."
          />
        )}
      </main>
    </Box>
  );
}

// --------------------------------------------------------------------------------
// Load Cycles
// --------------------------------------------------------------------------------

type LoadCycleFilter = "LULOccupancy" | "LoadOp" | "UnloadOp";

const loadShowGraph = atom<boolean>(true);
const loadSelectedPart = atomWithDefault<PartAndProcess | undefined>((get) =>
  get(isDemoAtom) ? { part: "aaa", proc: 2 } : undefined
);
const loadSelectedOperation = atomWithDefault<LoadCycleFilter>((get) =>
  get(isDemoAtom) ? "LoadOp" : "LULOccupancy"
);
const loadSelectedLoad = atom<string>(FilterAnyLoadKey);
const loadSelectedPallet = atom<number | undefined>(undefined);
const loadZoomDateRange = atom<{ start: Date; end: Date } | undefined>(undefined);
const loadYZoom = atom<YZoomRange | null>(null);

export function PartLoadStationCycleChart() {
  useSetTitle("L/U Cycles");
  const setMatToShow = useSetAtom(matDetails.materialDialogOpen);
  const extraLoadCycleTooltip = React.useCallback(
    function extraLoadCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
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
      }
      return ret;
    },
    [setMatToShow]
  );

  const period = useAtomValue(selectedAnalysisPeriod);

  const [showGraph, setShowGraph] = useAtom(loadShowGraph);
  const [selectedPart, setSelectedPart] = useAtom(loadSelectedPart);
  const [selectedOperation, setSelectedOperation] = useAtom(loadSelectedOperation);
  const [selectedLoadStation, setSelectedLoadStation] = useAtom(loadSelectedLoad);
  const [selectedPallet, setSelectedPallet] = useAtom(loadSelectedPallet);
  const [zoomDateRange, setZoomRange] = useAtom(loadZoomDateRange);
  const [yZoom, setYZoom] = useAtom(loadYZoom);
  const curOperation =
    selectedPart && selectedOperation === "LoadOp"
      ? new PartAndStationOperation(selectedPart.part, "L/U", "LOAD" + "-" + selectedPart.proc.toString())
      : selectedPart && selectedOperation === "UnloadOp"
      ? new PartAndStationOperation(selectedPart.part, "L/U", "UNLOAD" + "-" + selectedPart.proc.toString())
      : null;

  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];
  const cycles = useAtomValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const matSummary = useAtomValue(
    period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary
  );
  const estimatedCycleTimes = useAtomValue(
    period.type === "Last30" ? last30EstimatedCycleTimes : specificMonthEstimatedCycleTimes
  );
  const points = React.useMemo(() => {
    if (selectedPart || selectedPallet || selectedLoadStation !== FilterAnyLoadKey) {
      if (curOperation) {
        return estimateLulOperations(cycles.valuesToLazySeq(), {
          operation: curOperation,
          pallet: selectedPallet,
          station: selectedLoadStation,
        });
      } else if (showGraph) {
        return loadOccupancyCycles(cycles.valuesToLazySeq(), {
          partAndProc: selectedPart,
          pallet: selectedPallet,
          station: selectedLoadStation,
        });
      } else {
        return filterStationCycles(cycles.valuesToLazySeq(), {
          partAndProc: selectedPart,
          pallet: selectedPallet,
          station: selectedLoadStation,
        });
      }
    } else {
      return emptyStationCycles(cycles.valuesToLazySeq());
    }
  }, [selectedPart, selectedPallet, selectedOperation, selectedLoadStation, cycles, showGraph]);
  const plannedMinutes = React.useMemo(() => {
    if (selectedOperation === "LoadOp" || selectedOperation === "UnloadOp") {
      return plannedOperationMinutes(points, true);
    } else {
      return undefined;
    }
  }, [points, selectedOperation]);

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
        <Typography variant="subtitle1">Load/Unload Cycles</Typography>
        <Box flexGrow={1} />
        <FormControl size="small">
          <Select
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
              if (e.target.value === -1) {
                setSelectedPart(undefined);
                setSelectedOperation("LULOccupancy");
              } else {
                setSelectedPart(
                  e.target.value === -1 ? undefined : points.allPartAndProcNames[e.target.value as number]
                );
              }
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
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={selectedOperation}
            style={{ marginLeft: "1em" }}
            onChange={(e) => setSelectedOperation(e.target.value as LoadCycleFilter)}
          >
            <MenuItem value={"LULOccupancy"}>L/U Occupancy</MenuItem>
            {selectedPart ? <MenuItem value={"LoadOp"}>Load Operation (estimated)</MenuItem> : undefined}
            {selectedPart ? <MenuItem value={"UnloadOp"}>Unload Operation (estimated)</MenuItem> : undefined}
          </Select>
        </FormControl>
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={selectedLoadStation}
            style={{ marginLeft: "1em" }}
            onChange={(e) => {
              setSelectedLoadStation(e.target.value);
            }}
          >
            <MenuItem key={-1} value={FilterAnyLoadKey}>
              <em>Any Station</em>
            </MenuItem>
            {points.allLoadStationNames.map((n) => (
              <MenuItem key={n} value={n}>
                <div style={{ display: "flex", alignItems: "center" }}>
                  <span style={{ marginRight: "1em" }}>{n}</span>
                </div>
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={selectedPallet || ""}
            style={{ marginLeft: "1em" }}
            onChange={(e) =>
              setSelectedPallet(e.target.value === "" ? undefined : (e.target.value as number))
            }
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
        {points.data.size > 0 ? (
          <Tooltip title="Copy to Clipboard">
            <IconButton
              onClick={() =>
                copyCyclesToClipboard(
                  points,
                  matSummary.matsById,
                  zoomDateRange,
                  selectedOperation === "LULOccupancy"
                )
              }
              style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
              size="large"
            >
              <ImportExport />
            </IconButton>
          </Tooltip>
        ) : undefined}
      </Box>
      <main>
        {showGraph ? (
          <CycleChart
            points={points.data}
            series_label={points.seriesLabel}
            default_date_range={defaultDateRange}
            extra_tooltip={extraLoadCycleTooltip}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            yZoom={yZoom}
            setYZoom={setYZoom}
            stats={curOperation ? estimatedCycleTimes.get(curOperation) : undefined}
            plannedTimeMinutes={plannedMinutes}
          />
        ) : (
          <StationDataTable
            points={points.data}
            matsById={matSummary.matsById}
            period={period}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            showWorkorderAndInspect={true}
            hideMedian={selectedOperation === "LULOccupancy"}
            emptyMessage="Select part, operation, or pallet to see cycles."
          />
        )}
      </main>
    </Box>
  );
}
