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
import { Card } from "@mui/material";
import { CardHeader } from "@mui/material";
import { CardContent } from "@mui/material";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { Table } from "@mui/material";
import { BugReport as BugIcon, ImportExport } from "@mui/icons-material";
import {
  extractFailedInspections,
  FailedInspectionEntry,
  copyFailedInspectionsToClipboard,
} from "../../data/results.inspection.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { addDays, startOfToday } from "date-fns";
import {
  DataTableHead,
  DataTableBody,
  DataTableActions,
  Column,
  useColSort,
  useTablePage,
} from "../analysis/DataTable.js";
import { useSetMaterialToShowInDialog } from "../../cell-status/material-details.js";
import { RouteLocation, useCurrentRoute, useIsDemo } from "../routes.js";
import { useRecoilValue } from "recoil";
import { last30Inspections } from "../../cell-status/inspections.js";

interface RecentFailedInspectionsProps {
  readonly failed: ReadonlyArray<FailedInspectionEntry>;
}

enum ColumnId {
  Date,
  Part,
  InspType,
  Serial,
  Workorder,
}

const columns: ReadonlyArray<Column<ColumnId, FailedInspectionEntry>> = [
  {
    id: ColumnId.Date,
    numeric: false,
    label: "Date",
    getDisplay: (c) => c.time.toLocaleString(),
    getForSort: (c) => c.time.getTime(),
  },
  {
    id: ColumnId.Part,
    numeric: false,
    label: "Part",
    getDisplay: (c) => c.part,
  },
  {
    id: ColumnId.InspType,
    numeric: false,
    label: "Inspection",
    getDisplay: (c) => c.inspType,
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
];

function RecentFailedTable(props: RecentFailedInspectionsProps) {
  const demo = useIsDemo();
  const sort = useColSort(ColumnId.Date, columns);
  const tpage = useTablePage();
  const [, setRoute] = useCurrentRoute();
  const setMatToShow = useSetMaterialToShowInDialog();

  const curPage = Math.min(tpage.page, Math.ceil(props.failed.length / tpage.rowsPerPage));
  const points = LazySeq.of(props.failed).sortBy(sort.sortOn);

  return (
    <>
      <Table>
        <DataTableHead columns={columns} sort={sort} showDetailsCol={!demo} />
        <DataTableBody
          columns={columns}
          pageData={points.drop(curPage * tpage.rowsPerPage).take(tpage.rowsPerPage)}
          rowsPerPage={tpage.rowsPerPage}
          onClickDetails={
            demo
              ? undefined
              : (_, row) => {
                  setMatToShow({
                    type: "MatSummary",
                    summary: {
                      materialID: row.materialID,
                      partName: row.part,
                      jobUnique: "",
                      serial: row.serial,
                      workorderId: row.workorder,
                      startedProcess1: true,
                      signaledInspections: [],
                    },
                  });
                  setRoute({ route: RouteLocation.Quality_Serials });
                }
          }
        />
      </Table>
      <DataTableActions tpage={{ ...tpage, page: curPage }} count={props.failed.length} />
    </>
  );
}

export function RecentFailedInspectionsTable() {
  const inspections = useRecoilValue(last30Inspections);
  const failed = React.useMemo(() => {
    const today = startOfToday();
    const allEvts = LazySeq.of(inspections).flatMap(([_, evts]) => evts.valuesToLazySeq());
    return extractFailedInspections(allEvts, addDays(today, -4), addDays(today, 1));
  }, [inspections]);
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
                onClick={() => copyFailedInspectionsToClipboard(failed)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
        subheader="Inspections marked as failed in the last 5 days"
      />
      <CardContent>
        <RecentFailedTable failed={failed} />
      </CardContent>
    </Card>
  );
}

export function QualityDashboard(): JSX.Element {
  React.useEffect(() => {
    document.title = "Quality - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <div data-testid="recent-failed">
        <RecentFailedInspectionsTable />
      </div>
    </main>
  );
}
