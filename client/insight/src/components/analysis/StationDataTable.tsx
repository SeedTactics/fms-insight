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
import Table from "@material-ui/core/Table";
import { HashMap, ToOrderable } from "prelude-ts";

import { PartCycleData, format_cycle_inspection } from "../../data/events.cycles";
import { LazySeq } from "../../data/lazyseq";
import { Column, DataTableHead, DataTableActions, DataTableBody } from "./DataTable";

enum ColumnId {
  Date,
  Part,
  Station,
  Pallet,
  Serial,
  Workorder,
  Inspection,
  ElapsedMin,
  ActiveMin,
  MedianElapsed
}

const columns: ReadonlyArray<Column<ColumnId, PartCycleData>> = [
  {
    id: ColumnId.Date,
    numeric: false,
    label: "Date",
    getDisplay: c => c.x.toLocaleString(),
    getForSort: c => c.x.getTime()
  },
  { id: ColumnId.Part, numeric: false, label: "Part", getDisplay: c => c.part + "-" + c.process.toString() },
  {
    id: ColumnId.Station,
    numeric: false,
    label: "Station",
    getDisplay: c => c.stationGroup + " " + c.stationNumber.toString()
  },
  {
    id: ColumnId.Pallet,
    numeric: false,
    label: "Pallet",
    getDisplay: c => c.pallet
  },
  {
    id: ColumnId.Serial,
    numeric: false,
    label: "Serial",
    getDisplay: c => c.serial || ""
  },
  {
    id: ColumnId.Workorder,
    numeric: false,
    label: "Workorder",
    getDisplay: c => c.workorder || ""
  },
  {
    id: ColumnId.Inspection,
    numeric: false,
    label: "Inspection",
    getDisplay: format_cycle_inspection,
    getForSort: c => {
      return c.signaledInspections.toArray({ sortOn: x => x }).join(",");
    }
  },
  {
    id: ColumnId.ElapsedMin,
    numeric: true,
    label: "Elapsed Min",
    getDisplay: c => c.y.toFixed(1)
  },
  {
    id: ColumnId.ActiveMin,
    numeric: true,
    label: "Active Min",
    getDisplay: c => c.active.toFixed(1)
  },
  {
    id: ColumnId.MedianElapsed,
    numeric: true,
    label: "Median Elapsed Min",
    getDisplay: c => c.medianElapsed.toFixed(1)
  }
];

interface StationDataTableProps {
  readonly points: HashMap<string, ReadonlyArray<PartCycleData>>;
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
  readonly last30_days: boolean;
  readonly showWorkorderAndInspect: boolean;
  readonly showMedian: boolean;
  readonly openDetails: (matId: number) => void;
}

function extractData(
  points: HashMap<string, ReadonlyArray<PartCycleData>>,
  currentZoom: { start: Date; end: Date } | undefined,
  orderBy: ColumnId,
  order: "asc" | "desc"
): ReadonlyArray<PartCycleData> {
  let getData: ToOrderable<PartCycleData> | undefined;
  for (let col of columns) {
    if (col.id === orderBy) {
      getData = col.getForSort || col.getDisplay;
    }
  }
  if (getData === undefined) {
    getData = columns[0].getForSort || columns[0].getDisplay;
  }
  const getDataC = getData;

  const data = LazySeq.ofIterable(points.valueIterable()).flatMap(x => x);
  const arr = currentZoom
    ? data.filter(p => p.x >= currentZoom.start && p.x <= currentZoom.end).toArray()
    : data.toArray();
  return arr.sort((a, b) => {
    const aVal = getDataC(a);
    const bVal = getDataC(b);
    if (aVal === bVal) {
      // sort by date
      if (order === "desc") {
        return b.x.getTime() - a.x.getTime();
      } else {
        return a.x.getTime() - b.x.getTime();
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

export default React.memo(function StationDataTable(props: StationDataTableProps) {
  const [orderBy, setOrderBy] = React.useState(ColumnId.Date);
  const [order, setOrder] = React.useState<"asc" | "desc">("asc");
  const [page, setPage] = React.useState(0);
  const [rowsPerPage, setRowsPerPage] = React.useState(10);

  function handleRequestSort(property: ColumnId) {
    if (orderBy === property) {
      setOrder(order === "asc" ? "desc" : "asc");
    } else {
      setOrderBy(property);
      setOrder("asc");
    }
  }

  const allData = extractData(props.points, props.current_date_zoom, orderBy, order);
  const totalDataLength = allData.length;
  const pageData: ReadonlyArray<PartCycleData> = allData.slice(page * rowsPerPage, (page + 1) * rowsPerPage);
  const filteredColumns = columns.filter(c => {
    if (!props.showWorkorderAndInspect && c.id === ColumnId.Workorder) {
      return false;
    }
    if (!props.showWorkorderAndInspect && c.id === ColumnId.Inspection) {
      return false;
    }
    if (!props.showMedian && c.id === ColumnId.MedianElapsed) {
      return false;
    }
    return true;
  });
  return (
    <div>
      <Table>
        <DataTableHead columns={filteredColumns} onRequestSort={handleRequestSort} orderBy={orderBy} order={order} />
        <DataTableBody
          columns={filteredColumns}
          pageData={pageData}
          onClickDetails={row => props.openDetails(row.matId)}
        />
      </Table>
      <DataTableActions
        page={page}
        count={totalDataLength}
        last30_days={props.last30_days}
        rowsPerPage={rowsPerPage}
        setPage={setPage}
        setRowsPerPage={setRowsPerPage}
        set_date_zoom_range={props.set_date_zoom_range}
        default_date_range={props.default_date_range}
        current_date_zoom={props.current_date_zoom}
      />
    </div>
  );
});
