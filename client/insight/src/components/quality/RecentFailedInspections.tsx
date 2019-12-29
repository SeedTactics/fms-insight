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
import Card from "@material-ui/core/Card";
import CardHeader from "@material-ui/core/CardHeader";
import CardContent from "@material-ui/core/CardContent";
import Tooltip from "@material-ui/core/Tooltip";
import IconButton from "@material-ui/core/IconButton";
import ImportExport from "@material-ui/icons/ImportExport";
import Table from "@material-ui/core/Table";
import BugIcon from "@material-ui/icons/BugReport";
import { InspectionState } from "../../data/events.inspection";
import { Vector, ToOrderable } from "prelude-ts";
import {
  extractFailedInspections,
  FailedInspectionEntry,
  copyFailedInspectionsToClipboard
} from "../../data/results.inspection";
import { LazySeq } from "../../data/lazyseq";
import { createSelector } from "reselect";
import { addDays, startOfToday } from "date-fns";
import { connect } from "../../store/store";
import { DataTableHead, DataTableBody, DataTableActions, Column } from "../analysis/DataTable";
import { openMaterialById } from "../../data/material-details";
import { RouteLocation } from "../../data/routes";
// eslint-disable-next-line @typescript-eslint/no-var-requires
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

interface RecentFailedInspectionsProps {
  readonly failed: Vector<FailedInspectionEntry>;
  readonly openDetails: (matId: number) => void;
}

enum ColumnId {
  Date,
  Part,
  InspType,
  Serial,
  Workorder
}

const columns: ReadonlyArray<Column<ColumnId, FailedInspectionEntry>> = [
  {
    id: ColumnId.Date,
    numeric: false,
    label: "Date",
    getDisplay: c => c.time.toLocaleString(),
    getForSort: c => c.time.getTime()
  },
  {
    id: ColumnId.Part,
    numeric: false,
    label: "Part",
    getDisplay: c => c.part
  },
  {
    id: ColumnId.InspType,
    numeric: false,
    label: "Inspection",
    getDisplay: c => c.inspType
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
  }
];

function RecentFailedTable(props: RecentFailedInspectionsProps) {
  const [orderBy, setOrderBy] = React.useState(ColumnId.Date);
  const [order, setOrder] = React.useState<"asc" | "desc">("desc");
  const [origCurPage, setPage] = React.useState<number>(0);
  const [rowsPerPage, setRowsPerPage] = React.useState(50);

  function handleRequestSort(property: ColumnId) {
    if (orderBy === property) {
      setOrder(order === "asc" ? "desc" : "asc");
    } else {
      setOrderBy(property);
      setOrder("asc");
    }
  }

  let sortOn: ToOrderable<FailedInspectionEntry> | { desc: ToOrderable<FailedInspectionEntry> } =
    columns[0].getForSort || columns[0].getDisplay;
  for (const col of columns) {
    if (col.id === orderBy && order === "asc") {
      sortOn = col.getForSort || col.getDisplay;
    } else if (col.id === orderBy) {
      sortOn = { desc: col.getForSort || col.getDisplay };
    }
  }

  const curPage = Math.min(origCurPage, Math.ceil(props.failed.length() / rowsPerPage));
  const points = props.failed.sortOn(sortOn);

  return (
    <>
      <Table>
        <DataTableHead
          columns={columns}
          onRequestSort={handleRequestSort}
          orderBy={orderBy}
          order={order}
          showDetailsCol
        />
        <DataTableBody
          columns={columns}
          pageData={points.drop(curPage * rowsPerPage).take(rowsPerPage)}
          onClickDetails={row => props.openDetails(row.materialID)}
        />
      </Table>
      <DataTableActions
        page={curPage}
        count={props.failed.length()}
        rowsPerPage={rowsPerPage}
        setPage={setPage}
        setRowsPerPage={setRowsPerPage}
      />
    </>
  );
}

function RecentFailedInspections(props: RecentFailedInspectionsProps) {
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <BugIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Recent Failed Inspections</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyFailedInspectionsToClipboard(props.failed)}
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
        subheader="Inspections marked as failed in the last 5 days"
      />
      <CardContent>
        <RecentFailedTable {...props} />
      </CardContent>
    </Card>
  );
}

const failedReducer = createSelector(
  (st: InspectionState, _: Date) => st.by_part,
  (_: InspectionState, today: Date) => today,
  (byPart, today) => {
    const allEvts = LazySeq.ofIterable(byPart).flatMap(([_, evts]) => evts);
    return extractFailedInspections(allEvts, addDays(today, -4), addDays(today, 1));
  }
);

const ConnectedFailedInspections = connect(
  st => ({
    failed: failedReducer(st.Events.last30.inspection, startOfToday())
  }),
  {
    openDetails: (matId: number) => [{ type: RouteLocation.Quality_Serials }, openMaterialById(matId)]
  }
)(RecentFailedInspections);

export function QualityDashboard() {
  return (
    <DocumentTitle title="Quality - FMS Insight">
      <main style={{ padding: "24px" }}>
        <div data-testid="recent-failed">
          <ConnectedFailedInspections />
        </div>
      </main>
    </DocumentTitle>
  );
}
