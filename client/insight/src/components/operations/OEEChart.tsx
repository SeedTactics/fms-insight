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
import { memo, useMemo } from "react";
import {
  Box,
  Grid,
  IconButton,
  Table,
  Tooltip as MuiTooltip,
  Select,
  MenuItem,
  Typography,
  FormControl,
} from "@mui/material";

import { Column, DataTableHead, DataTableBody, useColSort } from "../analysis/DataTable.js";
import { LazySeq, ToComparable } from "@seedtactics/immutable-collections";
import {
  OEEBarSeries,
  OEEBarPoint,
  OEEType,
  buildOeeSeries,
  copyOeeToClipboard,
} from "../../data/results.oee.js";
import { seriesColor } from "../../util/chart-colors.js";
import { addDays, startOfToday } from "date-fns";
import { last30StationCycles } from "../../cell-status/station-cycles.js";
import { last30SimStationUse } from "../../cell-status/sim-station-use.js";
import { ImportExport } from "@mui/icons-material";
import { useSetTitle } from "../routes.js";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";
import { scaleBand, scaleLinear } from "d3-scale";
import { AxisBottom, AxisLeft, GridRows } from "../AxisAndGrid.js";
import { ChartWithTooltip } from "../ChartTooltip.js";
import { localPoint } from "../../util/chart-helpers.js";

const actualOeeColor = seriesColor(0, 2);
const plannedOeeColor = seriesColor(1, 2);

const marginLeft = 50;
const marginBottom = 30;
const marginTop = 10;
const marginRight = 10;

function BarChart({
  width,
  height,
  points,
  setTooltip,
}: {
  width: number;
  height: number;
  points: ReadonlyArray<OEEBarPoint>;
  setTooltip: (t: TooltipData | null) => void;
}) {
  const yMax = height - marginTop - marginBottom;

  const hourScale = useMemo(() => scaleLinear().domain([0, 24]).range([yMax, 0]), [yMax]);

  const dateScale = useMemo(
    () =>
      scaleBand()
        .domain(
          LazySeq.of(points)
            .map((p) => p.x)
            .distinct(),
        )
        .range([0, width - marginLeft - marginRight])
        .padding(0.1),
    [points, width],
  );

  const actualPlanningScale = useMemo(
    () => scaleBand().domain(["Actual", "Planned"]).range([0, dateScale.bandwidth()]).padding(0.05),
    [dateScale],
  );

  return (
    <svg width={width} height={height}>
      <g transform={`translate(${marginLeft},${marginTop})`}>
        <AxisBottom scale={dateScale} top={yMax} />
        <AxisLeft scale={hourScale} left={0} label="Hours" />
        <GridRows scale={hourScale} width={width - marginLeft - marginRight} />
        {points.map((p, i) => (
          <g key={i} transform={`translate(${dateScale(p.x)},0)`}>
            <rect
              x={actualPlanningScale("Actual")}
              y={hourScale(p.y)}
              width={actualPlanningScale.bandwidth()}
              height={yMax - hourScale(p.y)}
              fill={actualOeeColor}
              onMouseEnter={(e) => {
                const pt = localPoint(e);
                setTooltip({
                  left: pt?.x ?? 0,
                  top: pt?.y ?? 0,
                  datum: p,
                });
              }}
              onMouseLeave={() => setTooltip(null)}
            />
            <rect
              x={actualPlanningScale("Planned")}
              y={hourScale(p.plannedHours)}
              width={actualPlanningScale.bandwidth()}
              height={yMax - hourScale(p.plannedHours)}
              fill={plannedOeeColor}
              onMouseEnter={(e) => {
                const pt = localPoint(e);
                setTooltip({
                  left: pt?.x ?? 0,
                  top: pt?.y ?? 0,
                  datum: p,
                });
              }}
              onMouseLeave={() => setTooltip(null)}
            />
          </g>
        ))}
      </g>
    </svg>
  );
}

function OEELegend({ station }: { station: string }) {
  return (
    <div style={{ marginTop: "1em", display: "flex", flexWrap: "wrap", justifyContent: "space-evenly" }}>
      <div style={{ display: "flex", alignItems: "center" }}>
        <div style={{ width: "14px", height: "14px", backgroundColor: actualOeeColor }} />
        <div style={{ marginLeft: "1em" }}>{station} Actual</div>
      </div>
      <div style={{ display: "flex", alignItems: "center" }}>
        <div style={{ width: "14px", height: "14px", backgroundColor: plannedOeeColor }} />
        <div style={{ marginLeft: "1em" }}>{station} Planned</div>
      </div>
    </div>
  );
}

type TooltipData = {
  readonly left: number;
  readonly top: number;
  readonly datum: OEEBarPoint;
};

function OEETooltip({ tooltip }: { tooltip: TooltipData }) {
  return (
    <div>
      <div>{tooltip.datum.x}</div>
      <div>Actual Hours: {tooltip.datum.y?.toFixed(1)}</div>
      <div>Planned Hours: {tooltip.datum?.plannedHours?.toFixed(1)}</div>
    </div>
  );
}

function OEESeries({ series }: { series: OEEBarSeries }) {
  const tooltipAtom = useMemo(() => atom<TooltipData | null>(null), []);
  const setTooltip = useSetAtom(tooltipAtom);
  return (
    <Grid size={{ xs: 12, md: 6 }} onMouseLeave={() => setTooltip(null)}>
      <ChartWithTooltip
        sx={{ height: "calc(100vh / 2 - 200px)" }}
        chart={({ width, height }) => (
          <BarChart width={width} height={height} points={series.points} setTooltip={setTooltip} />
        )}
        tooltipAtom={tooltipAtom}
        TooltipContent={OEETooltip}
      />
      <OEELegend station={series.station} />
    </Grid>
  );
}

export function OEEChart({ points }: { points: ReadonlyArray<OEEBarSeries> }) {
  return (
    <Grid container>
      {points.map((series, idx) => (
        <OEESeries key={idx} series={series} />
      ))}
    </Grid>
  );
}

enum ColumnId {
  Date,
  Station,
  ActualHours,
  ActualOEE,
  PlannedHours,
  PlannedOEE,
}

const columns: ReadonlyArray<Column<ColumnId, OEEBarPoint>> = [
  {
    id: ColumnId.Date,
    numeric: false,
    label: "Date",
    getDisplay: (c) => c.x,
    getForSort: (c) => c.day.getTime(),
  },
  {
    id: ColumnId.Station,
    numeric: false,
    label: "Station",
    getDisplay: (c) => c.station,
  },
  {
    id: ColumnId.ActualHours,
    numeric: true,
    label: "Actual Hours",
    getDisplay: (c) => c.y.toFixed(1),
    getForSort: (c) => c.y,
  },
  {
    id: ColumnId.ActualOEE,
    numeric: true,
    label: "Actual OEE",
    getDisplay: (c) => (c.actualOee * 100).toFixed(0) + "%",
    getForSort: (c) => c.actualOee,
  },
  {
    id: ColumnId.PlannedHours,
    numeric: true,
    label: "Planned Hours",
    getDisplay: (c) => c.plannedHours.toFixed(1),
    getForSort: (c) => c.plannedHours,
  },
  {
    id: ColumnId.PlannedOEE,
    numeric: true,
    label: "Planned OEE",
    getDisplay: (c) => (c.plannedOee * 100).toFixed(0) + "%",
    getForSort: (c) => c.plannedOee,
  },
];

function dataForTable(
  series: ReadonlyArray<OEEBarSeries>,
  sortOn: ToComparable<OEEBarPoint>,
): ReadonlyArray<OEEBarPoint> {
  return LazySeq.of(series)
    .flatMap((e) => e.points)
    .toSortedArray(sortOn);
}

export const OEETable = memo(function OEETableF({ points }: { points: ReadonlyArray<OEEBarSeries> }) {
  const sort = useColSort(ColumnId.Date, columns);
  return (
    <Table>
      <DataTableHead columns={columns} sort={sort} showDetailsCol={false} />
      <DataTableBody columns={columns} pageData={dataForTable(points, sort.sortOn)} />
    </Table>
  );
});

const lulShowChart = atom<boolean>(true);
const mcShowChart = atom<boolean>(true);

export function StationOEEPage({ ty }: { readonly ty: OEEType }) {
  useSetTitle(ty === "labor" ? "L/U OEE" : "Machine OEE");
  const [showChart, setShowChart] = useAtom(ty === "labor" ? lulShowChart : mcShowChart);

  const start = addDays(startOfToday(), -6);
  const end = addDays(startOfToday(), 1);

  const cycles = useAtomValue(last30StationCycles);
  const statUse = useAtomValue(last30SimStationUse);
  const points = useMemo(
    () => buildOeeSeries(start, end, ty, cycles.valuesToLazySeq(), statUse),
    [start, end, ty, cycles, statUse],
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
        <Typography variant="subtitle1">
          {ty === "labor" ? "Load/Unload" : "Machine"} OEE: comparing flexplan hours between actual and
          simulated production
        </Typography>
        <Box flexGrow={1} />
        <FormControl size="small">
          <Select
            autoWidth
            value={showChart ? "chart" : "table"}
            onChange={(e) => setShowChart(e.target.value === "chart")}
          >
            <MenuItem key="chart" value="chart">
              Chart
            </MenuItem>
            <MenuItem key="table" value="table">
              Table
            </MenuItem>
          </Select>
        </FormControl>
        <MuiTooltip title="Copy to Clipboard">
          <IconButton
            style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
            onClick={() => copyOeeToClipboard(points)}
            size="large"
          >
            <ImportExport />
          </IconButton>
        </MuiTooltip>
      </Box>
      <main>{showChart ? <OEEChart points={points} /> : <OEETable points={points} />}</main>
    </Box>
  );
}
