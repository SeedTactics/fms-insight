/* Copyright (c) 2022, John Lenz

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
import { Card, CardContent, CardHeader, IconButton, MenuItem, Select, Table, Tooltip } from "@mui/material";
import { ImportExport, Dns as ToolIcon } from "@mui/icons-material";
import * as React from "react";
import { useRecoilValue } from "recoil";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import {
  last30ToolReplacements,
  specificMonthToolReplacements,
  StationGroupAndNum,
  ToolReplacement,
  ToolReplacements,
  ToolReplacementsByStation,
} from "../../cell-status/tool-replacements.js";
import {
  Column,
  copyTableToClipboard,
  DataTableActions,
  DataTableBody,
  DataTableHead,
  TablePage,
  TableZoom,
  useColSort,
  useTablePage,
  useTableZoomForPeriod,
} from "./DataTable.js";
import { LazySeq, OrderedMap, ToComparable } from "@seedtactics/immutable-collections";

type ReplacementTableProps = {
  readonly station: StationGroupAndNum | null;
};

type ToolReplacementSummary = {
  readonly tool: string;
  readonly numReplacements: number;
  readonly totalUseOfAllReplacements: number;
};

enum SummaryColumnId {
  Tool,
  NumReplacements,
  AvgUseAtReplacement,
}

const decimalFormat = Intl.NumberFormat(undefined, {
  maximumFractionDigits: 1,
});

const summaryColumns: ReadonlyArray<Column<SummaryColumnId, ToolReplacementSummary>> = [
  {
    id: SummaryColumnId.Tool,
    numeric: false,
    label: "Tool",
    getDisplay: (c) => c.tool,
  },
  {
    id: SummaryColumnId.NumReplacements,
    numeric: true,
    label: "Num Replacements",
    getDisplay: (c) => c.numReplacements.toString(),
    getForSort: (c) => c.numReplacements,
  },
  {
    id: SummaryColumnId.AvgUseAtReplacement,
    numeric: true,
    label: "Avg Use (min) at Replacement",
    getDisplay: (c) => decimalFormat.format(c.totalUseOfAllReplacements / c.numReplacements),
    getForSort: (c) => c.totalUseOfAllReplacements / c.numReplacements,
  },
  // TODO: show graph of all replacements
];

type SummaryPageData = {
  readonly pageData: ReadonlyArray<ToolReplacementSummary>;
  readonly totalDataLength: number;
};

function create_summary_data(
  tpage: TablePage | undefined,
  zoom: TableZoom | undefined,
  allReplacements: ToolReplacementsByStation,
  station: StationGroupAndNum | null | undefined,
  sortOn: ToComparable<ToolReplacementSummary>
): SummaryPageData {
  const zoomRange = zoom?.zoomRange;
  let allData: LazySeq<ToolReplacements>;
  if (station) {
    const rsForStat = allReplacements.get(station) ?? OrderedMap.empty();
    allData = rsForStat.valuesToAscLazySeq();
  } else {
    allData = allReplacements.valuesToAscLazySeq().flatMap((rsByStat) => rsByStat.valuesToAscLazySeq());
  }
  const allSorted = allData
    .transform((x) =>
      zoomRange ? x.filter((rs) => rs.time >= zoomRange.start && rs.time <= zoomRange.end) : x
    )
    .flatMap((rs) => rs.replacements)
    .aggregate<string, ToolReplacementSummary>(
      (r) => r.tool,
      (r) => ({
        tool: r.tool,
        numReplacements: 1,
        totalUseOfAllReplacements:
          r.type === "ReplaceBeforeCycleStart" ? r.useAtReplacement : r.totalUseAtBeginningOfCycle,
      }),
      (a, b) => ({
        tool: a.tool,
        numReplacements: a.numReplacements + b.numReplacements,
        totalUseOfAllReplacements: a.totalUseOfAllReplacements + b.totalUseOfAllReplacements,
      })
    )
    .map(([, summary]) => summary)
    .toSortedArray(sortOn);
  const pageData = tpage
    ? allSorted.slice(tpage.page * tpage.rowsPerPage, (tpage.page + 1) * tpage.rowsPerPage)
    : allSorted;
  return { pageData, totalDataLength: allSorted.length };
}

const SummaryTable = React.memo(function ReplacementTable(props: ReplacementTableProps) {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const tpage = useTablePage();
  const zoom = useTableZoomForPeriod(period);
  const sort = useColSort(SummaryColumnId.Tool, summaryColumns);

  const allReplacements = useRecoilValue(
    period.type === "Last30" ? last30ToolReplacements : specificMonthToolReplacements
  );
  const { pageData, totalDataLength } = create_summary_data(
    tpage,
    zoom,
    allReplacements,
    props.station,
    sort.sortOn
  );

  return (
    <div>
      <Table>
        <DataTableHead columns={summaryColumns} sort={sort} showDetailsCol={false} />
        <DataTableBody columns={summaryColumns} pageData={pageData} />
      </Table>
      <DataTableActions tpage={tpage} zoom={zoom.zoom} count={totalDataLength} />
    </div>
  );
});

enum AllReplacementColumnId {
  Date,
  Station,
  Tool,
  Pocket,
  Between,
  UseAtReplacement,
  UseAtEndOfCycle,
}

type ToolReplacementAndStationDate = ToolReplacement & {
  readonly time: Date;
  readonly station: StationGroupAndNum;
};

const allReplacementsColumns: ReadonlyArray<Column<AllReplacementColumnId, ToolReplacementAndStationDate>> = [
  {
    id: AllReplacementColumnId.Date,
    numeric: false,
    label: "Date",
    getDisplay: (c) => c.time.toLocaleString(),
    getForSort: (c) => c.time.getTime(),
    getForExport: (c) => c.time.toISOString(),
  },
  {
    id: AllReplacementColumnId.Station,
    numeric: false,
    label: "Machine",
    getDisplay: (c) => c.station.group + " #" + c.station.num.toString(),
    getForSort: (c) => c.station,
  },
  {
    id: AllReplacementColumnId.Tool,
    numeric: false,
    label: "Tool",
    getDisplay: (c) => c.tool,
  },
  {
    id: AllReplacementColumnId.Pocket,
    numeric: true,
    label: "Pocket",
    getDisplay: (c) => (c.pocket === -1 ? "" : c.pocket.toString()),
    getForSort: (c) => c.pocket,
  },
  {
    id: AllReplacementColumnId.Between,
    numeric: false,
    label: "Type",
    getDisplay: (c) => (c.type === "ReplaceBeforeCycleStart" ? "Between Cycles" : "During Cycle"),
  },
  {
    id: AllReplacementColumnId.UseAtReplacement,
    numeric: true,
    label: "Use At Replacement / Start of Cycle (min)",
    getDisplay: (c) =>
      decimalFormat.format(
        c.type === "ReplaceBeforeCycleStart" ? c.useAtReplacement : c.totalUseAtBeginningOfCycle
      ),
    getForSort: (c) =>
      c.type === "ReplaceBeforeCycleStart" ? c.useAtReplacement : c.totalUseAtBeginningOfCycle,
  },
  {
    id: AllReplacementColumnId.UseAtEndOfCycle,
    numeric: true,
    label: "Use At End of Cycle (min)",
    getDisplay: (c) =>
      c.type === "ReplaceBeforeCycleStart" ? "" : decimalFormat.format(c.totalUseAtEndOfCycle),
    getForSort: (c) => (c.type === "ReplaceBeforeCycleStart" ? 0 : c.totalUseAtEndOfCycle),
  },
];

type AllPageData = {
  readonly pageData: ReadonlyArray<ToolReplacementAndStationDate>;
  readonly totalDataLength: number;
};

function calculate_all_data(
  tpage: TablePage | undefined,
  zoom: TableZoom | undefined,
  allReplacements: ToolReplacementsByStation,
  station: StationGroupAndNum | null | undefined,
  sortOn: ToComparable<ToolReplacementAndStationDate>
): AllPageData {
  const zoomRange = zoom?.zoomRange;
  let allData: LazySeq<ToolReplacementAndStationDate>;
  if (station) {
    const rsForStat = allReplacements.get(station) ?? OrderedMap.empty();
    allData = rsForStat
      .valuesToAscLazySeq()
      .transform((x) =>
        zoomRange ? x.filter((rs) => rs.time >= zoomRange.start && rs.time <= zoomRange.end) : x
      )
      .flatMap((rs) => rs.replacements.map((r) => ({ ...r, station, time: rs.time })));
  } else {
    allData = allReplacements.toAscLazySeq().flatMap(([station, rsByStat]) =>
      rsByStat
        .valuesToAscLazySeq()
        .transform((x) =>
          zoomRange ? x.filter((rs) => rs.time >= zoomRange.start && rs.time <= zoomRange.end) : x
        )
        .flatMap((rs) => rs.replacements.map((r) => ({ ...r, station, time: rs.time })))
    );
  }
  const allSorted = allData.toSortedArray(sortOn);
  const pageData = tpage
    ? allSorted.slice(tpage.page * tpage.rowsPerPage, (tpage.page + 1) * tpage.rowsPerPage)
    : allSorted;
  return { pageData, totalDataLength: allSorted.length };
}

const AllReplacementTable = React.memo(function ReplacementTable(props: ReplacementTableProps) {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const tpage = useTablePage();
  const zoom = useTableZoomForPeriod(period);
  const sort = useColSort(AllReplacementColumnId.Date, allReplacementsColumns);

  const allReplacements = useRecoilValue(
    period.type === "Last30" ? last30ToolReplacements : specificMonthToolReplacements
  );
  const { pageData, totalDataLength } = calculate_all_data(
    tpage,
    zoom,
    allReplacements,
    props.station,
    sort.sortOn
  );

  return (
    <div>
      <Table>
        <DataTableHead columns={allReplacementsColumns} sort={sort} showDetailsCol={false} />
        <DataTableBody columns={allReplacementsColumns} pageData={pageData} />
      </Table>
      <DataTableActions tpage={tpage} zoom={zoom.zoom} count={totalDataLength} />
    </div>
  );
});

function copyToClipboard(replacements: ToolReplacementsByStation, displayType: "summary" | "details"): void {
  if (displayType === "summary") {
    const { pageData } = create_summary_data(undefined, undefined, replacements, undefined, (r) => r.tool);

    copyTableToClipboard(summaryColumns, pageData);
  } else {
    const { pageData } = calculate_all_data(undefined, undefined, replacements, undefined, (r) => r.tool);

    copyTableToClipboard(allReplacementsColumns, pageData);
  }
}

const ChooseMachine = React.memo(function ChooseMachineSelect(props: {
  readonly station: StationGroupAndNum | null;
  readonly setSelectedStation: (station: StationGroupAndNum | null) => void;
  readonly displayType: "summary" | "details";
}) {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const replacements = useRecoilValue(
    period.type === "Last30" ? last30ToolReplacements : specificMonthToolReplacements
  );
  const machines = Array.from(replacements.keys());
  const selMachineIdx = props.station !== null ? machines.indexOf(props.station) : -1;
  return (
    <>
      <Tooltip title="Copy to Clipboard">
        <IconButton
          onClick={() => copyToClipboard(replacements, props.displayType)}
          style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
          size="large"
        >
          <ImportExport />
        </IconButton>
      </Tooltip>
      <Select
        autoWidth
        value={selMachineIdx}
        style={{ marginLeft: "1em" }}
        onChange={(e) => {
          const v = e.target.value as number;
          if (v === -1) {
            props.setSelectedStation(null);
          } else {
            props.setSelectedStation(machines[v]);
          }
        }}
      >
        <MenuItem value={-1}>
          <em>Any Machine</em>
        </MenuItem>
        {machines.map((n, idx) => (
          <MenuItem key={idx} value={idx}>
            <div style={{ display: "flex", alignItems: "center" }}>
              <span style={{ marginRight: "1em" }}>
                {n.group} #{n.num}
              </span>
            </div>
          </MenuItem>
        ))}
      </Select>
    </>
  );
});

export const ToolReplacementCard = React.memo(function ToolReplacementCard() {
  const [selectedMachine, setSelectedMachine] = React.useState<StationGroupAndNum | null>(null);
  const [type, setType] = React.useState<"summary" | "details">("summary");

  return (
    <Card raised>
      <CardHeader
        title={
          <div
            style={{
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
            }}
          >
            <ToolIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Tool Replacements</div>
            <div style={{ flexGrow: 1 }} />
            <ChooseMachine
              station={selectedMachine}
              setSelectedStation={setSelectedMachine}
              displayType={type}
            />
            <Select
              autoWidth
              value={type}
              style={{ marginLeft: "1em" }}
              onChange={(e) => setType(e.target.value as "summary" | "details")}
            >
              <MenuItem value="summary">Summary</MenuItem>
              <MenuItem value="details">Details</MenuItem>
            </Select>
          </div>
        }
      />
      <CardContent>
        {type === "summary" ? (
          <SummaryTable station={selectedMachine} />
        ) : (
          <AllReplacementTable station={selectedMachine} />
        )}
      </CardContent>
    </Card>
  );
});
