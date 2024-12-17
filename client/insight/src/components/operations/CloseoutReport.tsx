/* Copyright (c) 2024, John Lenz

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

import { memo, ReactNode, useMemo, useState } from "react";
import { addDays, startOfToday } from "date-fns";
import {
  Box,
  IconButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TableSortLabel,
  Tooltip,
  Typography,
} from "@mui/material";
import { Button } from "@mui/material";

import { MaterialDialog, PartIdenticon } from "../station-monitor/Material.js";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "../station-monitor/SelectWorkorder.js";
import {
  materialDialogOpen,
  materialInDialogInfo,
  useCompleteCloseout,
} from "../../cell-status/material-details.js";
import {
  last30MaterialSummary,
  MaterialSummaryAndCompletedData,
} from "../../cell-status/material-summary.js";
import { useSetTitle } from "../routes.js";
import { useAtomValue, useSetAtom } from "jotai";
import { LazySeq, ToComparableBase } from "@seedtactics/immutable-collections";
import { Check, ErrorOutline, MoreHoriz, SkipNext, SkipPrevious } from "@mui/icons-material";

function CloseoutDialogButtons() {
  const setWorkorderDialogOpen = useSetAtom(selectWorkorderDialogOpen);
  const mat = useAtomValue(materialInDialogInfo);
  const [complete, isCompleting] = useCompleteCloseout();
  const setToShow = useSetAtom(materialDialogOpen);

  if (mat === null || mat.materialID < 0) {
    return null;
  }

  function closeout(failed: boolean) {
    if (mat === null) return;
    complete({
      mat,
      operator: "Manager",
      failed,
    });
    setToShow(null);
  }

  return (
    <>
      <Button color="primary" disabled={isCompleting} onClick={() => closeout(true)}>
        Fail CloseOut
      </Button>
      <Button color="primary" disabled={isCompleting} onClick={() => closeout(false)}>
        Pass CloseOut
      </Button>
      <Button color="primary" onClick={() => setWorkorderDialogOpen(true)}>
        Change Workorder
      </Button>
    </>
  );
}

const CloseoutMaterialDialog = memo(function CloseoutDialog() {
  return <MaterialDialog buttons={<CloseoutDialogButtons />} />;
});

const fulldayFormat = new Intl.DateTimeFormat(undefined, {
  weekday: "long",
  month: "long",
  day: "numeric",
  year: "numeric",
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
        <div>
          <IconButton disabled={day <= addDays(today, -28)} onClick={() => setDay((d) => addDays(d, -1))}>
            <SkipPrevious />
          </IconButton>
        </div>
      </Tooltip>
      <Typography sx={{ minWidth: "14em", textAlign: "center" }}>{fulldayFormat.format(day)}</Typography>
      <Tooltip title="Next Day">
        <div>
          <IconButton disabled={day >= today} onClick={() => setDay((d) => addDays(d, 1))}>
            <SkipNext />
          </IconButton>
        </div>
      </Tooltip>
    </Stack>
  );
}

enum SortColumn {
  CompletedTime,
  Serial,
  Part,
  Workorder,
  Closeout,
  CloseoutTime,
}

function sortMats(
  mats: ReadonlyArray<MaterialSummaryAndCompletedData>,
  sortBy: SortColumn,
  order: "asc" | "desc",
): ReadonlyArray<MaterialSummaryAndCompletedData> {
  let sortCol: ToComparableBase<MaterialSummaryAndCompletedData>;
  switch (sortBy) {
    case SortColumn.CompletedTime:
      sortCol = (j) => j.last_unload_time ?? null;
      break;
    case SortColumn.Part:
      sortCol = (j) => j.partName;
      break;
    case SortColumn.Serial:
      sortCol = (j) => j.serial ?? null;
      break;
    case SortColumn.Workorder:
      sortCol = (j) => j.workorderId ?? null;
      break;
    case SortColumn.Closeout:
      sortCol = (j) => j.closeout_failed ?? null;
      break;
    case SortColumn.CloseoutTime:
      sortCol = (j) => j.closeout_completed ?? null;
  }
  return LazySeq.of(mats).toSortedArray(order === "asc" ? { asc: sortCol } : { desc: sortCol }, {
    asc: (j) => j.last_unload_time ?? null,
  });
}

function CloseoutHeader({
  sortBy,
  setSortBy,
  sortOrder,
  setSortOrder,
}: {
  sortBy: SortColumn;
  setSortBy: (col: SortColumn) => void;
  sortOrder: "asc" | "desc";
  setSortOrder: (order: "asc" | "desc") => void;
}) {
  function toggleSort(col: SortColumn) {
    if (col === sortBy) {
      setSortOrder(sortOrder === "asc" ? "desc" : "asc");
    } else {
      setSortBy(col);
      setSortOrder("asc");
    }
  }

  return (
    <TableRow>
      <TableCell sortDirection={sortBy === SortColumn.CompletedTime ? sortOrder : false}>
        <TableSortLabel
          active={sortBy === SortColumn.CompletedTime}
          direction={sortOrder}
          onClick={() => toggleSort(SortColumn.CompletedTime)}
        >
          Completed Time
        </TableSortLabel>
      </TableCell>
      <TableCell sortDirection={sortBy === SortColumn.Serial ? sortOrder : false}>
        <TableSortLabel
          active={sortBy === SortColumn.Serial}
          direction={sortOrder}
          onClick={() => toggleSort(SortColumn.Serial)}
        >
          Serial
        </TableSortLabel>
      </TableCell>
      <TableCell sortDirection={sortBy === SortColumn.Part ? sortOrder : false}>
        <TableSortLabel
          active={sortBy === SortColumn.Part}
          direction={sortOrder}
          onClick={() => toggleSort(SortColumn.Part)}
        >
          Part
        </TableSortLabel>
      </TableCell>
      <TableCell sortDirection={sortBy === SortColumn.Workorder ? sortOrder : false}>
        <TableSortLabel
          active={sortBy === SortColumn.Workorder}
          direction={sortOrder}
          onClick={() => toggleSort(SortColumn.Workorder)}
        >
          Workorder
        </TableSortLabel>
      </TableCell>
      <TableCell sortDirection={sortBy === SortColumn.Closeout ? sortOrder : false}>
        <TableSortLabel
          active={sortBy === SortColumn.Closeout}
          direction={sortOrder}
          onClick={() => toggleSort(SortColumn.Closeout)}
        >
          Closeout
        </TableSortLabel>
      </TableCell>
      <TableCell sortDirection={sortBy === SortColumn.CloseoutTime ? sortOrder : false}>
        <TableSortLabel
          active={sortBy === SortColumn.CloseoutTime}
          direction={sortOrder}
          onClick={() => toggleSort(SortColumn.CloseoutTime)}
        >
          Closeout Time
        </TableSortLabel>
      </TableCell>
      <TableCell />
    </TableRow>
  );
}

const timeOnlyFormat = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "numeric",
  second: "numeric",
});

const noSecondsFormat = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "numeric",
  day: "numeric",
  hour: "numeric",
  minute: "numeric",
});

const CloseoutRow = memo(function CloseoutRow({ mat }: { mat: MaterialSummaryAndCompletedData }) {
  const setMatToShow = useSetAtom(materialDialogOpen);
  return (
    <TableRow>
      <TableCell>{mat.last_unload_time ? timeOnlyFormat.format(mat.last_unload_time) : ""}</TableCell>
      <TableCell>{mat.serial}</TableCell>
      <TableCell>
        <Stack direction="row" spacing="2" alignItems="center">
          <PartIdenticon part={mat.partName} size={25} />
          <Typography variant="body2">{mat.partName}</Typography>
        </Stack>
      </TableCell>
      <TableCell>{mat.workorderId}</TableCell>
      <TableCell>
        {mat.closeout_completed === null || mat.closeout_completed === undefined ? (
          ""
        ) : mat.closeout_failed ? (
          <ErrorOutline fontSize="inherit" />
        ) : (
          <Check fontSize="inherit" />
        )}
      </TableCell>
      <TableCell>{mat.closeout_completed ? noSecondsFormat.format(mat.closeout_completed) : ""}</TableCell>
      <TableCell padding="checkbox">
        {mat.serial !== null && mat.serial !== "" ? (
          <IconButton
            onClick={() => {
              if (!mat.serial) return;
              setMatToShow({
                type: "ManuallyEnteredSerial",
                serial: mat.serial,
              });
            }}
            size="large"
          >
            <MoreHoriz fontSize="inherit" />
          </IconButton>
        ) : undefined}
      </TableCell>
    </TableRow>
  );
});

function CloseoutTable({ day }: { day: Date }): ReactNode {
  const matSummary = useAtomValue(last30MaterialSummary);

  const material: ReadonlyArray<MaterialSummaryAndCompletedData> = useMemo(() => {
    const endT = addDays(day, 1);
    const uncompleted: Array<MaterialSummaryAndCompletedData> = [];
    for (const m of matSummary.matsById.values()) {
      if (
        m.completed_last_proc_machining === true &&
        m.last_unload_time &&
        m.last_unload_time >= day &&
        m.last_unload_time < endT
      ) {
        uncompleted.push(m);
      }
    }
    return uncompleted;
  }, [matSummary, day]);

  const [sortBy, setSortBy] = useState(SortColumn.CompletedTime);
  const [sortOrder, setSortOrder] = useState<"asc" | "desc">("asc");

  const sortedMats = useMemo(() => sortMats(material, sortBy, sortOrder), [material, sortBy, sortOrder]);

  return (
    <Table stickyHeader>
      <TableHead>
        <CloseoutHeader
          sortBy={sortBy}
          setSortBy={setSortBy}
          sortOrder={sortOrder}
          setSortOrder={setSortOrder}
        />
      </TableHead>
      <TableBody>
        {sortedMats.map((mat) => (
          <CloseoutRow key={mat.materialID} mat={mat} />
        ))}
      </TableBody>
    </Table>
  );
}

export function CloseoutReport(): ReactNode {
  useSetTitle("Close Out");
  const [day, setDay] = useState(startOfToday());

  return (
    <Box padding="24px">
      <Box component="nav" display="flex" justifyContent="center">
        <NavigateDay day={day} setDay={setDay} />
      </Box>
      <main>
        <CloseoutTable day={day} />
      </main>
      <SelectWorkorderDialog />
      <CloseoutMaterialDialog />
    </Box>
  );
}
