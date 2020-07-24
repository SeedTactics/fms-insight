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
import { addMonths, addDays, startOfToday } from "date-fns";
import ExtensionIcon from "@material-ui/icons/Extension";
import HourglassIcon from "@material-ui/icons/HourglassFull";
import { HashMap, Vector } from "prelude-ts";
import Card from "@material-ui/core/Card";
import CardHeader from "@material-ui/core/CardHeader";
import Select from "@material-ui/core/Select";
import MenuItem from "@material-ui/core/MenuItem";
import CardContent from "@material-ui/core/CardContent";
import Tooltip from "@material-ui/core/Tooltip";
import IconButton from "@material-ui/core/IconButton";
import ImportExport from "@material-ui/icons/ImportExport";
import AccountIcon from "@material-ui/icons/AccountBox";
import DonutIcon from "@material-ui/icons/DonutSmall";
import Slider from "@material-ui/core/Slider";

import AnalysisSelectToolbar from "./AnalysisSelectToolbar";
import { CycleChart, CycleChartPoint, ExtraTooltip } from "./CycleChart";
import { SelectableHeatChart } from "./HeatChart";
import { connect, mkAC, DispatchAction, useSelector } from "../../store/store";
import * as guiState from "../../data/gui-state";
import * as matDetails from "../../data/material-details";
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
} from "../../data/results.cycles";
import { PartIdenticon } from "../station-monitor/Material";
import { LazySeq } from "../../data/lazyseq";
import StationDataTable from "./StationDataTable";
import { AnalysisPeriod } from "../../data/events";
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
import { SimUseState } from "../../data/events.simuse";
import { DataTableActionZoomType } from "./DataTable";
import { BufferChart } from "./BufferChart";

// --------------------------------------------------------------------------------
// Machine Cycles
// --------------------------------------------------------------------------------

interface PartStationCycleChartProps {
  readonly openMaterial: (matId: number) => void;
}

function PartMachineCycleChart(props: PartStationCycleChartProps) {
  const extraStationCycleTooltip = React.useCallback(
    function extraStationCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
      const partC = point as PartCycleData;
      const ret = [];
      for (const mat of partC.material) {
        ret.push({
          title: mat.serial ? mat.serial : "Material",
          value: "Open Card",
          link: () => props.openMaterial(mat.id),
        });
      }
      return ret;
    },
    [props.openMaterial]
  );

  // filter/display state
  const [showGraph, setShowGraph] = React.useState(true);
  const [selectedPart, setSelectedPart] = React.useState<PartAndProcess>();
  const [selectedMachine, setSelectedMachine] = React.useState<string>(FilterAnyMachineKey);
  const [selectedOperation, setSelectedOperation] = React.useState<number>();
  const [selectedPallet, setSelectedPallet] = React.useState<string>();
  const [zoomDateRange, setZoomRange] = React.useState<{ start: Date; end: Date }>();

  // values which user can select to be filtered on
  const allParts = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.part_and_proc_names
      : st.Events.selected_month.cycles.part_and_proc_names
  );
  const machineGroups = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.machine_groups
      : st.Events.selected_month.cycles.machine_groups
  );
  const machineNames = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.machine_names
      : st.Events.selected_month.cycles.machine_names
  );
  const palletNames = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.pallet_names
      : st.Events.selected_month.cycles.pallet_names
  );
  const estimatedCycleTimes = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.estimatedCycleTimes
      : st.Events.selected_month.cycles.estimatedCycleTimes
  );
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
  const analysisPeriod = useSelector((s) => s.Events.analysis_period);
  const analysisPeriodMonth = useSelector((s) => s.Events.analysis_period_month);
  const defaultDateRange =
    analysisPeriod === AnalysisPeriod.Last30Days
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [analysisPeriodMonth, addMonths(analysisPeriodMonth, 1)];
  const cycles = useSelector((s) =>
    s.Events.analysis_period === AnalysisPeriod.Last30Days
      ? s.Events.last30.cycles.part_cycles
      : s.Events.selected_month.cycles.part_cycles
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
                  onClick={() => copyCyclesToClipboard(points, zoomDateRange)}
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
            default_date_range={defaultDateRange}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            last30_days={analysisPeriod === AnalysisPeriod.Last30Days}
            openDetails={props.openMaterial}
            showWorkorderAndInspect={true}
          />
        )}
      </CardContent>
    </Card>
  );
}

const ConnectedPartMachineCycleChart = connect((st) => ({}), {
  openMaterial: matDetails.openMaterialById,
})(PartMachineCycleChart);

// --------------------------------------------------------------------------------
// Load Cycles
// --------------------------------------------------------------------------------

type LoadCycleFilter = "LULOccupancy" | "LoadOp" | "UnloadOp";

function PartLoadStationCycleChart(props: PartStationCycleChartProps) {
  const extraLoadCycleTooltip = React.useCallback(
    function extraLoadCycleTooltip(point: CycleChartPoint): ReadonlyArray<ExtraTooltip> {
      const partC = point as LoadCycleData;
      const ret = [];
      if (partC.operations) {
        for (const mat of partC.operations) {
          ret.push({
            title: (mat.serial ? mat.serial : "Material") + " " + mat.operation,
            value: "Open Card",
            link: () => props.openMaterial(mat.id),
          });
        }
      }
      return ret;
    },
    [props.openMaterial]
  );

  const allParts = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.part_and_proc_names
      : st.Events.selected_month.cycles.part_and_proc_names
  );
  const palletNames = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.pallet_names
      : st.Events.selected_month.cycles.pallet_names
  );

  const [showGraph, setShowGraph] = React.useState(true);
  const [selectedPart, setSelectedPart] = React.useState<PartAndProcess>();
  const [selectedOperation, setSelectedOperation] = React.useState<LoadCycleFilter>("LULOccupancy");
  const [selectedPallet, setSelectedPallet] = React.useState<string>();
  const [zoomDateRange, setZoomRange] = React.useState<{ start: Date; end: Date }>();
  const curOperation =
    selectedPart && selectedOperation === "LoadOp"
      ? new PartAndStationOperation(selectedPart.part, selectedPart.proc, "L/U", "LOAD")
      : selectedPart && selectedOperation === "UnloadOp"
      ? new PartAndStationOperation(selectedPart.part, selectedPart.proc, "L/U", "UNLOAD")
      : null;

  const analysisPeriod = useSelector((s) => s.Events.analysis_period);
  const analysisPeriodMonth = useSelector((s) => s.Events.analysis_period_month);
  const defaultDateRange =
    analysisPeriod === AnalysisPeriod.Last30Days
      ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
      : [analysisPeriodMonth, addMonths(analysisPeriodMonth, 1)];
  const cycles = useSelector((s) =>
    s.Events.analysis_period === AnalysisPeriod.Last30Days
      ? s.Events.last30.cycles.part_cycles
      : s.Events.selected_month.cycles.part_cycles
  );
  const estimatedCycleTimes = useSelector((st) =>
    st.Events.analysis_period === AnalysisPeriod.Last30Days
      ? st.Events.last30.cycles.estimatedCycleTimes
      : st.Events.selected_month.cycles.estimatedCycleTimes
  );
  const points = React.useMemo(() => {
    if (selectedPart || selectedPallet) {
      if (curOperation) {
        return estimateLulOperations(cycles, { operation: curOperation, pallet: selectedPallet });
      } else if (showGraph) {
        return loadOccupancyCycles(cycles, {
          partAndProc: selectedPart,
          pallet: selectedPallet,
        });
      } else {
        return filterStationCycles(cycles, {
          partAndProc: selectedPart,
          pallet: selectedPallet,
          station: FilterAnyLoadKey,
        });
      }
    } else {
      return { seriesLabel: "Station", data: HashMap.empty<string, ReadonlyArray<LoadCycleData>>() };
    }
  }, [selectedPart, selectedPallet, selectedOperation, cycles, showGraph]);
  const plannedSeries = React.useMemo(() => {
    if (selectedOperation === "LoadOp" || selectedOperation === "UnloadOp") {
      return plannedOperationSeries(points, true);
    } else {
      return undefined;
    }
  }, [points, selectedOperation]);

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
                  onClick={() => copyCyclesToClipboard(points, zoomDateRange, selectedOperation === "LULOccupancy")}
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
            default_date_range={defaultDateRange}
            current_date_zoom={zoomDateRange}
            set_date_zoom_range={(z) => setZoomRange(z.zoom)}
            last30_days={analysisPeriod === AnalysisPeriod.Last30Days}
            openDetails={props.openMaterial}
            showWorkorderAndInspect={true}
            hideMedian={selectedOperation === "LULOccupancy"}
          />
        )}
      </CardContent>
    </Card>
  );
}

const ConnectedPartLoadStationCycleChart = connect((st) => ({}), {
  openMaterial: matDetails.openMaterialById,
})(PartLoadStationCycleChart);

// --------------------------------------------------------------------------------
// Pallet Cycles
// --------------------------------------------------------------------------------

interface PalletCycleChartProps {
  readonly points: HashMap<string, ReadonlyArray<CycleData>>;
  readonly default_date_range: Date[];
  readonly selected?: string;
  readonly setSelected: (s: string) => void;
  readonly zoomDateRange?: { start: Date; end: Date };
  readonly setZoomRange: DispatchAction<guiState.ActionType.SetStationCycleDateZoom>;
}

function PalletCycleChart(props: PalletCycleChartProps) {
  let points = HashMap.empty<string, ReadonlyArray<CycleData>>();
  if (props.selected) {
    const palData = props.points.get(props.selected);
    if (palData.isSome()) {
      points = HashMap.of([props.selected, palData.get()]);
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
            <Select
              name={"Pallet-Cycles-cycle-chart-select"}
              autoWidth
              displayEmpty
              value={props.selected || ""}
              onChange={(e) => props.setSelected(e.target.value as string)}
            >
              {props.selected ? undefined : (
                <MenuItem key={0} value="">
                  <em>Select Pallet</em>
                </MenuItem>
              )}
              {props.points
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
          default_date_range={props.default_date_range}
          current_date_zoom={props.zoomDateRange}
          set_date_zoom_range={props.setZoomRange}
        />
      </CardContent>
    </Card>
  );
}

const ConnectedPalletCycleChart = connect(
  (st) => {
    if (st.Events.analysis_period === AnalysisPeriod.Last30Days) {
      const now = addDays(startOfToday(), 1);
      const oneMonthAgo = addDays(now, -30);
      return {
        points: st.Events.last30.cycles.by_pallet,
        selected: st.Gui.pallet_cycle_selected,
        default_date_range: [oneMonthAgo, now],
        zoomDateRange: st.Gui.pallet_cycle_date_zoom,
      };
    } else {
      return {
        points: st.Events.selected_month.cycles.by_pallet,
        selected: st.Gui.pallet_cycle_selected,
        default_date_range: [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)],
        zoomDateRange: st.Gui.pallet_cycle_date_zoom,
      };
    }
  },
  {
    setSelected: (p: string) => ({
      type: guiState.ActionType.SetSelectedPalletCycle,
      pallet: p,
    }),
    setZoomRange: mkAC(guiState.ActionType.SetStationCycleDateZoom),
  }
)(PalletCycleChart);

// --------------------------------------------------------------------------------
// Buffer Chart
// --------------------------------------------------------------------------------

// https://github.com/mui-org/material-ui/issues/20191
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
              onChange={(e: React.ChangeEvent<{}>, v: number) => setMovingAverage(v)}
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

interface HeatmapProps {
  readonly allowSetType: boolean;
}

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

function StationOeeHeatmap(props: HeatmapProps) {
  const [selected, setSelected] = React.useState<StationOeeHeatmapTypes>("Standard OEE");
  const data = useSelector((s) =>
    s.Events.analysis_period === AnalysisPeriod.Last30Days ? s.Events.last30 : s.Events.selected_month
  );
  const points = React.useMemo(() => {
    if (selected === "Standard OEE") {
      return dayAndStatToHeatmapPoints(binActiveCyclesByDayAndStat(data.cycles.part_cycles));
    } else if (selected === "Occupied") {
      return dayAndStatToHeatmapPoints(binOccupiedCyclesByDayAndStat(data.cycles.part_cycles));
    } else {
      return dayAndStatToHeatmapPoints(binSimStationUseByDayAndStat(data.sim_use.station_use));
    }
  }, [selected, data]);

  return (
    <SelectableHeatChart<StationOeeHeatmapTypes>
      card_label="Station Use"
      y_title="Station"
      label_title={selected === "Occupied" ? "Occupied" : "OEE"}
      icon={<HourglassIcon style={{ color: "#6D4C41" }} />}
      cur_selected={selected}
      options={["Standard OEE", "Occupied", "Planned OEE"]}
      setSelected={props.allowSetType ? setSelected : undefined}
      points={points}
      onExport={() => copyOeeHeatmapToClipboard("Station", points)}
    />
  );
}

// --------------------------------------------------------------------------------
// Completed Heatmap
// --------------------------------------------------------------------------------

interface CompletedHeatmapProps {
  readonly allowSetType: boolean;
}

type CompletedPartsHeatmapTypes = "Planned" | "Completed";

function partsCompletedPoints(cycles: CycleState) {
  const pts = binCyclesByDayAndPart(cycles.part_cycles);
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

function partsPlannedPoints(simUse: SimUseState) {
  const pts = binSimProductionByDayAndPart(simUse.production);
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

function CompletedCountHeatmap(props: CompletedHeatmapProps) {
  const [selected, setSelected] = React.useState<CompletedPartsHeatmapTypes>("Completed");
  const data = useSelector((s) =>
    s.Events.analysis_period === AnalysisPeriod.Last30Days ? s.Events.last30 : s.Events.selected_month
  );
  const points = React.useMemo(() => {
    if (selected === "Completed") {
      return partsCompletedPoints(data.cycles);
    } else {
      return partsPlannedPoints(data.sim_use);
    }
  }, [selected, data]);
  return (
    <SelectableHeatChart
      card_label="Part Production"
      y_title="Part"
      label_title={selected}
      icon={<ExtensionIcon style={{ color: "#6D4C41" }} />}
      cur_selected={selected}
      options={["Completed", "Planned"]}
      setSelected={props.allowSetType ? setSelected : undefined}
      points={points}
      onExport={() => copyCompletedPartsHeatmapToClipboard(points)}
    />
  );
}

// --------------------------------------------------------------------------------
// Inspection
// --------------------------------------------------------------------------------

const ConnectedInspection = connect(
  (st) => ({
    inspectionlogs:
      st.Events.analysis_period === AnalysisPeriod.Last30Days
        ? st.Events.last30.inspection.by_part
        : st.Events.selected_month.inspection.by_part,
    zoomType:
      st.Events.analysis_period === AnalysisPeriod.Last30Days
        ? DataTableActionZoomType.Last30Days
        : DataTableActionZoomType.ZoomIntoRange,
    default_date_range:
      st.Events.analysis_period === AnalysisPeriod.Last30Days
        ? [addDays(startOfToday(), -29), addDays(startOfToday(), 1)]
        : [st.Events.analysis_period_month, addMonths(st.Events.analysis_period_month, 1)],
    defaultToTable: false,
  }),
  {
    openMaterialDetails: matDetails.openMaterialById,
  }
)(InspectionSankey);

// --------------------------------------------------------------------------------
// Efficiency
// --------------------------------------------------------------------------------

export default function Efficiency({ allowSetType }: { allowSetType: boolean }) {
  React.useEffect(() => {
    document.title = "Efficiency - FMS Insight";
  }, []);
  return (
    <>
      <AnalysisSelectToolbar />
      <main style={{ padding: "24px" }}>
        <div data-testid="part-cycle-chart">
          <ConnectedPartMachineCycleChart />
        </div>
        <div data-testid="part-load-cycle-chart" style={{ marginTop: "3em" }}>
          <ConnectedPartLoadStationCycleChart />
        </div>
        <div data-testid="pallet-cycle-chart" style={{ marginTop: "3em" }}>
          <ConnectedPalletCycleChart />
        </div>
        <div data-testid="buffer-chart" style={{ marginTop: "3em" }}>
          <BufferOccupancyChart />
        </div>
        <div data-testid="station-oee-heatmap" style={{ marginTop: "3em" }}>
          <StationOeeHeatmap allowSetType={allowSetType} />
        </div>
        <div data-testid="completed-heatmap" style={{ marginTop: "3em" }}>
          <CompletedCountHeatmap allowSetType={allowSetType} />
        </div>
        <div data-testid="inspection-sankey" style={{ marginTop: "3em" }}>
          <ConnectedInspection />
        </div>
      </main>
    </>
  );
}
