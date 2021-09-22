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
import * as React from "react";
import WorkIcon from "@material-ui/icons/Work";
import BasketIcon from "@material-ui/icons/ShoppingBasket";
import { addMonths, addDays, startOfToday, startOfDay } from "date-fns";
import ExtensionIcon from "@material-ui/icons/Extension";
import HourglassIcon from "@material-ui/icons/HourglassFull";
import { HashMap, Vector } from "prelude-ts";
import { Card } from "@material-ui/core";
import { CardHeader } from "@material-ui/core";
import { Select } from "@material-ui/core";
import { MenuItem } from "@material-ui/core";
import { CardContent } from "@material-ui/core";
import { Tooltip } from "@material-ui/core";
import { IconButton } from "@material-ui/core";
import ImportExport from "@material-ui/icons/ImportExport";
import AccountIcon from "@material-ui/icons/AccountBox";
import DonutIcon from "@material-ui/icons/DonutSmall";
import { Slider } from "@material-ui/core";

import AnalysisSelectToolbar from "./AnalysisSelectToolbar";
import { selectedAnalysisPeriod } from "../../network/load-specific-month";
import { CycleChart, CycleChartPoint, ExtraTooltip } from "./CycleChart";
import { SelectableHeatChart } from "./HeatChart";
import { useSelector } from "../../store/store";
import * as matDetails from "../../cell-status/material-details";
import { InspectionSankey } from "./InspectionSankey";
import {
  PartCycleData,
  CycleData,
  CycleState,
  PartAndProcess,
  PartAndStationOperation,
} from "../../data/events.cycles";
import {
  filterStationCycles,
  FilterAnyMachineKey,
  copyCyclesToClipboard,
  estimateLulOperations,
  plannedOperationSeries,
  LoadCycleData,
  loadOccupancyCycles,
  FilterAnyLoadKey,
  copyPalletCyclesToClipboard,
} from "../../data/results.cycles";
import { PartIdenticon } from "../station-monitor/Material";
import { LazySeq } from "../../util/lazyseq";
import StationDataTable from "./StationDataTable";
import {
  binSimStationUseByDayAndStat,
  copyOeeHeatmapToClipboard,
  binActiveCyclesByDayAndStat,
  DayAndStation,
  binOccupiedCyclesByDayAndStat,
} from "../../data/results.oee";
import {
  binCyclesByDayAndPart,
  binSimProductionByDayAndPart,
  copyCompletedPartsHeatmapToClipboard,
} from "../../data/results.completed-parts";
import { DataTableActionZoomType } from "./DataTable";
import { BufferChart } from "./BufferChart";
import { useIsDemo } from "../routes";
import { useRecoilValue, useSetRecoilState } from "recoil";
import { last30SimStationUse, specificMonthSimStationUse } from "../../cell-status/sim-station-use";
import { last30SimProduction, SimPartCompleted, specificMonthSimProduction } from "../../cell-status/sim-production";
import { last30Inspections, specificMonthInspections } from "../../cell-status/inspections";
import {
  last30MaterialSummary,
  specificMonthMaterialSummary,
  MaterialSummaryAndCompletedData,
} from "../../cell-status/material-summary";

// --------------------------------------------------------------------------------
// Machine Cycles
// --------------------------------------------------------------------------------

function PartMachineCycleChart() {
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);
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
  const period = useRecoilValue(selectedAnalysisPeriod);
  const allParts = useSelector((st) =>
    period.type === "Last30"
      ? st.Events.last30.cycles.part_and_proc_names
      : st.Events.selected_month.cycles.part_and_proc_names
  );
  const machineGroups = useSelector((st) =>
    period.type === "Last30" ? st.Events.last30.cycles.machine_groups : st.Events.selected_month.cycles.machine_groups
  );
  const machineNames = useSelector((st) =>
    period.type === "Last30" ? st.Events.last30.cycles.machine_names : st.Events.selected_month.cycles.machine_names
  );
  const palletNames = useSelector((st) =>
    period.type === "Last30" ? st.Events.last30.cycles.pallet_names : st.Events.selected_month.cycles.pallet_names
  );
  const estimatedCycleTimes = useSelector((st) =>
    period.type === "Last30"
      ? st.Events.last30.cycles.estimatedCycleTimes
      : st.Events.selected_month.cycles.estimatedCycleTimes
  );
  const matSummary = useRecoilValue(period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary);

  // filter/display state
  const demo = useIsDemo();
  const [showGraph, setShowGraph] = React.useState(true);
  const [selectedPart, setSelectedPart] = React.useState<PartAndProcess | undefined>(
    demo ? new PartAndProcess("aaa", 2) : undefined
  );
  const [selectedMachine, setSelectedMachine] = React.useState<string>(FilterAnyMachineKey);
  const [selectedOperation, setSelectedOperation] = React.useState<number>();
  const [selectedPallet, setSelectedPallet] = React.useState<string>();
  const [zoomDateRange, setZoomRange] = React.useState<{ start: Date; end: Date }>();

  const operationNames = React.useMemo(
    () =>
      selectedPart
        ? LazySeq.ofIterable(estimatedCycleTimes)
            .filter(
              ([k]) =>
                selectedPart.part === k.part && selectedPart.proc === k.proc && machineGroups.contains(k.statGroup)
            )
            .map(([k]) => k)
            .toVector()
            .sortOn(
              (k) => k.statGroup,
              (k) => k.operation
            )
        : Vector.empty<PartAndStationOperation>(),
    [selectedPart, estimatedCycleTimes, machineGroups]
  );
  const curOperation = selectedPart ? operationNames.get(selectedOperation ?? 0).getOrNull() : null;

  // calculate points
  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];
  const cycles = useSelector((s) =>
    period.type === "Last30" ? s.Events.last30.cycles.part_cycles : s.Events.selected_month.cycles.part_cycles
  );
  const points = React.useMemo(() => {
    if (selectedPart) {
      if (curOperation) {
        return filterStationCycles(cycles, { operation: curOperation, pallet: selectedPallet });
      } else {
        return filterStationCycles(cycles, {
          partAndProc: selectedPart,
          pallet: selectedPallet,
          station: FilterAnyMachineKey,
        });
      }
    } else {
      if (selectedPallet || selectedMachine !== FilterAnyMachineKey) {
        return filterStationCycles(cycles, { pallet: selectedPallet, station: selectedMachine });
      } else {
        return { seriesLabel: "Station", data: HashMap.empty<string, ReadonlyArray<PartCycleData>>() };
      }
    }
  }, [selectedPart, selectedPallet, selectedMachine, curOperation, cycles]);
  const plannedSeries = React.useMemo(() => {
    if (curOperation !== null) {
      return plannedOperationSeries(points, false);
    } else {
      return undefined;
    }
  }, [points, curOperation]);

  if (demo && selectedPart !== undefined && !allParts.isEmpty()) {
    // Select below compares object equality, but it takes time to load the demo data
    const fromLst = allParts.findAny((p) => p.part === "aaa" && p.proc == 2).getOrUndefined();
    if (fromLst !== selectedPart) {
      setSelectedPart(fromLst);
    }
  }

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <WorkIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Machine Cycles</div>
            <div style={{ flexGrow: 1 }} />
            {points.data.length() > 0 ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  onClick={() => copyCyclesToClipboard(points, matSummary.matsById, zoomDateRange)}
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : undefined}
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
            <Select
              name="Station-Cycles-cycle-chart-select"
              autoWidth
              displayEmpty
              value={selectedPart || ""}
              style={{ marginLeft: "1em" }}
              onChange={(e) => {
                setSelectedPart(e.target.value === "" ? undefined : (e.target.value as PartAndProcess));
                setSelectedOperation(undefined);
              }}
            >
              <MenuItem key={0} value="">
                <em>Any Part</em>
              </MenuItem>
              {allParts.toArray({ sortOn: [(x) => x.part, (x) => x.proc] }).map((n, idx) => (
                <MenuItem key={idx} value={n as any}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <PartIdenticon part={n.part} size={20} />
                    <span style={{ marginRight: "1em" }}>
                      {n.part}-{n.proc}
                    </span>
                  </div>
                </MenuItem>
              ))}
            </Select>
            <Select
              name="Station-Cycles-cycle-chart-station-select"
              autoWidth
              displayEmpty
              value={selectedPart ? selectedOperation ?? 0 : selectedMachine}
              style={{ marginLeft: "1em" }}
              onChange={(e) => {
                if (selectedPart) {
                  setSelectedOperation(e.target.value as number);
                } else {
                  setSelectedMachine(e.target.value as string);
                }
              }}
            >
              {selectedPart ? (
                operationNames.length() === 0 ? (
                  <MenuItem value={0}>
                    <em>Any Operation</em>
                  </MenuItem>
                ) : (
                  LazySeq.ofIterable(operationNames).map((oper, idx) => (
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
                  machineNames.toArray({ sortOn: (x) => x }).map((n) => (
                    <MenuItem key={n} value={n}>
                      <div style={{ display: "flex", alignItems: "center" }}>
                        <span style={{ marginRight: "1em" }}>{n}</span>
                      </div>
                    </MenuItem>
                  )),
                ]
              )}
            </Select>
            <Select
              name="Station-Cycles-cycle-chart-station-pallet"
              autoWidth
              displayEmpty
              value={selectedPallet || ""}
              style={{ marginLeft: "1em" }}
              onChange={(e) => setSelectedPallet(e.target.value === "" ? undefined : (e.target.value as string))}
            >
              <MenuItem key={0} value="">
                <em>Any Pallet</em>
              </MenuItem>
              {palletNames.toArray({ sortOn: (x) => x }).map((n) => (
                <MenuItem key={n} value={n}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <span style={{ marginRight: "1em" }}>{n}</span>
                  </div>
                </MenuItem>
              ))}
            </Select>
          </div>
        }
      />
      <CardContent>
        {showGraph ? (
          <CycleChart
            points={points.data}
            series_label={points.seriesLabel}
            default_date_range={defaultDateRange}
            extra_tooltip={extraStationCycleTooltip}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            stats={curOperation ? estimatedCycleTimes.get(curOperation).getOrUndefined() : undefined}
            partCntPerPoint={
              curOperation
                ? points.data
                    .findAny(() => true)
                    .map(([, cs]) => cs[0]?.material.length)
                    .getOrUndefined()
                : undefined
            }
            plannedSeries={plannedSeries}
          />
        ) : (
          <StationDataTable
            points={points.data}
            matsById={matSummary.matsById}
            default_date_range={defaultDateRange}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            last30_days={period.type === "Last30"}
            showWorkorderAndInspect={true}
          />
        )}
      </CardContent>
    </Card>
  );
}

// --------------------------------------------------------------------------------
// Load Cycles
// --------------------------------------------------------------------------------

type LoadCycleFilter = "LULOccupancy" | "LoadOp" | "UnloadOp";

function PartLoadStationCycleChart() {
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);
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

  const period = useRecoilValue(selectedAnalysisPeriod);
  const allParts = useSelector((st) =>
    period.type === "Last30"
      ? st.Events.last30.cycles.part_and_proc_names
      : st.Events.selected_month.cycles.part_and_proc_names
  );
  const loadStationNames = useSelector((st) =>
    period.type === "Last30"
      ? st.Events.last30.cycles.loadstation_names
      : st.Events.selected_month.cycles.loadstation_names
  );
  const palletNames = useSelector((st) =>
    period.type === "Last30" ? st.Events.last30.cycles.pallet_names : st.Events.selected_month.cycles.pallet_names
  );

  const demo = useIsDemo();
  const [showGraph, setShowGraph] = React.useState(true);
  const [selectedPart, setSelectedPart] = React.useState<PartAndProcess | undefined>(
    demo ? new PartAndProcess("aaa", 2) : undefined
  );
  const [selectedOperation, setSelectedOperation] = React.useState<LoadCycleFilter>(demo ? "LoadOp" : "LULOccupancy");
  const [selectedLoadStation, setSelectedLoadStation] = React.useState<string>(FilterAnyLoadKey);
  const [selectedPallet, setSelectedPallet] = React.useState<string>();
  const [zoomDateRange, setZoomRange] = React.useState<{ start: Date; end: Date }>();
  const curOperation =
    selectedPart && selectedOperation === "LoadOp"
      ? new PartAndStationOperation(selectedPart.part, selectedPart.proc, "L/U", "LOAD")
      : selectedPart && selectedOperation === "UnloadOp"
      ? new PartAndStationOperation(selectedPart.part, selectedPart.proc, "L/U", "UNLOAD")
      : null;

  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];
  const cycles = useSelector((s) =>
    period.type === "Last30" ? s.Events.last30.cycles.part_cycles : s.Events.selected_month.cycles.part_cycles
  );
  const matSummary = useRecoilValue(period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary);
  const estimatedCycleTimes = useSelector((st) =>
    period.type === "Last30"
      ? st.Events.last30.cycles.estimatedCycleTimes
      : st.Events.selected_month.cycles.estimatedCycleTimes
  );
  const points = React.useMemo(() => {
    if (selectedPart || selectedPallet || selectedLoadStation !== FilterAnyLoadKey) {
      if (curOperation) {
        return estimateLulOperations(cycles, {
          operation: curOperation,
          pallet: selectedPallet,
          station: selectedLoadStation,
        });
      } else if (showGraph) {
        return loadOccupancyCycles(cycles, {
          partAndProc: selectedPart,
          pallet: selectedPallet,
          station: selectedLoadStation,
        });
      } else {
        return filterStationCycles(cycles, {
          partAndProc: selectedPart,
          pallet: selectedPallet,
          station: selectedLoadStation,
        });
      }
    } else {
      return { seriesLabel: "Station", data: HashMap.empty<string, ReadonlyArray<LoadCycleData>>() };
    }
  }, [selectedPart, selectedPallet, selectedOperation, selectedLoadStation, cycles, showGraph]);
  const plannedSeries = React.useMemo(() => {
    if (selectedOperation === "LoadOp" || selectedOperation === "UnloadOp") {
      return plannedOperationSeries(points, true);
    } else {
      return undefined;
    }
  }, [points, selectedOperation]);

  if (demo && selectedPart !== undefined && !allParts.isEmpty()) {
    // Select below compares object equality, but it takes time to load the demo data
    const fromLst = allParts.findAny((p) => p.part === "aaa" && p.proc == 2).getOrUndefined();
    if (fromLst !== selectedPart) {
      setSelectedPart(fromLst);
    }
  }

  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <AccountIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Load/Unload Cycles</div>
            <div style={{ flexGrow: 1 }} />
            {points.data.length() > 0 ? (
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
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : undefined}
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
            <Select
              name="Station-Cycles-cycle-chart-select"
              autoWidth
              displayEmpty
              value={selectedPart || ""}
              style={{ marginLeft: "1em" }}
              onChange={(e) => {
                if (e.target.value === "") {
                  setSelectedPart(undefined);
                  setSelectedOperation("LULOccupancy");
                } else {
                  setSelectedPart(e.target.value as PartAndProcess);
                }
              }}
            >
              <MenuItem key={0} value="">
                <em>Any Part</em>
              </MenuItem>
              {allParts.toArray({ sortOn: [(x) => x.part, (x) => x.proc] }).map((n, idx) => (
                <MenuItem key={idx} value={n as any}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <PartIdenticon part={n.part} size={20} />
                    <span style={{ marginRight: "1em" }}>
                      {n.part}-{n.proc}
                    </span>
                  </div>
                </MenuItem>
              ))}
            </Select>
            <Select
              name="Station-Cycles-cycle-chart-station-select"
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
            <Select
              name="Station-Cycles-cycle-chart-station-select"
              autoWidth
              displayEmpty
              value={selectedLoadStation}
              style={{ marginLeft: "1em" }}
              onChange={(e) => {
                setSelectedLoadStation(e.target.value as string);
              }}
            >
              <MenuItem key={-1} value={FilterAnyLoadKey}>
                <em>Any Station</em>
              </MenuItem>
              {loadStationNames.toArray({ sortOn: (x) => x }).map((n) => (
                <MenuItem key={n} value={n}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <span style={{ marginRight: "1em" }}>{n}</span>
                  </div>
                </MenuItem>
              ))}
            </Select>
            <Select
              name="Station-Cycles-cycle-chart-station-pallet"
              autoWidth
              displayEmpty
              value={selectedPallet || ""}
              style={{ marginLeft: "1em" }}
              onChange={(e) => setSelectedPallet(e.target.value === "" ? undefined : (e.target.value as string))}
            >
              <MenuItem key={0} value="">
                <em>Any Pallet</em>
              </MenuItem>
              {palletNames.toArray({ sortOn: (x) => x }).map((n) => (
                <MenuItem key={n} value={n}>
                  <div style={{ display: "flex", alignItems: "center" }}>
                    <span style={{ marginRight: "1em" }}>{n}</span>
                  </div>
                </MenuItem>
              ))}
            </Select>
          </div>
        }
      />
      <CardContent>
        {showGraph ? (
          <CycleChart
            points={points.data}
            series_label={points.seriesLabel}
            default_date_range={defaultDateRange}
            extra_tooltip={extraLoadCycleTooltip}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            stats={curOperation ? estimatedCycleTimes.get(curOperation).getOrUndefined() : undefined}
            plannedSeries={plannedSeries}
          />
        ) : (
          <StationDataTable
            points={points.data}
            matsById={matSummary.matsById}
            default_date_range={defaultDateRange}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            last30_days={period.type === "Last30"}
            showWorkorderAndInspect={true}
            hideMedian={selectedOperation === "LULOccupancy"}
          />
        )}
      </CardContent>
    </Card>
  );
}

// --------------------------------------------------------------------------------
// Pallet Cycles
// --------------------------------------------------------------------------------

function PalletCycleChart() {
  const demo = useIsDemo();
  const [selectedPallet, setSelectedPallet] = React.useState<string | undefined>(demo ? "3" : undefined);
  const [zoomDateRange, setZoomRange] = React.useState<{ start: Date; end: Date }>();

  const period = useRecoilValue(selectedAnalysisPeriod);
  const defaultDateRange =
    period.type === "Last30"
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [period.month, addMonths(period.month, 1)];

  const palletCycles = useSelector((st) =>
    period.type === "Last30" ? st.Events.last30.cycles.by_pallet : st.Events.selected_month.cycles.by_pallet
  );
  let points = HashMap.empty<string, ReadonlyArray<CycleData>>();
  if (selectedPallet) {
    const palData = palletCycles.get(selectedPallet);
    if (palData.isSome()) {
      points = HashMap.of([selectedPallet, palData.get()]);
    }
  }
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <BasketIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Pallet Cycles</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                onClick={() => copyPalletCyclesToClipboard(palletCycles)}
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
            <Select
              name={"Pallet-Cycles-cycle-chart-select"}
              autoWidth
              displayEmpty
              value={selectedPallet || ""}
              onChange={(e) => setSelectedPallet(e.target.value as string)}
            >
              {selectedPallet !== undefined ? undefined : (
                <MenuItem key={0} value="">
                  <em>Select Pallet</em>
                </MenuItem>
              )}
              {palletCycles
                .keySet()
                .toArray({ sortOn: (x) => x })
                .map((n) => (
                  <MenuItem key={n} value={n}>
                    <div style={{ display: "flex", alignItems: "center" }}>
                      <span style={{ marginRight: "1em" }}>{n}</span>
                    </div>
                  </MenuItem>
                ))}
            </Select>
          </div>
        }
      />
      <CardContent>
        <CycleChart
          points={points}
          series_label="Pallet"
          default_date_range={defaultDateRange}
          current_date_zoom={zoomDateRange}
          set_date_zoom_range={(z) => setZoomRange(z.zoom)}
        />
      </CardContent>
    </Card>
  );
}

// --------------------------------------------------------------------------------
// Buffer Chart
// --------------------------------------------------------------------------------

// https://github.com/mui-org/material-ui/issues/20191
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const SliderAny: React.ComponentType<any> = Slider;

function BufferOccupancyChart() {
  const [movingAverageHours, setMovingAverage] = React.useState(12);
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <DonutIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Buffer Occupancy</div>
            <div style={{ flexGrow: 1 }} />
            <span style={{ fontSize: "small", marginRight: "1em" }}>Moving Average Window: </span>
            <SliderAny
              style={{ width: "10em" }}
              min={1}
              max={36}
              steps={0.2}
              valueLabelDisplay="off"
              value={movingAverageHours}
              onChange={(e: React.ChangeEvent<unknown>, v: number) => setMovingAverage(v)}
            />
          </div>
        }
      />
      <CardContent>
        <BufferChart movingAverageDistanceInHours={movingAverageHours} />
      </CardContent>
    </Card>
  );
}

// --------------------------------------------------------------------------------
// Oee Heatmap
// --------------------------------------------------------------------------------

type StationOeeHeatmapTypes = "Standard OEE" | "Planned OEE" | "Occupied";

function dayAndStatToHeatmapPoints(pts: HashMap<DayAndStation, number>) {
  return LazySeq.ofIterable(pts)
    .map(([dayAndStat, val]) => {
      const pct = val / (24 * 60);
      return {
        x: dayAndStat.day,
        y: dayAndStat.station,
        color: Math.min(pct, 1),
        label: (pct * 100).toFixed(1) + "%",
      };
    })
    .toArray()
    .sort((p1, p2) => {
      const cmp = p1.x.getTime() - p2.x.getTime();
      if (cmp === 0) {
        return p2.y.localeCompare(p1.y); // descending, compare p2 to p1
      } else {
        return cmp;
      }
    });
}

function StationOeeHeatmap() {
  const [selected, setSelected] = React.useState<StationOeeHeatmapTypes>("Standard OEE");
  const period = useRecoilValue(selectedAnalysisPeriod);
  const data = useSelector((s) => (period.type === "Last30" ? s.Events.last30 : s.Events.selected_month));
  const statUse = useRecoilValue(period.type === "Last30" ? last30SimStationUse : specificMonthSimStationUse);
  const points = React.useMemo(() => {
    if (selected === "Standard OEE") {
      return dayAndStatToHeatmapPoints(binActiveCyclesByDayAndStat(data.cycles.part_cycles));
    } else if (selected === "Occupied") {
      return dayAndStatToHeatmapPoints(binOccupiedCyclesByDayAndStat(data.cycles.part_cycles));
    } else {
      return dayAndStatToHeatmapPoints(binSimStationUseByDayAndStat(statUse));
    }
  }, [selected, data, statUse]);

  return (
    <SelectableHeatChart<StationOeeHeatmapTypes>
      card_label="Station Use"
      y_title="Station"
      label_title={selected === "Occupied" ? "Occupied" : "OEE"}
      icon={<HourglassIcon style={{ color: "#6D4C41" }} />}
      cur_selected={selected}
      options={["Standard OEE", "Occupied", "Planned OEE"]}
      setSelected={setSelected}
      points={points}
      onExport={() => copyOeeHeatmapToClipboard("Station", points)}
    />
  );
}

// --------------------------------------------------------------------------------
// Completed Heatmap
// --------------------------------------------------------------------------------

type CompletedPartsHeatmapTypes = "Planned" | "Completed";

function partsCompletedPoints(
  cycles: CycleState,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  start: Date,
  end: Date
) {
  const pts = binCyclesByDayAndPart(cycles.part_cycles, matsById, start, end);
  return LazySeq.ofIterable(pts)
    .map(([dayAndPart, val]) => {
      return {
        x: dayAndPart.day,
        y: dayAndPart.part,
        color: val.activeMachineMins,
        label: val.count.toFixed(0) + " (" + (val.activeMachineMins / 60).toFixed(1) + " hours)",
        count: val.count,
        activeMachineMins: val.activeMachineMins,
      };
    })
    .toArray()
    .sort((p1, p2) => {
      const cmp = p1.x.getTime() - p2.x.getTime();
      if (cmp === 0) {
        return p2.y.localeCompare(p1.y); // descending, compare p2 to p1
      } else {
        return cmp;
      }
    });
}

function partsPlannedPoints(prod: Iterable<SimPartCompleted>) {
  const pts = binSimProductionByDayAndPart(prod);
  return LazySeq.ofIterable(pts)
    .map(([dayAndPart, val]) => {
      return {
        x: dayAndPart.day,
        y: dayAndPart.part,
        color: val.activeMachineMins,
        label: val.count.toFixed(0) + " (" + (val.activeMachineMins / 60).toFixed(1) + " hours)",
        count: val.count,
        activeMachineMins: val.activeMachineMins,
      };
    })
    .toArray()
    .sort((p1, p2) => {
      const cmp = p1.x.getTime() - p2.x.getTime();
      if (cmp === 0) {
        return p2.y.localeCompare(p1.y); // descending, compare p2 to p1
      } else {
        return cmp;
      }
    });
}

function CompletedCountHeatmap() {
  const [selected, setSelected] = React.useState<CompletedPartsHeatmapTypes>("Completed");
  const period = useRecoilValue(selectedAnalysisPeriod);
  const data = useSelector((s) =>
    period.type === "Last30"
      ? {
          evts: s.Events.last30,
          start: startOfDay(s.Events.last30.thirty_days_ago),
          end: addDays(startOfDay(s.Events.last30.thirty_days_ago), 31),
        }
      : {
          evts: s.Events.selected_month,
          start: period.month,
          end: addMonths(period.month, 1),
        }
  );
  const productionCounts = useRecoilValue(period.type === "Last30" ? last30SimProduction : specificMonthSimProduction);
  const matSummary = useRecoilValue(period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary);
  const points = React.useMemo(() => {
    if (selected === "Completed") {
      return partsCompletedPoints(data.evts.cycles, matSummary.matsById, data.start, data.end);
    } else {
      return partsPlannedPoints(productionCounts);
    }
  }, [selected, data.evts, data.start, data.end, matSummary, productionCounts]);
  return (
    <SelectableHeatChart
      card_label="Part Production"
      y_title="Part"
      label_title={selected}
      icon={<ExtensionIcon style={{ color: "#6D4C41" }} />}
      cur_selected={selected}
      options={["Completed", "Planned"]}
      setSelected={setSelected}
      points={points}
      onExport={() => copyCompletedPartsHeatmapToClipboard(points)}
    />
  );
}

// --------------------------------------------------------------------------------
// Inspection
// --------------------------------------------------------------------------------

function ConnectedInspection() {
  const period = useRecoilValue(selectedAnalysisPeriod);

  const inspectionlogs = useRecoilValue(period.type === "Last30" ? last30Inspections : specificMonthInspections);
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

export function EfficiencyCards(): JSX.Element {
  return (
    <>
      <div data-testid="part-cycle-chart">
        <PartMachineCycleChart />
      </div>
      <div data-testid="part-load-cycle-chart" style={{ marginTop: "3em" }}>
        <PartLoadStationCycleChart />
      </div>
      <div data-testid="pallet-cycle-chart" style={{ marginTop: "3em" }}>
        <PalletCycleChart />
      </div>
      <div data-testid="buffer-chart" style={{ marginTop: "3em" }}>
        <BufferOccupancyChart />
      </div>
      <div data-testid="station-oee-heatmap" style={{ marginTop: "3em" }}>
        <StationOeeHeatmap />
      </div>
      <div data-testid="completed-heatmap" style={{ marginTop: "3em" }}>
        <CompletedCountHeatmap />
      </div>
      <div data-testid="inspection-sankey" style={{ marginTop: "3em" }}>
        <ConnectedInspection />
      </div>
    </>
  );
}

export default function Efficiency(): JSX.Element {
  React.useEffect(() => {
    document.title = "Efficiency - FMS Insight";
  }, []);
  return (
    <>
      <AnalysisSelectToolbar />
      <main style={{ padding: "24px" }}>
        <EfficiencyCards />
      </main>
    </>
  );
}
