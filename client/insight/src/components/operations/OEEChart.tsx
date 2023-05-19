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
import { AnimatedAxis, AnimatedBarGroup, AnimatedBarSeries, Tooltip, XYChart } from "@visx/xychart";
import { seriesColor, chartTheme } from "../../util/chart-colors.js";
import { addDays, startOfToday } from "date-fns";
import { atom, useRecoilState, useRecoilValue } from "recoil";
import { last30StationCycles } from "../../cell-status/station-cycles.js";
import { last30SimStationUse } from "../../cell-status/sim-station-use.js";
import { ImportExport } from "@mui/icons-material";
import { useSetTitle } from "../routes.js";

export interface OEEProps {
  readonly points: ReadonlyArray<OEEBarSeries>;
}

const actualOeeColor = seriesColor(0, 2);
const plannedOeeColor = seriesColor(1, 2);

export function OEEChart(props: OEEProps) {
  return (
    <Grid container>
      {props.points.map((series, idx) => (
        <Grid item xs={12} md={6} key={idx}>
          <div>
            <Box sx={{ height: "calc(100vh / 2 - 200px)" }}>
              <XYChart
                xScale={{ type: "band" }}
                yScale={{ type: "linear", domain: [0, 24] }}
                theme={chartTheme}
              >
                <AnimatedAxis orientation="bottom" />
                <AnimatedAxis orientation="left" tickValues={[0, 8, 16, 24]} />
                <AnimatedBarGroup>
                  <AnimatedBarSeries
                    data={series.points as OEEBarPoint[]}
                    dataKey="Actual"
                    xAccessor={(p) => p.x}
                    yAccessor={(p) => p.y}
                    colorAccessor={() => actualOeeColor}
                  />
                  <AnimatedBarSeries
                    data={series.points as OEEBarPoint[]}
                    dataKey="Planned"
                    xAccessor={(p) => p.x}
                    yAccessor={(p) => p.planned}
                    colorAccessor={() => plannedOeeColor}
                  />
                </AnimatedBarGroup>
                <Tooltip<OEEBarPoint>
                  snapTooltipToDatumX
                  renderTooltip={({ tooltipData }) => (
                    <div>
                      <div>{tooltipData?.nearestDatum?.datum?.x}</div>
                      <div>Actual Hours: {tooltipData?.nearestDatum?.datum?.y?.toFixed(1)}</div>
                      <div>Planned Hours: {tooltipData?.nearestDatum?.datum?.planned?.toFixed(1)}</div>
                    </div>
                  )}
                />
              </XYChart>
            </Box>
            <div
              style={{ marginTop: "1em", display: "flex", flexWrap: "wrap", justifyContent: "space-evenly" }}
            >
              <div style={{ display: "flex", alignItems: "center" }}>
                <div style={{ width: "14px", height: "14px", backgroundColor: actualOeeColor }} />
                <div style={{ marginLeft: "1em" }}>{series.station} Actual</div>
              </div>
              <div style={{ display: "flex", alignItems: "center" }}>
                <div style={{ width: "14px", height: "14px", backgroundColor: plannedOeeColor }} />
                <div style={{ marginLeft: "1em" }}>{series.station} Planned</div>
              </div>
            </div>
          </div>
        </Grid>
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
    getDisplay: (c) => ((c.y * 100) / 24).toFixed(0) + "%",
    getForSort: (c) => c.y,
  },
  {
    id: ColumnId.PlannedHours,
    numeric: true,
    label: "Planned Hours",
    getDisplay: (c) => c.planned.toFixed(1),
    getForSort: (c) => c.planned,
  },
  {
    id: ColumnId.PlannedOEE,
    numeric: true,
    label: "Planned OEE",
    getDisplay: (c) => ((c.planned * 100) / 24).toFixed(0) + "%",
    getForSort: (c) => c.planned,
  },
];

function dataForTable(
  series: ReadonlyArray<OEEBarSeries>,
  sortOn: ToComparable<OEEBarPoint>
): ReadonlyArray<OEEBarPoint> {
  return LazySeq.of(series)
    .flatMap((e) => e.points)
    .toSortedArray(sortOn);
}

export const OEETable = React.memo(function OEETableF(p: OEEProps) {
  const sort = useColSort(ColumnId.Date, columns);
  return (
    <Table>
      <DataTableHead columns={columns} sort={sort} showDetailsCol={false} />
      <DataTableBody columns={columns} pageData={dataForTable(p.points, sort.sortOn)} />
    </Table>
  );
});

const lulShowChart = atom<boolean>({
  key: "insight-oee-chart-labor",
  default: true,
});
const mcShowChart = atom<boolean>({
  key: "insight-oee-chart-machine",
  default: true,
});

export function StationOEEPage({ ty }: { readonly ty: OEEType }) {
  useSetTitle(ty === "labor" ? "L/U OEE" : "Machine OEE");
  const [showChart, setShowChart] = useRecoilState(ty === "labor" ? lulShowChart : mcShowChart);

  const start = addDays(startOfToday(), -6);
  const end = addDays(startOfToday(), 1);

  const cycles = useRecoilValue(last30StationCycles);
  const statUse = useRecoilValue(last30SimStationUse);
  const points = React.useMemo(
    () => buildOeeSeries(start, end, ty, cycles.valuesToLazySeq(), statUse),
    [start, end, ty, cycles, statUse]
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
