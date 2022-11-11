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
import { Table } from "@mui/material";

import { format_cycle_inspection } from "../../data/results.cycles.js";
import {
  Column,
  DataTableHead,
  DataTableActions,
  DataTableBody,
  DataTableActionZoom,
  DataTableActionZoomType,
  useColSort,
  useTablePage,
  TableZoom,
} from "./DataTable.js";
import { addDays, addHours, addMonths } from "date-fns";
import * as api from "../../network/api.js";
import { Menu } from "@mui/material";
import { MenuItem } from "@mui/material";
import { useSetMaterialToShowInDialog } from "../../cell-status/material-details.js";
import { MaterialSummaryAndCompletedData } from "../../cell-status/material-summary.js";
import { PartCycleData } from "../../cell-status/station-cycles.js";
import {
  HashMap,
  LazySeq,
  mkCompareByProperties,
  ToComparableBase,
} from "@seedtactics/immutable-collections";
import { SelectedAnalysisPeriod } from "../../network/load-specific-month.js";

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
  MedianElapsed,
  MedianDeviation,
}

function buildColumns(
  matIds: HashMap<number, MaterialSummaryAndCompletedData>
): ReadonlyArray<Column<ColumnId, PartCycleData>> {
  return [
    {
      id: ColumnId.Date,
      numeric: false,
      label: "Date",
      getDisplay: (c) => c.x.toLocaleString(),
      getForSort: (c) => c.x.getTime(),
    },
    {
      id: ColumnId.Part,
      numeric: false,
      label: "Part",
      getDisplay: (c) => c.part + "-" + c.process.toString(),
    },
    {
      id: ColumnId.Station,
      numeric: false,
      label: "Station",
      getDisplay: (c) => c.stationGroup + " " + c.stationNumber.toString(),
    },
    {
      id: ColumnId.Pallet,
      numeric: false,
      label: "Pallet",
      getDisplay: (c) => c.pallet,
    },
    {
      id: ColumnId.Serial,
      numeric: false,
      label: "Serial",
      getDisplay: (c) =>
        c.material
          .filter((m) => m.serial)
          .map((m) => m.serial)
          .join(", "),
    },
    {
      id: ColumnId.Workorder,
      numeric: false,
      label: "Workorder",
      getDisplay: (c) =>
        c.material
          .filter((m) => m.workorder)
          .map((m) => m.workorder)
          .join(", "),
    },
    {
      id: ColumnId.Inspection,
      numeric: false,
      label: "Inspection",
      getDisplay: (c) => format_cycle_inspection(c, matIds),
      getForSort: (c) => {
        return LazySeq.of(c.material)
          .collect((m) => matIds.get(m.id))
          .flatMap((m) => m.signaledInspections)
          .distinct()
          .toSortedArray((x) => x)
          .join(",");
      },
    },
    {
      id: ColumnId.ElapsedMin,
      numeric: true,
      label: "Elapsed Min",
      getDisplay: (c) => c.y.toFixed(1),
    },
    {
      id: ColumnId.ActiveMin,
      numeric: true,
      label: "Target Min",
      getDisplay: (c) => c.activeMinutes.toFixed(1),
    },
    {
      id: ColumnId.MedianElapsed,
      numeric: true,
      label: "Median Elapsed Min",
      getDisplay: (c) => c.medianCycleMinutes.toFixed(1),
    },
    {
      id: ColumnId.MedianDeviation,
      numeric: true,
      label: "Median Deviation",
      getDisplay: (c) => c.MAD_aboveMinutes.toFixed(1),
    },
  ];
}

interface StationDataTableProps {
  readonly points: ReadonlyMap<string, ReadonlyArray<PartCycleData>>;
  readonly matsById: HashMap<number, MaterialSummaryAndCompletedData>;
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
  readonly period: SelectedAnalysisPeriod;
  readonly showWorkorderAndInspect: boolean;
  readonly hideMedian?: boolean;
  readonly defaultSortDesc?: boolean;
}

function extractData(
  points: ReadonlyMap<string, ReadonlyArray<PartCycleData>>,
  columns: ReadonlyArray<Column<ColumnId, PartCycleData>>,
  currentZoom: { start: Date; end: Date } | undefined,
  orderBy: ColumnId,
  order: "asc" | "desc"
): ReadonlyArray<PartCycleData> {
  let getData: ToComparableBase<PartCycleData> | undefined;
  for (const col of columns) {
    if (col.id === orderBy) {
      getData = col.getForSort || col.getDisplay;
    }
  }
  if (getData === undefined) {
    getData = columns[0].getForSort || columns[0].getDisplay;
  }
  const getDataC = getData;

  const data = LazySeq.of(points.values()).flatMap((x) => x);
  const arr = currentZoom
    ? data.filter((p) => p.x >= currentZoom.start && p.x <= currentZoom.end).toMutableArray()
    : data.toMutableArray();
  return arr.sort(
    mkCompareByProperties(
      order === "desc" ? { desc: getDataC } : { asc: getDataC },
      order === "desc" ? { desc: (a) => a.x.getTime() } : { asc: (a) => a.x.getTime() }
    )
  );
}

function useZoom(props: StationDataTableProps): TableZoom {
  let zoom: DataTableActionZoom | undefined;
  const setZoomRange = props.set_date_zoom_range;
  if (setZoomRange) {
    if (props.period.type === "Last30") {
      zoom = {
        type: DataTableActionZoomType.Last30Days,
        set_days_back: (numDaysBack) => {
          if (numDaysBack) {
            const now = new Date();
            setZoomRange({ zoom: { start: addDays(now, -numDaysBack), end: addHours(now, 1) } });
          } else {
            setZoomRange({ zoom: undefined });
          }
        },
      };
    } else {
      zoom = {
        type: DataTableActionZoomType.ZoomIntoRange,
        default_date_range: [props.period.month, addMonths(props.period.month, 1)],
        current_date_zoom: props.current_date_zoom,
        set_date_zoom_range: (z) => setZoomRange({ zoom: z }),
      };
    }
  }

  return {
    zoom,
    zoomRange: props.current_date_zoom,
  };
}

interface DetailMenuData {
  readonly anchorEl: Element;
  readonly material: ReadonlyArray<Readonly<api.ILogMaterial>>;
}

export default React.memo(function StationDataTable(props: StationDataTableProps) {
  const columns = buildColumns(props.matsById);
  const sort = useColSort(ColumnId.Date, columns);
  const tpage = useTablePage();
  const zoom = useZoom(props);
  const [detailMenu, setDetailMenu] = React.useState<DetailMenuData | null>(null);
  const setMatToShow = useSetMaterialToShowInDialog();

  const allData = extractData(props.points, columns, props.current_date_zoom, sort.orderBy, sort.order);
  const totalDataLength = allData.length;
  const pageData: ReadonlyArray<PartCycleData> = allData.slice(
    tpage.page * tpage.rowsPerPage,
    (tpage.page + 1) * tpage.rowsPerPage
  );
  const filteredColumns = columns.filter((c) => {
    if (!props.showWorkorderAndInspect && c.id === ColumnId.Workorder) {
      return false;
    }
    if (!props.showWorkorderAndInspect && c.id === ColumnId.Inspection) {
      return false;
    }
    if (props.hideMedian && c.id === ColumnId.MedianElapsed) {
      return false;
    }
    if (props.hideMedian && c.id === ColumnId.MedianDeviation) {
      return false;
    }
    return true;
  });
  return (
    <div>
      <Table>
        <DataTableHead columns={filteredColumns} sort={sort} showDetailsCol />
        <DataTableBody
          columns={filteredColumns}
          pageData={pageData}
          rowsPerPage={tpage.rowsPerPage}
          onClickDetails={(e, row) => {
            if (row.material.length === 0) return;
            if (row.material.length === 1) {
              setMatToShow({ type: "LogMat", logMat: row.material[0] });
            } else {
              setDetailMenu({ anchorEl: e.currentTarget, material: row.material });
            }
          }}
        />
      </Table>
      <DataTableActions zoom={zoom.zoom} tpage={tpage} count={totalDataLength} />
      <Menu
        anchorEl={detailMenu?.anchorEl}
        keepMounted
        open={detailMenu !== null}
        onClose={() => setDetailMenu(null)}
      >
        {detailMenu != null
          ? detailMenu.material.map((mat, idx) => (
              <MenuItem
                key={idx}
                onClick={() => {
                  setMatToShow({ type: "LogMat", logMat: mat });
                  setDetailMenu(null);
                }}
              >
                {mat.serial || "Material"}
              </MenuItem>
            ))
          : undefined}
      </Menu>
    </div>
  );
});
