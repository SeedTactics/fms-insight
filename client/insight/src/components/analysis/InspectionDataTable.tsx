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
import ExpansionPanel from "@material-ui/core/ExpansionPanel";
import ExpansionPanelDetails from "@material-ui/core/ExpansionPanelDetails";
import ExpansionPanelSummary from "@material-ui/core/ExpansionPanelSummary";
import ExpandMoreIcon from "@material-ui/icons/ExpandMore";

import { Column, DataTableHead, DataTableActions, DataTableBody } from "./DataTable";
import { InspectionLogEntry } from "../../data/events.inspection";
import { Typography } from "@material-ui/core";
import { HashMap, ToOrderable } from "prelude-ts";
import { TriggeredInspectionEntry, groupInspectionsByPath } from "../../data/results.inspection";

enum ColumnId {
  Date,
  Serial,
  Workorder,
  Inspected,
  Failed
}

const columns: ReadonlyArray<Column<ColumnId, TriggeredInspectionEntry>> = [
  {
    id: ColumnId.Date,
    numeric: false,
    label: "Date",
    getDisplay: c => c.time.toLocaleString(),
    getForSort: c => c.time.getTime()
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
    id: ColumnId.Inspected,
    numeric: false,
    label: "Inspected",
    getDisplay: c => (c.toInspect ? "inspected" : "")
  },
  {
    id: ColumnId.Failed,
    numeric: false,
    label: "Failed",
    getDisplay: c => (c.failed ? "failed" : "")
  }
];

export interface InspectionDataTableProps {
  readonly points: ReadonlyArray<InspectionLogEntry>;
  readonly default_date_range: Date[];
  readonly last30_days: boolean;
  readonly allowChangeDateRange: boolean;
  readonly openDetails: ((matId: number) => void) | undefined;
}

export default React.memo(function InspDataTable(props: InspectionDataTableProps) {
  const openDetails = props.openDetails;
  const [orderBy, setOrderBy] = React.useState(ColumnId.Date);
  const [order, setOrder] = React.useState<"asc" | "desc">("asc");
  const [pages, setPages] = React.useState<HashMap<string, number>>(HashMap.empty());
  const [rowsPerPage, setRowsPerPage] = React.useState(10);
  const [curPath, setCurPathOpen] = React.useState<string | undefined>(undefined);
  const [curZoom, setCurZoom] = React.useState<{ start: Date; end: Date } | undefined>(undefined);

  function handleRequestSort(property: ColumnId) {
    if (orderBy === property) {
      setOrder(order === "asc" ? "desc" : "asc");
    } else {
      setOrderBy(property);
      setOrder("asc");
    }
  }

  let sortOn: ToOrderable<TriggeredInspectionEntry> | { desc: ToOrderable<TriggeredInspectionEntry> } =
    columns[0].getForSort || columns[0].getDisplay;
  for (let col of columns) {
    if (col.id === orderBy && order === "asc") {
      sortOn = col.getForSort || col.getDisplay;
    } else if (col.id === orderBy) {
      sortOn = { desc: col.getForSort || col.getDisplay };
    }
  }

  const groups = groupInspectionsByPath(props.points, curZoom, sortOn);
  const paths = groups.keySet().toArray({ sortOn: x => x });

  return (
    <div style={{ width: "100%" }}>
      {paths.map(path => {
        const points = groups.get(path).getOrThrow();
        const page = Math.min(pages.get(path).getOrElse(0), Math.ceil(points.material.length() / rowsPerPage));
        return (
          <ExpansionPanel
            expanded={path === curPath}
            key={path}
            onChange={(_evt, open) => setCurPathOpen(open ? path : undefined)}
          >
            <ExpansionPanelSummary expandIcon={<ExpandMoreIcon />}>
              <Typography variant="h6" style={{ flexBasis: "33.33%", flexShrink: 0 }}>
                {path}
              </Typography>
              <Typography variant="caption">
                {points.material.length()} total parts, {points.failedCnt} failed
              </Typography>
            </ExpansionPanelSummary>
            <ExpansionPanelDetails>
              <div style={{ width: "100%" }}>
                <Table>
                  <DataTableHead
                    columns={columns}
                    onRequestSort={handleRequestSort}
                    orderBy={orderBy}
                    order={order}
                    showDetailsCol={props.openDetails !== undefined}
                  />
                  <DataTableBody
                    columns={columns}
                    pageData={points.material.drop(page * rowsPerPage).take(rowsPerPage)}
                    onClickDetails={openDetails ? row => openDetails(row.materialID) : undefined}
                  />
                </Table>
                <DataTableActions
                  page={page}
                  count={points.material.length()}
                  last30_days={props.last30_days}
                  rowsPerPage={rowsPerPage}
                  setPage={p => setPages(pages.put(path, p))}
                  setRowsPerPage={setRowsPerPage}
                  set_date_zoom_range={props.allowChangeDateRange ? p => setCurZoom(p.zoom) : undefined}
                  default_date_range={props.default_date_range}
                  current_date_zoom={curZoom}
                />
              </div>
            </ExpansionPanelDetails>
          </ExpansionPanel>
        );
      })}
    </div>
  );
});
