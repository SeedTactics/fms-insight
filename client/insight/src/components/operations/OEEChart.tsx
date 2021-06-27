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
import { Grid } from "@material-ui/core";
import { Table } from "@material-ui/core";

import { FlexibleWidthXYPlot, XAxis, YAxis, VerticalBarSeries, DiscreteColorLegend, Hint } from "react-vis";
import { Column, DataTableHead, DataTableBody } from "../analysis/DataTable";
import { LazySeq } from "../../data/lazyseq";
import { ToOrderable } from "prelude-ts";
import { OEEBarSeries, OEEBarPoint } from "../../data/results.oee";

export interface OEEProps {
  readonly showLabor: boolean;
  readonly start: Date;
  readonly end: Date;
  readonly points: ReadonlyArray<OEEBarSeries>;
}

function format_oee_hint(p: OEEBarPoint): ReadonlyArray<{ title: string; value: string }> {
  return [
    { title: "Day", value: p.x },
    { title: "Actual Hours", value: p.y.toFixed(1) },
    { title: "Planned Hours", value: p.planned.toFixed(1) },
  ];
}

const actualOeeColor = "#6200EE";
const plannedOeeColor = "#03DAC5";

export function OEEChart(props: OEEProps) {
  const [hoveredSeries, setHoveredSeries] = React.useState<{ station: string; day: string } | undefined>(undefined);

  const [chartHeight, setChartHeight] = React.useState(300);
  React.useEffect(() => {
    setChartHeight(window.innerHeight / 2 - 200);
  }, []);

  return (
    <Grid container>
      {props.points.map((series, idx) => (
        <Grid item xs={12} md={6} key={idx}>
          <div>
            <FlexibleWidthXYPlot
              xType="ordinal"
              height={chartHeight}
              animation
              yDomain={[0, 24]}
              onMouseLeave={() => setHoveredSeries(undefined)}
            >
              <XAxis />
              <YAxis />
              <VerticalBarSeries
                data={series.points}
                onValueMouseOver={(p: OEEBarPoint) => setHoveredSeries({ station: series.station, day: p.x })}
                onValueMouseOut={() => setHoveredSeries(undefined)}
                color={actualOeeColor}
              />
              <VerticalBarSeries
                data={series.points}
                getY={(p: OEEBarPoint) => p.planned}
                color={plannedOeeColor}
                onValueMouseOver={(p: OEEBarPoint) => setHoveredSeries({ station: series.station, day: p.x })}
                onValueMouseOut={() => setHoveredSeries(undefined)}
              />
              {hoveredSeries === undefined || hoveredSeries.station !== series.station ? undefined : (
                <Hint
                  value={series.points.find((p: OEEBarPoint) => p.x === hoveredSeries.day)}
                  format={format_oee_hint}
                />
              )}
            </FlexibleWidthXYPlot>
            <div style={{ textAlign: "center" }}>
              {props.points.length > 1 ? (
                <DiscreteColorLegend
                  orientation="horizontal"
                  items={[
                    { title: series.station + " Actual", color: actualOeeColor },
                    { title: series.station + " Planned", color: plannedOeeColor },
                  ]}
                />
              ) : undefined}
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
  orderBy: ColumnId,
  order: "asc" | "desc"
): ReadonlyArray<OEEBarPoint> {
  let getData: ToOrderable<OEEBarPoint> | undefined;
  for (const col of columns) {
    if (col.id === orderBy) {
      getData = col.getForSort || col.getDisplay;
    }
  }
  if (getData === undefined) {
    getData = columns[0].getForSort || columns[0].getDisplay;
  }
  const getDataC = getData;

  const arr = LazySeq.ofIterable(series)
    .flatMap((e) => e.points)
    .toArray();
  return arr.sort((a, b) => {
    const aVal = getDataC(a);
    const bVal = getDataC(b);
    if (aVal === bVal) {
      // sort by date
      if (order === "desc") {
        return b.day.getTime() - a.day.getTime();
      } else {
        return a.day.getTime() - b.day.getTime();
      }
    } else {
      if (order === "desc") {
        return aVal > bVal ? -1 : 1;
      } else {
        return aVal > bVal ? 1 : -1;
      }
    }
  });
}

export const OEETable = React.memo(function OEETableF(p: OEEProps) {
  const [orderBy, setOrderBy] = React.useState(ColumnId.Date);
  const [order, setOrder] = React.useState<"asc" | "desc">("asc");
  function handleRequestSort(property: ColumnId) {
    if (orderBy === property) {
      setOrder(order === "asc" ? "desc" : "asc");
    } else {
      setOrderBy(property);
      setOrder("asc");
    }
  }
  return (
    <Table>
      <DataTableHead
        columns={columns}
        onRequestSort={handleRequestSort}
        orderBy={orderBy}
        order={order}
        showDetailsCol={false}
      />
      <DataTableBody columns={columns} pageData={dataForTable(p.points, orderBy, order)} />
    </Table>
  );
});
