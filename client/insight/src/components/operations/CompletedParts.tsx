/* Copyright (c) 2025, John Lenz

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

import { ReactNode, memo, useMemo, useState } from "react";
import {
  Box,
  Button,
  Stack,
  TableSortLabel,
  IconButton,
  Tooltip,
  Typography,
  Table,
  TableRow,
  TableCell,
  TableHead,
  TableBody,
  Toolbar,
  Select,
  MenuItem,
  InputBase,
  styled,
} from "@mui/material";
import { MaterialDialog, PartIdenticon } from "../station-monitor/Material.js";
import {
  FirstPage as FirstPageIcon,
  KeyboardArrowLeft,
  KeyboardArrowRight,
  LastPage as LastPageIcon,
  KeyboardArrowDown as KeyboardArrowDownIcon,
  KeyboardArrowUp as KeyboardArrowUpIcon,
  ImportExport,
  MoreHoriz,
  Check,
  ErrorOutline,
  SavedSearch,
} from "@mui/icons-material";
import { Collapse } from "@mui/material";
import { LazySeq, mkCompareByProperties, ToComparableBase } from "@seedtactics/immutable-collections";
import { AppLink, RouteLocation, useSetTitle } from "../routes.js";
import {
  materialDialogOpen,
  materialInDialogInfo,
  useCompleteCloseout,
} from "../../cell-status/material-details.js";
import copy from "copy-to-clipboard";
import { Atom, useAtom, useAtomValue, useSetAtom } from "jotai";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "../station-monitor/SelectWorkorder.js";
import {
  last30PartSummary,
  last30PartSummaryRange,
  last30PartSummaryRangeStart,
  PartSummary,
} from "../../data/part-summary.js";
import { MaterialSummaryAndCompletedData } from "../../cell-status/material-summary.js";

type PartSummaryAtom = Atom<ReadonlyArray<PartSummary>>;

const numFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 });

const partColCount = 6;
type SortColumn = "PartName" | "CompletedQty" | "AbnormalQty" | "Workorders" | "Elapsed" | "Active";

function sortPartSummary(
  parts: ReadonlyArray<PartSummary>,
  col: SortColumn,
  order: "asc" | "desc",
): ReadonlyArray<PartSummary> {
  let sortCol: ToComparableBase<PartSummary>;
  switch (col) {
    case "PartName":
      sortCol = (j) => j.part;
      break;
    case "CompletedQty":
      sortCol = (j) => j.completedQty;
      break;
    case "AbnormalQty":
      sortCol = (j) => j.abnormalQty;
      break;
    case "Workorders":
      sortCol = (j) => j.workorders.lookupMin()?.[0] ?? null;
      break;
    case "Elapsed":
      sortCol = (j) =>
        j.stationMins
          .toAscLazySeq()
          .filter(([stat, _]) => stat !== "L/U")
          .sumBy(([, t]) => t.elapsed);
      break;
    case "Active":
      sortCol = (j) =>
        j.stationMins
          .toAscLazySeq()
          .filter(([stat, _]) => stat !== "L/U")
          .sumBy(([_, t]) => t.active);
      break;
  }
  const sorted = [...parts];
  sorted.sort(mkCompareByProperties(order === "asc" ? { asc: sortCol } : { desc: sortCol }));
  return sorted;
}

type MatSortCol = "Serial" | "CompletedDate" | "Workorder" | "Quarantined" | "InspectFailed" | "CloseOut";

const completedDateFormat = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

function sortMaterial(
  material: ReadonlyArray<MaterialSummaryAndCompletedData>,
  col: MatSortCol,
  order: "asc" | "desc",
) {
  let sortCol: ToComparableBase<MaterialSummaryAndCompletedData>;
  switch (col) {
    case "Serial":
      sortCol = (j) => j.serial ?? null;
      break;
    case "CompletedDate":
      sortCol = (j) => j.last_unload_time ?? null;
      break;
    case "Workorder":
      sortCol = (j) => j.workorderId ?? null;
      break;
    case "Quarantined":
      sortCol = (j) => j.currently_quarantined ?? null;
      break;
    case "InspectFailed":
      sortCol = (j) =>
        LazySeq.ofObject(j.completedInspections ?? {}).some(([, insp]) => insp.success === false) ? -1 : 0;
      break;
    case "CloseOut":
      sortCol = (j) => (j.closeout_failed === undefined ? 1 : j.closeout_failed === true ? -1 : 0);
      break;
  }
  const sorted = [...material];
  sorted.sort(mkCompareByProperties(order === "asc" ? { asc: sortCol } : { desc: sortCol }));
  return sorted;
}

function SortColHeader<Col>(props: {
  readonly col: Col;
  readonly align?: "left" | "right";
  readonly order: "asc" | "desc";
  readonly setOrder: (o: "asc" | "desc") => void;
  readonly sortBy: Col;
  readonly setSortBy: (c: Col) => void;
  readonly children: ReactNode;
  readonly extraIcon?: ReactNode;
  readonly noWhitespaceWrap?: boolean;
}) {
  return (
    <TableCell
      align={props.align}
      sortDirection={props.sortBy === props.col ? props.order : false}
      sx={props.noWhitespaceWrap ? { whiteSpace: "nowrap" } : undefined}
    >
      <Tooltip title="Sort" enterDelay={300}>
        <TableSortLabel
          active={props.sortBy === props.col}
          direction={props.order}
          onClick={() => {
            if (props.col === props.sortBy) {
              props.setOrder(props.order === "asc" ? "desc" : "asc");
            } else {
              props.setSortBy(props.col);
              props.setOrder("asc");
            }
          }}
        >
          {props.children}
        </TableSortLabel>
      </Tooltip>
      {props.extraIcon}
    </TableCell>
  );
}

const WorkorderLink = memo(function WorkorderLink({
  workorderId,
}: {
  workorderId: string | null | undefined;
}) {
  if (!workorderId || workorderId === "") {
    return <span />;
  } else {
    return (
      <AppLink to={{ route: RouteLocation.Operations_CurrentWorkorders, workorder: workorderId }}>
        {workorderId}
      </AppLink>
    );
  }
});

function MaterialTable({ material }: { material: ReadonlyArray<MaterialSummaryAndCompletedData> }) {
  const setMatToShow = useSetAtom(materialDialogOpen);
  const [sortCol, setSortCol] = useState<MatSortCol>("Serial");
  const [order, setOrder] = useState<"asc" | "desc">("asc");
  const sortedMats = useMemo(() => sortMaterial(material, sortCol, order), [material, sortCol, order]);
  const sort = {
    sortBy: sortCol,
    setSortBy: setSortCol,
    order: order,
    setOrder: setOrder,
  };

  const [rowsPerPage, setRowsPerPage] = useState<number>(10);
  const [page, setPage] = useState<number>(0);
  const curPage = sortedMats.slice(page * rowsPerPage, (page + 1) * rowsPerPage);

  return (
    <>
      <Table size="small">
        <TableHead>
          <TableRow>
            <SortColHeader col="Serial" {...sort}>
              Serial
            </SortColHeader>
            <SortColHeader col="CompletedDate" {...sort}>
              Completed
            </SortColHeader>
            <SortColHeader col="Workorder" {...sort}>
              Workorder
            </SortColHeader>
            <SortColHeader col="Quarantined" {...sort}>
              Quarantine?
            </SortColHeader>
            <SortColHeader col="InspectFailed" noWhitespaceWrap {...sort}>
              Inspect Failed?
            </SortColHeader>
            <SortColHeader col="CloseOut" noWhitespaceWrap {...sort}>
              Close Out
            </SortColHeader>
            <TableCell padding="checkbox" />
          </TableRow>
        </TableHead>
        <TableBody>
          {curPage.map((s) => (
            <TableRow key={s.materialID}>
              <TableCell>{s.serial ?? ""}</TableCell>
              <TableCell>
                {s.last_unload_time ? completedDateFormat.format(s.last_unload_time) : ""}
              </TableCell>
              <TableCell>
                <WorkorderLink workorderId={s.workorderId} />
              </TableCell>
              <TableCell sx={{ textAlign: "center" }} padding="checkbox">
                {s.currently_quarantined ? <SavedSearch fontSize="inherit" /> : ""}
              </TableCell>
              <TableCell sx={{ textAlign: "center" }} padding="checkbox">
                {LazySeq.ofObject(s.completedInspections ?? {}).some(([, insp]) => insp.success === false) ? (
                  <ErrorOutline fontSize="inherit" />
                ) : (
                  ""
                )}
              </TableCell>
              <TableCell sx={{ textAlign: "center" }} padding="checkbox">
                {s.closeout_completed === undefined ? (
                  ""
                ) : s.closeout_failed === false ? (
                  <Check fontSize="inherit" />
                ) : (
                  <ErrorOutline fontSize="inherit" />
                )}
              </TableCell>
              <TableCell padding="checkbox">
                <IconButton
                  onClick={() =>
                    setMatToShow({
                      type: "MatSummary",
                      summary: s,
                    })
                  }
                  size="large"
                >
                  <MoreHoriz fontSize="inherit" />
                </IconButton>
              </TableCell>
            </TableRow>
          ))}
          {LazySeq.ofRange(0, rowsPerPage - curPage.length).map((i) => (
            <TableRow key={i}>
              <TableCell colSpan={7}>&nbsp;</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <Toolbar variant="dense" sx={{ mb: "0.5em" }}>
        <Typography color="textSecondary" variant="caption">
          Rows per page:
        </Typography>
        <Select
          style={{ marginLeft: 8, marginRight: "1em" }}
          value={rowsPerPage}
          SelectDisplayProps={{ style: { color: "rgba(0, 0, 0, 0.54)" } }}
          input={<InputBase />}
          onChange={(evt) => {
            const rpp = evt.target.value;
            setRowsPerPage(rpp);
            const maxPage = Math.ceil(material.length / rpp) - 1;
            if (page > maxPage) {
              setPage(maxPage);
            }
          }}
        >
          {[10, 15, 20, 50].map((rowsPerPageOption) => (
            <MenuItem key={rowsPerPageOption} value={rowsPerPageOption}>
              {rowsPerPageOption}
            </MenuItem>
          ))}
        </Select>
        <Typography color="textSecondary" variant="caption">
          {`${material.length === 0 ? 0 : page * rowsPerPage + 1}-${Math.min(
            material.length,
            (page + 1) * rowsPerPage,
          )} of ${material.length}`}
        </Typography>
        <IconButton onClick={() => setPage(0)} disabled={page === 0} aria-label="First Page" size="large">
          <FirstPageIcon />
        </IconButton>
        <IconButton
          onClick={() => setPage(page - 1)}
          disabled={page === 0}
          aria-label="Previous Page"
          size="large"
        >
          <KeyboardArrowLeft />
        </IconButton>
        <IconButton
          onClick={() => setPage(page + 1)}
          disabled={page >= Math.ceil(material.length / rowsPerPage) - 1}
          aria-label="Next Page"
          size="large"
        >
          <KeyboardArrowRight />
        </IconButton>
        <IconButton
          onClick={() => setPage(Math.max(0, Math.ceil(material.length / rowsPerPage) - 1))}
          disabled={page >= Math.ceil(material.length / rowsPerPage) - 1}
          aria-label="Last Page"
          size="large"
        >
          <LastPageIcon />
        </IconButton>
      </Toolbar>
    </>
  );
}

const PartDetails = memo(function PartDetails({ part }: { readonly part: PartSummary }) {
  return (
    <Stack direction="row" flexWrap="wrap" justifyContent="space-around" ml="1em" mr="1em">
      <div>
        <MaterialTable material={part.mats} />
      </div>
      <div>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Station</TableCell>
              <TableCell>Active Hours</TableCell>
              <TableCell>Elapsed Hours</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {part.stationMins.toAscLazySeq().map(([st, times]) => (
              <TableRow key={st}>
                <TableCell>{st}</TableCell>
                <TableCell>{numFormat.format(times.active / 60)}</TableCell>
                <TableCell>{numFormat.format(times.elapsed / 60)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </Stack>
  );
});

const PartTableRow = styled(TableRow)({
  "& > *": {
    borderBottom: "unset !important",
  },
});

const PartRow = memo(function PartRow({ part }: { readonly part: PartSummary }) {
  const [open, setOpen] = useState<boolean>(false);

  return (
    <>
      <PartTableRow>
        <TableCell>
          <Box
            sx={{
              display: "flex",
              alignItems: "center",
            }}
          >
            <Box sx={{ mr: "0.2em" }}>
              <PartIdenticon part={part.part} size={25} />
            </Box>
            <div>
              <Typography variant="body2" component="span" display="block">
                {part.part}
              </Typography>
            </div>
          </Box>
        </TableCell>
        <TableCell align="right">{part.completedQty}</TableCell>
        <TableCell align="right">{part.abnormalQty}</TableCell>
        <TableCell align="right">
          {numFormat.format(
            part.stationMins
              .toAscLazySeq()
              .filter(([stat, _]) => stat !== "L/U")
              .sumBy(([_, t]) => t.active) / 60,
          )}
        </TableCell>
        <TableCell align="right">
          {numFormat.format(
            part.stationMins
              .toAscLazySeq()
              .filter(([stat, _]) => stat !== "L/U")
              .sumBy(([, t]) => t.elapsed) / 60,
          )}
        </TableCell>
        <TableCell align="left">
          {part.workorders.size <= 1 ? (
            <WorkorderLink workorderId={part.workorders.lookupMin() ?? null} />
          ) : part.workorders.size === 2 ? (
            <span>
              <WorkorderLink workorderId={part.workorders.lookupMin()} /> &{" "}
              <WorkorderLink workorderId={part.workorders.lookupMax()} />
            </span>
          ) : (
            `${part.workorders.size} workorders`
          )}
        </TableCell>
        <TableCell>
          <Tooltip title="Show Details">
            <IconButton size="small" onClick={() => setOpen(!open)}>
              {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
            </IconButton>
          </Tooltip>
        </TableCell>
      </PartTableRow>
      <TableRow>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={partColCount + 1}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <PartDetails part={part} />
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
});

function CopyToClipboardBtn({ partsAtom }: { partsAtom: PartSummaryAtom }) {
  const parts = useAtomValue(partsAtom);
  return (
    <Tooltip title="Copy to Clipboard">
      <IconButton
        style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
        size="large"
        onClick={() => copyPartsToClipboard(parts)}
      >
        <ImportExport />
      </IconButton>
    </Tooltip>
  );
}

const PartHeader = memo(function PartHeader({
  partsAtom,
  order,
  setOrder,
  sortBy,
  setSortBy,
}: {
  readonly partsAtom: PartSummaryAtom;
  readonly order: "asc" | "desc";
  readonly setOrder: (o: "asc" | "desc") => void;
  readonly sortBy: SortColumn;
  readonly setSortBy: (c: SortColumn) => void;
}) {
  const sort = {
    sortBy: sortBy,
    setSortBy: setSortBy,
    order: order,
    setOrder: setOrder,
  };

  return (
    <TableHead>
      <TableRow>
        <SortColHeader align="left" col="PartName" {...sort}>
          Part
        </SortColHeader>
        <SortColHeader align="right" col="CompletedQty" {...sort}>
          Completed Quantity
        </SortColHeader>
        <SortColHeader align="right" col="AbnormalQty" {...sort}>
          Abnormal Quantity
        </SortColHeader>
        <SortColHeader align="right" col="Active" {...sort}>
          Active Hours
        </SortColHeader>
        <SortColHeader align="right" col="Elapsed" {...sort}>
          Elapsed Hours
        </SortColHeader>
        <SortColHeader align="left" col="Workorders" {...sort}>
          Workorders
        </SortColHeader>
        <TableCell>
          <CopyToClipboardBtn partsAtom={partsAtom} />
        </TableCell>
      </TableRow>
    </TableHead>
  );
});

const CompletedPartsTable = memo(function CompletedPartsTable({
  partsAtom,
}: {
  readonly partsAtom: PartSummaryAtom;
}) {
  const parts = useAtomValue(partsAtom);
  const [sortBy, setSortBy] = useState<SortColumn>("PartName");
  const [order, setOrder] = useState<"asc" | "desc">("asc");
  const sorted = sortPartSummary(parts, sortBy, order);
  return (
    <Table stickyHeader>
      <PartHeader
        partsAtom={partsAtom}
        sortBy={sortBy}
        setSortBy={setSortBy}
        order={order}
        setOrder={setOrder}
      />
      <TableBody>
        {sorted.map((part) => (
          <PartRow key={part.part} part={part} />
        ))}
      </TableBody>
    </Table>
  );
});

function CompPartsMatDialogBtns() {
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

const CompletedPartsMaterialDialog = memo(function CompletedPartsMaterialDialog() {
  return <MaterialDialog buttons={<CompPartsMatDialogBtns />} />;
});

function RecentCompletedToolbar() {
  const [range, setRange] = useAtom(last30PartSummaryRange);
  const start = last30PartSummaryRangeStart(range);

  return (
    <Box
      component="nav"
      sx={{
        display: "flex",
        minHeight: "2.5em",
        alignItems: "center",
      }}
    >
      <Typography variant="subtitle1">
        Completed Parts from{" "}
        {start.toLocaleString(undefined, {
          weekday: "short",
          month: "short",
          day: "numeric",
          hour: "numeric",
          minute: "2-digit",
        })}{" "}
        to now
      </Typography>
      <Box flexGrow={1} />
      <Select
        value={range}
        onChange={(v) => setRange(v.target.value as "Today" | "PastTwoDays" | "ThisWeek" | "LastTwoWeeks")}
        sx={{ minWidth: "10em" }}
      >
        <MenuItem value="Today">Today</MenuItem>
        <MenuItem value="PastTwoDays">Past Two Days</MenuItem>
        <MenuItem value="ThisWeek">This Week</MenuItem>
        <MenuItem value="LastTwoWeeks">Last Two Weeks</MenuItem>
      </Select>
    </Box>
  );
}

export function RecentCompletedPartsPage(): ReactNode {
  useSetTitle("Completed Parts");

  return (
    <Box component="main" padding="24px">
      <RecentCompletedToolbar />
      <CompletedPartsTable partsAtom={last30PartSummary} />
      <CompletedPartsMaterialDialog />
      <SelectWorkorderDialog />
    </Box>
  );
}

function copyPartsToClipboard(parts: ReadonlyArray<PartSummary>) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Part</th>";
  table += "<th>Completed Qty</th>";
  table += "<th>Abnormal Qty</th>";
  table += "<th>Workorders</th>";
  table += "<th>Active Time (mins)</th>";
  table += "<th>Elapsed Time (mins)</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (const p of parts) {
    table += "<tr>";
    table += `<td>${p.part}</td>`;
    table += `<td>${p.completedQty}</td>`;
    table += `<td>${p.workorders.toAscLazySeq().toRArray().join(";")}</td>`;
    table += `<td>${p.stationMins
      .toAscLazySeq()
      .map(([st, t]) => `${st}: ${t.active}`)
      .toRArray()
      .join(";")}</td>`;
    table += `<td>${p.stationMins
      .toAscLazySeq()
      .map(([st, t]) => `${st}: ${t.elapsed}`)
      .toRArray()
      .join(";")}</td>`;
    table += "</tr>\n";
  }
  table += "</tbody></table>\n";

  copy(table);
}
