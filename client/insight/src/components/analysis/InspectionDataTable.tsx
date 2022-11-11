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
import { Accordion } from "@mui/material";
import { AccordionDetails } from "@mui/material";
import { AccordionSummary } from "@mui/material";
import { ExpandMore as ExpandMoreIcon } from "@mui/icons-material";

import {
  Column,
  DataTableHead,
  DataTableActions,
  DataTableBody,
  DataTableActionZoom,
  DataTableActionZoomType,
  useColSort,
  TableZoom,
} from "./DataTable.js";
import { InspectionLogEntry } from "../../cell-status/inspections.js";
import { Typography } from "@mui/material";
import { TriggeredInspectionEntry, groupInspectionsByPath } from "../../data/results.inspection.js";
import { addDays, addHours } from "date-fns";
import { useSetMaterialToShowInDialog } from "../../cell-status/material-details.js";
import { LazySeq, HashMap } from "@seedtactics/immutable-collections";

enum ColumnId {
  Date,
  Serial,
  Workorder,
  Inspected,
  Failed,
}

const columns: ReadonlyArray<Column<ColumnId, TriggeredInspectionEntry>> = [
  {
    id: ColumnId.Date,
    numeric: false,
    label: "Date",
    getDisplay: (c) => c.time.toLocaleString(),
    getForSort: (c) => c.time.getTime(),
  },
  {
    id: ColumnId.Serial,
    numeric: false,
    label: "Serial",
    getDisplay: (c) => c.serial || "",
  },
  {
    id: ColumnId.Workorder,
    numeric: false,
    label: "Workorder",
    getDisplay: (c) => c.workorder || "",
  },
  {
    id: ColumnId.Inspected,
    numeric: false,
    label: "Inspected",
    getDisplay: (c) => (c.toInspect ? "inspected" : ""),
  },
  {
    id: ColumnId.Failed,
    numeric: false,
    label: "Failed",
    getDisplay: (c) => (c.failed ? "failed" : ""),
  },
];

function useZoom(
  zoomType: DataTableActionZoomType | undefined,
  extendDateRange: ((numDays: number) => void) | undefined,
  default_date_range: Date[]
): TableZoom {
  const [curZoom, setCurZoom] = React.useState<{ start: Date; end: Date } | undefined>(undefined);

  let zoom: DataTableActionZoom | undefined;
  if (zoomType && zoomType === DataTableActionZoomType.Last30Days) {
    zoom = {
      type: DataTableActionZoomType.Last30Days,
      set_days_back: (numDaysBack) => {
        if (numDaysBack) {
          const now = new Date();
          setCurZoom({ start: addDays(now, -numDaysBack), end: addHours(now, 1) });
        } else {
          setCurZoom(undefined);
        }
      },
    };
  } else if (zoomType && zoomType === DataTableActionZoomType.ZoomIntoRange) {
    zoom = {
      type: DataTableActionZoomType.ZoomIntoRange,
      default_date_range,
      current_date_zoom: curZoom,
      set_date_zoom_range: setCurZoom,
    };
  } else if (zoomType && extendDateRange && zoomType === DataTableActionZoomType.ExtendDays) {
    zoom = {
      type: DataTableActionZoomType.ExtendDays,
      curStart: default_date_range[0],
      curEnd: default_date_range[1],
      extend: extendDateRange,
    };
  }

  return { zoom, zoomRange: curZoom };
}

export interface InspectionDataTableProps {
  readonly points: Iterable<InspectionLogEntry>;
  readonly default_date_range: Date[];
  readonly zoomType?: DataTableActionZoomType;
  readonly extendDateRange?: (numDays: number) => void;
  readonly hideOpenDetailColumn?: boolean;
}

export default React.memo(function InspDataTable(props: InspectionDataTableProps) {
  const setMatToShow = useSetMaterialToShowInDialog();
  const sort = useColSort(ColumnId.Date, columns);
  const [pages, setPages] = React.useState<HashMap<string, number>>(HashMap.empty());
  const [rowsPerPage, setRowsPerPage] = React.useState(10);
  const [curPath, setCurPathOpen] = React.useState<string | undefined>(undefined);
  const tzoom = useZoom(props.zoomType, props.extendDateRange, props.default_date_range);

  const groups = groupInspectionsByPath(props.points, tzoom.zoomRange, sort.sortOn);
  const paths = LazySeq.of(groups).toSortedArray(([x]) => x);

  return (
    <div style={{ width: "100%" }}>
      {paths.map(([path, points]) => {
        const page = Math.min(pages.get(path) ?? 0, Math.ceil(points.material.length / rowsPerPage));
        return (
          <Accordion
            expanded={path === curPath}
            key={path}
            onChange={(_evt, open) => setCurPathOpen(open ? path : undefined)}
          >
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Typography variant="h6" style={{ flexBasis: "33.33%", flexShrink: 0 }}>
                {path}
              </Typography>
              <Typography variant="caption">
                {points.material.length} total parts, {points.failedCnt} failed
              </Typography>
            </AccordionSummary>
            <AccordionDetails>
              <div style={{ width: "100%" }}>
                <Table>
                  <DataTableHead columns={columns} sort={sort} showDetailsCol />
                  <DataTableBody
                    columns={columns}
                    pageData={LazySeq.of(points.material)
                      .drop(page * rowsPerPage)
                      .take(rowsPerPage)}
                    rowsPerPage={rowsPerPage}
                    onClickDetails={
                      props.hideOpenDetailColumn
                        ? undefined
                        : (_, row) =>
                            setMatToShow({
                              type: "MatSummary",
                              summary: {
                                materialID: row.materialID,
                                jobUnique: "",
                                partName: row.partName,
                                startedProcess1: true,
                                serial: row.serial,
                                workorderId: row.workorder,
                                signaledInspections: [],
                              },
                            })
                    }
                  />
                </Table>
                <DataTableActions
                  tpage={{ page, rowsPerPage, setPage: (p) => setPages(pages.set(path, p)), setRowsPerPage }}
                  count={points.material.length}
                  zoom={tzoom.zoom}
                />
              </div>
            </AccordionDetails>
          </Accordion>
        );
      })}
    </div>
  );
});
