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
import {
  Card,
  CardContent,
  CardHeader,
  IconButton,
  MenuItem,
  Select,
  Stack,
  Table,
  Tooltip,
} from "@mui/material";
import { ImportExport, Dns as ToolIcon } from "@mui/icons-material";
import * as React from "react";
import { useRecoilValue } from "recoil";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import {
  last30ToolReplacements,
  specificMonthToolReplacements,
  StationGroupAndNum,
  ToolReplacement,
  ToolReplacementsByStation,
} from "../../cell-status/tool-replacements.js";
import {
  Column,
  copyTableToClipboard,
  DataTableActions,
  DataTableBody,
  DataTableHead,
  TableZoom,
  useColSort,
  useTablePage,
  useTableZoomForPeriod,
} from "./DataTable.js";
import { LazySeq, OrderedMap, ToComparable } from "@seedtactics/immutable-collections";
import { scaleLinear, scaleTime } from "@visx/scale";
import { Circle } from "@visx/shape";
import { addDays, addMonths, startOfToday } from "date-fns";
import { ChartTooltip } from "../ChartTooltip.js";
import { localPoint } from "@visx/event";

type ReplacementTableProps = {
  readonly station: StationGroupAndNum | null;
};

type ToolReplacementAndStationDate = ToolReplacement & {
  readonly time: Date;
  readonly station: StationGroupAndNum;
};

type ToolReplacementSummary = {
  readonly tool: string;
  readonly numReplacements: number;
  readonly totalUseOfAllReplacements: number;
  readonly maxUseOfAnyReplacement: number;
  readonly replacements: ReadonlyArray<ToolReplacementAndStationDate>;
};

function tool_replacements_with_station_and_date(
  zoom: TableZoom | undefined,
  allReplacements: ToolReplacementsByStation,
  station: StationGroupAndNum | null | undefined
): LazySeq<ToolReplacementAndStationDate> {
  const zoomRange = zoom?.zoomRange;
  if (station) {
    const rsForStat = allReplacements.get(station) ?? OrderedMap.empty();
    return rsForStat
      .valuesToAscLazySeq()
      .transform((x) =>
        zoomRange ? x.filter((rs) => rs.time >= zoomRange.start && rs.time <= zoomRange.end) : x
      )
      .flatMap((rs) => rs.replacements.map((r) => ({ ...r, station, time: rs.time })));
  } else {
    return allReplacements.toAscLazySeq().flatMap(([station, rsByStat]) =>
      rsByStat
        .valuesToAscLazySeq()
        .transform((x) =>
          zoomRange ? x.filter((rs) => rs.time >= zoomRange.start && rs.time <= zoomRange.end) : x
        )
        .flatMap((rs) => rs.replacements.map((r) => ({ ...r, station, time: rs.time })))
    );
  }
}

function tool_summary(
  zoom: TableZoom | undefined,
  allReplacements: ToolReplacementsByStation,
  station: StationGroupAndNum | null | undefined,
  sortOn: ToComparable<ToolReplacementSummary>
): ReadonlyArray<ToolReplacementSummary> {
  return tool_replacements_with_station_and_date(zoom, allReplacements, station)
    .groupBy((r) => r.tool)
    .map(([tool, replacements]) => {
      let totalUse = 0;
      let maxUse = null;
      for (const r of replacements) {
        const u = r.type === "ReplaceBeforeCycleStart" ? r.useAtReplacement : r.totalUseAtBeginningOfCycle;
        if (maxUse === null || u > maxUse) {
          maxUse = u;
        }
        totalUse += u;
      }
      return {
        tool,
        numReplacements: replacements.length,
        totalUseOfAllReplacements: totalUse,
        maxUseOfAnyReplacement: maxUse ?? 0,
        replacements,
      };
    })
    .toSortedArray(sortOn);
}

enum SummaryColumnId {
  Tool,
  NumReplacements,
  AvgUseAtReplacement,
  Graph,
}

const decimalFormat = Intl.NumberFormat(undefined, {
  maximumFractionDigits: 1,
});

const CurZoomContext = React.createContext<{ readonly start: Date; readonly end: Date }>({
  start: new Date(),
  end: new Date(),
});

const ReplacementGraph = React.memo(function ReplacementGraph({
  row,
}: {
  readonly row: ToolReplacementSummary;
}) {
  const zoom = React.useContext(CurZoomContext);
  const [tooltip, setTooltip] = React.useState<{
    readonly left: number;
    readonly r: ToolReplacementAndStationDate;
  } | null>(null);

  const timeScale = scaleTime({
    domain: [zoom.start, zoom.end],
    range: [0, 1000],
  });

  const yScale = scaleLinear({
    domain: [0, row.maxUseOfAnyReplacement],
    range: [33, 3],
  });

  const avgUse =
    row.numReplacements === 0 ? null : yScale(row.totalUseOfAllReplacements / row.numReplacements);

  return (
    <div style={{ position: "relative" }}>
      <svg
        height="35"
        width="100%"
        viewBox="0 0 1000 35"
        preserveAspectRatio="none"
        onMouseLeave={() => setTooltip(null)}
      >
        {avgUse !== null ? (
          <line x1="0" y1={avgUse} x2="1000" y2={avgUse} stroke="red" strokeWidth={0.5} />
        ) : undefined}
        {row.replacements.map((r, i) => (
          <Circle
            key={i}
            cx={timeScale(r.time)}
            cy={yScale(
              r.type === "ReplaceBeforeCycleStart" ? r.useAtReplacement : r.totalUseAtBeginningOfCycle
            )}
            r={r === tooltip?.r ? 3 : 1}
            onMouseEnter={(e) => setTooltip({ left: localPoint(e)?.x ?? 0, r })}
            fill="black"
          />
        ))}
      </svg>
      {tooltip !== null ? (
        <ChartTooltip style={{ left: tooltip.left, top: 0 }}>
          <Stack spacing={0.5}>
            <div>Tool: {tooltip.r.tool}</div>
            <div>Pocket: {tooltip.r.pocket}</div>
            <div>Time: {tooltip.r.time.toLocaleString()}</div>
            <div>
              Station: {tooltip.r.station.group} #{tooltip.r.station.num}
            </div>
            {tooltip.r.type === "ReplaceBeforeCycleStart" ? (
              <div>Mins at replacement: {decimalFormat.format(tooltip.r.useAtReplacement)}</div>
            ) : (
              <>
                <div>
                  Mins at beginning of cycle: {decimalFormat.format(tooltip.r.totalUseAtBeginningOfCycle)}
                </div>
                <div>Mins at end of cycle: {decimalFormat.format(tooltip.r.totalUseAtBeginningOfCycle)}</div>
              </>
            )}
          </Stack>
        </ChartTooltip>
      ) : undefined}
    </div>
  );
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
  {
    id: SummaryColumnId.Graph,
    numeric: false,
    ignoreDuringExport: true,
    label: "All Replacements",
    getDisplay: () => "",
    ExpandedCell: ReplacementGraph,
  },
];

const SummaryTable = React.memo(function ReplacementTable(props: ReplacementTableProps) {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const tpage = useTablePage();
  const zoom = useTableZoomForPeriod(period);
  const sort = useColSort(SummaryColumnId.Tool, summaryColumns);

  const allReplacements = useRecoilValue(
    period.type === "Last30" ? last30ToolReplacements : specificMonthToolReplacements
  );
  const allSorted = React.useMemo(
    () => tool_summary(zoom, allReplacements, props.station, sort.sortOn),
    [zoom, allReplacements, props.station, sort]
  );
  const pageData = tpage
    ? allSorted.slice(tpage.page * tpage.rowsPerPage, (tpage.page + 1) * tpage.rowsPerPage)
    : allSorted;

  const zoomRange =
    zoom?.zoomRange ??
    (period.type === "Last30"
      ? { start: addDays(startOfToday(), -29), end: addDays(startOfToday(), 1) }
      : { start: period.month, end: addMonths(period.month, 1) });

  return (
    <div>
      <CurZoomContext.Provider value={zoomRange}>
        <Table>
          <DataTableHead columns={summaryColumns} sort={sort} showDetailsCol={false} />
          <DataTableBody columns={summaryColumns} pageData={pageData} rowsPerPage={tpage.rowsPerPage} />
        </Table>
        <DataTableActions tpage={tpage} zoom={zoom.zoom} count={allSorted.length} />
      </CurZoomContext.Provider>
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

const AllReplacementTable = React.memo(function ReplacementTable(props: ReplacementTableProps) {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const tpage = useTablePage();
  const zoom = useTableZoomForPeriod(period);
  const sort = useColSort(AllReplacementColumnId.Date, allReplacementsColumns);

  const allReplacements = useRecoilValue(
    period.type === "Last30" ? last30ToolReplacements : specificMonthToolReplacements
  );
  const allSorted = React.useMemo(
    () =>
      tool_replacements_with_station_and_date(zoom, allReplacements, props.station).toSortedArray(
        sort.sortOn
      ),
    [sort, zoom, allReplacements, props.station]
  );
  const pageData = tpage
    ? allSorted.slice(tpage.page * tpage.rowsPerPage, (tpage.page + 1) * tpage.rowsPerPage)
    : allSorted;

  return (
    <div>
      <Table>
        <DataTableHead columns={allReplacementsColumns} sort={sort} showDetailsCol={false} />
        <DataTableBody columns={allReplacementsColumns} pageData={pageData} />
      </Table>
      <DataTableActions tpage={tpage} zoom={zoom.zoom} count={allSorted.length} />
    </div>
  );
});

function copyToClipboard(replacements: ToolReplacementsByStation, displayType: "summary" | "details"): void {
  if (displayType === "summary") {
    const r = tool_summary(undefined, replacements, undefined, (r) => r.tool);
    copyTableToClipboard(summaryColumns, r);
  } else {
    const r = tool_replacements_with_station_and_date(undefined, replacements, undefined).toSortedArray(
      (r) => r.tool,
      (r) => r.time
    );
    copyTableToClipboard(allReplacementsColumns, r);
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
