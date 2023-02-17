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

import { Box, IconButton, Stack, Table, Tooltip, Typography } from "@mui/material";
import { addDays, startOfToday } from "date-fns";
import * as React from "react";
import {
  Column,
  copyTableToClipboard,
  DataTableBody,
  DataTableHead,
  useColSort,
} from "../analysis/DataTable.js";
import { ShiftStart, ShiftStartAndEnd, useShifts } from "./ShiftSettings.js";
import { SkipPrevious as SkipPrevIcon, SkipNext as SkipNextIcon, ImportExport } from "@mui/icons-material";
import { LazySeq, OrderedMap } from "@seedtactics/immutable-collections";
import { last30SimProduction, SimPartCompleted } from "../../cell-status/sim-production.js";
import { last30StationCycles, StationCyclesByCntr } from "../../cell-status/station-cycles.js";
import { useRecoilValue } from "recoil";
import { PartIdenticon } from "../station-monitor/Material.js";

enum ColumnId {
  Part,
  PlannedShift0,
  PlannedShift1,
  PlannedShift2,
  CompletedShift0,
  CompletedShift1,
  CompletedShift2,
}

type Row = {
  readonly part: string;
  readonly planned: ReadonlyMap<number, number>;
  readonly completed: ReadonlyMap<number, number>;
};

function decideShift<T>(
  f: (t: T) => Date,
  shifts: ReadonlyArray<ShiftStartAndEnd>
): (t: T) => { readonly val: T; readonly shift: number } | null {
  return (t) => {
    const time = f(t);
    for (let i = 0; i < shifts.length; i++) {
      const s = shifts[i];
      if (time >= s.start && time < s.end) {
        return { val: t, shift: i };
      }
    }
    return null;
  };
}

function binSimProduction(
  prod: Iterable<SimPartCompleted>,
  shifts: ReadonlyArray<ShiftStartAndEnd>
): OrderedMap<string, OrderedMap<number, number>> {
  return LazySeq.of(prod)
    .filter((p) => p.finalProcess)
    .collect(decideShift((p) => p.completeTime, shifts))
    .toLookupOrderedMap(
      (p) => p.val.partName,
      (p) => p.shift,
      (p) => p.val.quantity,
      (a, b) => a + b
    );
}

function binCompleted(
  cycles: StationCyclesByCntr,
  shifts: ReadonlyArray<ShiftStartAndEnd>
): OrderedMap<string, OrderedMap<number, number>> {
  return cycles
    .valuesToLazySeq()
    .filter(
      (cycle) =>
        cycle.isLabor &&
        cycle.operation === "UNLOAD" &&
        LazySeq.of(cycle.material).anyMatch((m) => m.proc === m.numproc)
    )
    .collect(decideShift((c) => c.x, shifts))
    .toLookupOrderedMap(
      (p) => p.val.part,
      (p) => p.shift,
      (p) => LazySeq.of(p.val.material).sumBy((m) => (m.proc === m.numproc ? 1 : 0)),
      (a, b) => a + b
    );
}

function useRows(day: Date): ReadonlyArray<Row> {
  const cycles = useRecoilValue(last30StationCycles);
  const sim = useRecoilValue(last30SimProduction);
  const shifts = useShifts(day);
  return React.useMemo(() => {
    const planned = binSimProduction(sim, shifts);
    const completed = binCompleted(cycles, shifts);

    return planned
      .mapValues<{ planned?: OrderedMap<number, number>; completed?: OrderedMap<number, number> }>((p) => ({
        planned: p,
      }))
      .adjust(completed, (plan, comp) => ({ ...(plan ?? {}), completed: comp }))
      .toAscLazySeq()
      .map(([partName, { planned, completed }]) => ({
        part: partName,
        planned: planned ?? OrderedMap.empty(),
        completed: completed ?? OrderedMap.empty(),
      }))
      .toRArray();
  }, [cycles, sim, shifts]);
}

const fulldayFormat = new Intl.DateTimeFormat(undefined, {
  weekday: "long",
  month: "long",
  day: "numeric",
  year: "numeric",
});

function PartCell({ row }: { row: Row }) {
  return (
    <Stack direction="row" spacing={1} alignItems="center">
      <PartIdenticon part={row.part} size={25} />
      <span>{row.part}</span>
    </Stack>
  );
}

function useColumns(day: Date): ReadonlyArray<Column<ColumnId, Row>> {
  const numShifts = useShifts(day).length;

  return React.useMemo(() => {
    const cols: Array<Column<ColumnId, Row>> = [
      {
        id: ColumnId.Part,
        numeric: false,
        label: "Part",
        getDisplay: (c) => c.part,
        Cell: PartCell,
      },
      {
        id: ColumnId.PlannedShift0,
        numeric: true,
        label: `Shift 1 Planned`,
        getDisplay: (c) => c.planned.get(0)?.toString() ?? "",
        getForSort: (c) => c.planned.get(0) ?? 0,
      },
      {
        id: ColumnId.CompletedShift0,
        numeric: true,
        label: `Shift 1 Completed`,
        getDisplay: (c) => c.completed.get(0)?.toString() ?? "",
        getForSort: (c) => c.completed.get(0) ?? 0,
      },
    ];

    if (numShifts >= 2) {
      cols.push({
        id: ColumnId.PlannedShift1,
        numeric: true,
        label: `Shift 2 Planned`,
        getDisplay: (c) => c.planned.get(1)?.toString() ?? "",
        getForSort: (c) => c.planned.get(1) ?? 0,
      });
      cols.push({
        id: ColumnId.CompletedShift1,
        numeric: true,
        label: `Shift 2 Completed`,
        getDisplay: (c) => c.completed.get(1)?.toString() ?? "",
        getForSort: (c) => c.completed.get(1) ?? 0,
      });
    }

    if (numShifts === 3) {
      cols.push({
        id: ColumnId.PlannedShift2,
        numeric: true,
        label: `Shift 3 Planned`,
        getDisplay: (c) => c.planned.get(2)?.toString() ?? "",
        getForSort: (c) => c.planned.get(2) ?? 0,
      });

      cols.push({
        id: ColumnId.CompletedShift2,
        numeric: true,
        label: `Shift 3 Completed`,
        getDisplay: (c) => c.completed.get(2)?.toString() ?? "",
        getForSort: (c) => c.completed.get(2) ?? 0,
      });
    }
    return cols;
  }, [numShifts]);
}

const RecentProductionTable = React.memo(function RecentSchedules({
  columns,
  rows,
}: {
  readonly columns: ReadonlyArray<Column<ColumnId, Row>>;
  readonly rows: ReadonlyArray<Row>;
}): JSX.Element {
  const sort = useColSort(ColumnId.Part, columns);

  return (
    <Box sx={{ overflowX: "auto" }}>
      <Table stickyHeader>
        <DataTableHead columns={columns} sort={sort} showDetailsCol={false} />
        <DataTableBody
          columns={columns}
          pageData={LazySeq.of(rows).toSortedArray(sort.sortOn, (x) => x.part)}
        />
      </Table>
    </Box>
  );
});

function NavigateDay({
  day,
  setDay,
}: {
  readonly day: Date;
  readonly setDay: (s: (d: Date) => Date) => void;
}) {
  const today = startOfToday();
  return (
    <Stack direction="row" spacing={2} alignItems="center">
      <Tooltip title="Previous Day">
        <IconButton disabled={day <= addDays(today, -28)} onClick={() => setDay((d) => addDays(d, -1))}>
          <SkipPrevIcon />
        </IconButton>
      </Tooltip>
      <Typography sx={{ minWidth: "14em", textAlign: "center" }}>{fulldayFormat.format(day)}</Typography>
      <Tooltip title="Next Day">
        <IconButton disabled={day >= today} onClick={() => setDay((d) => addDays(d, 1))}>
          <SkipNextIcon />
        </IconButton>
      </Tooltip>
    </Stack>
  );
}

const mdGridTemplate = '"shifts day export" auto / 1fr 1fr 1fr';
const xsGridTemplate = `"shifts export" auto
"day day" auto / 1fr auto`;

const RecentProductionToolbar = function RecentProductionToolbar({
  day,
  setDay,
  columns,
  rows,
}: {
  readonly day: Date;
  readonly setDay: (s: (d: Date) => Date) => void;
  readonly columns: ReadonlyArray<Column<ColumnId, Row>>;
  readonly rows: ReadonlyArray<Row>;
}): JSX.Element {
  return (
    <Box
      component="nav"
      sx={{
        display: "grid",
        gridTemplate: {
          md: mdGridTemplate,
          xs: xsGridTemplate,
        },
        backgroundColor: "#E0E0E0",
        paddingLeft: "24px",
        paddingRight: "24px",
        paddingTop: "4px",
        paddingBottom: "4px",
        minHeight: "2.5em",
        alignItems: "center",
        width: "100%",
      }}
    >
      <Box gridArea="shifts" justifySelf="flex-start">
        <ShiftStart />
      </Box>
      <Box gridArea="day" justifySelf="center">
        <NavigateDay day={day} setDay={setDay} />
      </Box>
      <Box gridArea="export" justifySelf="flex-end">
        <Tooltip title="Copy to Clipboard">
          <IconButton onClick={() => copyTableToClipboard(columns, rows)}>
            <ImportExport />
          </IconButton>
        </Tooltip>
      </Box>
    </Box>
  );
};

export function RecentProductionPage(): JSX.Element {
  const [day, setDay] = React.useState<Date>(startOfToday);
  React.useEffect(() => {
    document.title = "Recent Completed Parts - FMS Insight";
  }, []);
  const columns = useColumns(day);
  const rows = useRows(day);
  return (
    <>
      <RecentProductionToolbar day={day} setDay={setDay} rows={rows} columns={columns} />
      <main style={{ padding: "24px" }}>
        <RecentProductionTable rows={rows} columns={columns} />
      </main>
    </>
  );
}
