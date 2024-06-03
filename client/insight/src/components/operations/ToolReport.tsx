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
import * as React from "react";
import { Fab, styled, Box, FormControl } from "@mui/material";
import { CircularProgress } from "@mui/material";
import TimeAgo from "react-timeago";
import { Table } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableSortLabel } from "@mui/material";
import { Tooltip } from "@mui/material";
import { TableBody } from "@mui/material";
import { IconButton } from "@mui/material";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Collapse } from "@mui/material";

import {
  Refresh as RefreshIcon,
  ImportExport,
  KeyboardArrowDown as KeyboardArrowDownIcon,
  KeyboardArrowUp as KeyboardArrowUpIcon,
} from "@mui/icons-material";

import {
  ToolReport,
  currentToolReport,
  toolReportRefreshTime,
  machinesWithTools,
  toolReportHasSerial,
  toolReportHasTimeUsage,
  toolReportHasCntUsage,
  useCopyToolReportToClipboard,
  ToolInMachine,
  toolReportMachineFilter,
} from "../../data/tools-programs.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { PartIdenticon } from "../station-monitor/Material.js";
import { useIsDemo, useSetTitle } from "../routes.js";
import { DisplayLoadingAndError } from "../ErrorsAndLoading.js";
import { useAtom, useAtomValue } from "jotai";

interface ToolRowProps {
  readonly tool: ToolReport;
  readonly showingMultipleMachines: boolean;
}

const cntFormat = new Intl.NumberFormat("en-US", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 1,
});

const ToolTableRow = styled(TableRow, { shouldForwardProp: (prop) => prop.toString()[0] !== "$" })<{
  $noBorderBottom?: boolean;
  $highlightedRow?: boolean;
  $noticeRow?: boolean;
}>(({ theme, $noBorderBottom, $highlightedRow, $noticeRow }) => ({
  ...($noBorderBottom && {
    "& > *": {
      borderBottom: "unset",
    },
  }),
  [theme.breakpoints.up("lg")]: {
    "& td:not(:last-child), & th:not(:last-child)": {
      whiteSpace: "nowrap",
    },
    "& td:last-child, & th:last-child": {
      width: "100%",
    },
  },
  backgroundColor: $highlightedRow ? "#BDBDBD" : $noticeRow ? "#E0E0E0" : undefined,
}));

const ToolDetailSummaryRow = styled(TableRow)({
  "&:not(:last-child)": {
    borderBottom: "2px solid black",
  },
});

function ToolDetailRow({ machines }: { machines: ReadonlyArray<ToolInMachine> }) {
  const showSerial = useAtomValue(toolReportHasSerial);
  const showTime = LazySeq.of(machines).some((m) => m.currentUseMinutes != null || m.lifetimeMinutes != null);
  const showCnts = LazySeq.of(machines).some((m) => m.currentUseCnt != null || m.lifetimeCnt != null);

  const byMachine = LazySeq.of(machines).toLookupOrderedMap(
    (m) => m.machineName,
    (m) => m.pocket,
  );

  return (
    <Table
      size="small"
      sx={{
        width: "auto",
        ml: "10em",
        mb: "1em",
      }}
    >
      <TableHead>
        <TableRow>
          <TableCell>Machine</TableCell>
          <TableCell align="right">Pocket</TableCell>
          {showSerial ? <TableCell>Serial</TableCell> : undefined}
          {showTime ? (
            <>
              <TableCell align="right">Current Use (min)</TableCell>
              <TableCell align="right">Lifetime (min)</TableCell>
              <TableCell align="right">Remaining Use (min)</TableCell>
            </>
          ) : undefined}
          {showCnts ? (
            <>
              <TableCell align="right">Current Use (count)</TableCell>
              <TableCell align="right">Lifetime (count)</TableCell>
              <TableCell align="right">Remaining Use (count)</TableCell>
            </>
          ) : undefined}
        </TableRow>
      </TableHead>
      <TableBody>
        {byMachine.toAscLazySeq().map(([mach, tools]) => (
          <React.Fragment key={mach}>
            {tools.valuesToAscLazySeq().map((m, idx) => (
              <TableRow key={idx}>
                <TableCell>{m.machineName}</TableCell>
                <TableCell align="right">{m.pocket}</TableCell>
                {showSerial ? <TableCell>{m.serial ?? ""}</TableCell> : undefined}
                {showTime ? (
                  <>
                    <TableCell align="right">
                      {m.currentUseMinutes !== null ? m.currentUseMinutes.toFixed(1) : ""}
                    </TableCell>
                    <TableCell align="right">
                      {m.lifetimeMinutes !== null ? m.lifetimeMinutes.toFixed(1) : ""}
                    </TableCell>
                    <TableCell align="right">
                      {m.remainingMinutes !== null ? m.remainingMinutes.toFixed(1) : ""}
                    </TableCell>
                  </>
                ) : undefined}
                {showCnts ? (
                  <>
                    <TableCell align="right">
                      {m.currentUseCnt !== null ? cntFormat.format(m.currentUseCnt) : ""}
                    </TableCell>
                    <TableCell align="right">
                      {m.lifetimeCnt !== null ? cntFormat.format(m.lifetimeCnt) : ""}
                    </TableCell>
                    <TableCell align="right">
                      {m.remainingCnt !== null ? cntFormat.format(m.remainingCnt) : ""}
                    </TableCell>
                  </>
                ) : undefined}
              </TableRow>
            ))}
            {byMachine.size > 1 && tools.size > 1 ? (
              <ToolDetailSummaryRow>
                <TableCell colSpan={showSerial ? 4 : 3} />
                <TableCell>Subtotal</TableCell>
                {showTime ? (
                  <TableCell align="right">
                    {tools
                      .valuesToAscLazySeq()
                      .sumBy((m) => m.remainingMinutes ?? 0)
                      .toFixed(1)}
                  </TableCell>
                ) : undefined}
                {showCnts ? (
                  <TableCell align="right" colSpan={showTime ? 3 : 1}>
                    {cntFormat.format(tools.valuesToAscLazySeq().sumBy((m) => m.remainingCnt ?? 0))}
                  </TableCell>
                ) : undefined}
              </ToolDetailSummaryRow>
            ) : undefined}
          </React.Fragment>
        ))}
        <TableRow>
          <TableCell colSpan={showSerial ? 4 : 3} />
          <TableCell>Total</TableCell>
          {showTime ? (
            <TableCell align="right">
              {LazySeq.of(machines)
                .sumBy((m) => m.remainingMinutes ?? 0)
                .toFixed(1)}
            </TableCell>
          ) : undefined}
          {showCnts ? (
            <TableCell align="right" colSpan={showTime ? 3 : 1}>
              {cntFormat.format(LazySeq.of(machines).sumBy((m) => m.remainingCnt ?? 0))}
            </TableCell>
          ) : undefined}
        </TableRow>
      </TableBody>
    </Table>
  );
}

function ToolRow(props: ToolRowProps) {
  const [open, setOpen] = React.useState<boolean>(false);
  const showTime = useAtomValue(toolReportHasTimeUsage);
  const showCnts = useAtomValue(toolReportHasCntUsage);

  const schUseMin = LazySeq.of(props.tool.parts).sumBy((p) => p.scheduledUseMinutes * p.quantity);
  const totalLifeMin = LazySeq.of(props.tool.machines).sumBy((m) => m.remainingMinutes ?? 0);
  const schUseCnt = LazySeq.of(props.tool.parts).sumBy((p) => p.scheduledUseCnt * p.quantity);
  const totalLifeCnt = LazySeq.of(props.tool.machines).sumBy((m) => m.remainingCnt ?? 0);

  let numCols = 3;
  if (showTime) {
    numCols += 2;
    if (props.showingMultipleMachines) numCols += 1;
  }
  if (showCnts) {
    numCols += 2;
    if (props.showingMultipleMachines) numCols += 1;
  }

  return (
    <>
      <ToolTableRow
        $noBorderBottom
        $highlightedRow={schUseMin > totalLifeMin || schUseCnt > totalLifeCnt}
        $noticeRow={
          schUseMin <= totalLifeMin &&
          schUseCnt <= totalLifeCnt &&
          ((props.tool.minRemainingMinutes !== null && schUseMin > props.tool.minRemainingMinutes) ||
            (props.tool.minRemainingCnt !== null && schUseCnt > props.tool.minRemainingCnt))
        }
      >
        <TableCell>
          <IconButton size="small" onClick={() => setOpen(!open)}>
            {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
          </IconButton>
        </TableCell>
        <TableCell>{props.tool.toolName}</TableCell>
        {showTime ? (
          <>
            <TableCell align="right">{schUseMin > 0 ? schUseMin.toFixed(1) : ""}</TableCell>
            <TableCell align="right">{totalLifeMin > 0 ? totalLifeMin.toFixed(1) : ""}</TableCell>
          </>
        ) : undefined}
        {showCnts ? (
          <>
            <TableCell align="right">{schUseCnt > 0 ? cntFormat.format(schUseCnt) : ""}</TableCell>
            <TableCell align="right">{totalLifeCnt > 0 ? cntFormat.format(totalLifeCnt) : ""}</TableCell>
          </>
        ) : undefined}
        {props.showingMultipleMachines ? (
          <>
            {showTime ? (
              <TableCell align="right">
                {props.tool.minRemainingMinutes !== null ? props.tool.minRemainingMinutes.toFixed(1) : ""}
              </TableCell>
            ) : undefined}
            {showCnts ? (
              <TableCell align="right">
                {props.tool.minRemainingCnt !== null ? cntFormat.format(props.tool.minRemainingCnt) : ""}
              </TableCell>
            ) : undefined}
          </>
        ) : undefined}
        <TableCell />
      </ToolTableRow>
      <TableRow>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={numCols}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <Box
              sx={{
                display: "flex",
                flexWrap: "wrap",
                justifyContent: "space-around",
                ml: "1em",
                mr: "1em",
              }}
            >
              {props.tool.parts.length === 0 ? undefined : (
                <div>
                  <Table
                    size="small"
                    sx={{
                      width: "auto",
                      ml: "10em",
                      mb: "1em",
                    }}
                  >
                    <TableHead>
                      <TableRow>
                        <TableCell>Part</TableCell>
                        <TableCell>Program</TableCell>
                        <TableCell align="right">Quantity</TableCell>
                        {showTime ? <TableCell align="right">Use/Cycle (min)</TableCell> : undefined}
                        {showCnts ? <TableCell align="right">Use/Cycle (cnt)</TableCell> : undefined}
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {LazySeq.of(props.tool.parts).map((p, idx) => (
                        <TableRow key={idx}>
                          <TableCell>
                            <Box
                              sx={{
                                display: "flex",
                                alignItems: "center",
                              }}
                            >
                              <PartIdenticon part={p.partName} size={20} />
                              <span>{p.partName}</span>
                            </Box>
                          </TableCell>
                          <TableCell>{p.program}</TableCell>
                          <TableCell align="right">{p.quantity}</TableCell>
                          {showTime ? (
                            <TableCell align="right">
                              {p.scheduledUseMinutes > 0 ? p.scheduledUseMinutes.toFixed(1) : ""}
                            </TableCell>
                          ) : undefined}
                          {showCnts ? (
                            <TableCell align="right">
                              {p.scheduledUseCnt > 0 ? cntFormat.format(p.scheduledUseCnt) : ""}
                            </TableCell>
                          ) : undefined}
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
              <div>
                <ToolDetailRow machines={props.tool.machines} />
              </div>
            </Box>
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

type SortColumn =
  | "ToolName"
  | "ScheduledUseMin"
  | "RemainingTotalMin"
  | "ScheduledUseCnt"
  | "RemainingTotalCnt"
  | "MinRemainingLifeMinutes"
  | "MinRemainingLifeCnt";

const FilterAnyMachineKey = "__Insight__FilterAnyMachine__";

export function ToolSummaryTable(): JSX.Element {
  const machineFilter = useAtomValue(toolReportMachineFilter);
  const tools = useAtomValue(currentToolReport);
  const [sortCol, setSortCol] = React.useState<SortColumn>("ToolName");
  const [sortDir, setSortDir] = React.useState<"asc" | "desc">("asc");
  const showTime = useAtomValue(toolReportHasTimeUsage);
  const showCnts = useAtomValue(toolReportHasCntUsage);

  if (tools === null) {
    return <div />;
  }

  const showingMultipleMachines =
    machineFilter === null &&
    LazySeq.of(tools)
      .flatMap((t) => t.machines)
      .distinctBy((m) => m.machineName)
      .length() > 1;

  const rows = LazySeq.of(tools).sortWith((a: ToolReport, b: ToolReport) => {
    let c = 0;
    switch (sortCol) {
      case "ToolName":
        c = a.toolName.localeCompare(b.toolName);
        break;
      case "ScheduledUseMin":
        c =
          LazySeq.of(a.parts).sumBy((p) => p.scheduledUseMinutes * p.quantity) -
          LazySeq.of(b.parts).sumBy((p) => p.scheduledUseMinutes * p.quantity);
        break;
      case "RemainingTotalMin":
        c =
          LazySeq.of(a.machines).sumBy((m) => m.remainingMinutes ?? 0) -
          LazySeq.of(b.machines).sumBy((m) => m.remainingMinutes ?? 0);
        break;
      case "ScheduledUseCnt":
        c =
          LazySeq.of(a.parts).sumBy((p) => p.scheduledUseCnt * p.quantity) -
          LazySeq.of(b.parts).sumBy((p) => p.scheduledUseCnt * p.quantity);
        break;
      case "RemainingTotalCnt":
        c =
          LazySeq.of(a.machines).sumBy((m) => m.remainingCnt ?? 0) -
          LazySeq.of(b.machines).sumBy((m) => m.remainingCnt ?? 0);
        break;
      case "MinRemainingLifeMinutes":
        c = (a.minRemainingMinutes ?? 0) - (b.minRemainingMinutes ?? 0);
        break;
      case "MinRemainingLifeCnt":
        c = (a.minRemainingCnt ?? 0) - (b.minRemainingCnt ?? 0);
        break;
    }
    if (c === 0) {
      return 0;
    } else if ((c < 0 && sortDir === "asc") || (c > 0 && sortDir === "desc")) {
      return -1;
    } else {
      return 1;
    }
  });

  function toggleSort(s: SortColumn) {
    if (s == sortCol) {
      setSortDir(sortDir === "asc" ? "desc" : "asc");
    } else {
      setSortCol(s);
    }
  }

  return (
    <Table>
      <TableHead>
        <ToolTableRow>
          <TableCell />
          <TableCell sortDirection={sortCol === "ToolName" ? sortDir : false}>
            <TableSortLabel
              active={sortCol === "ToolName"}
              direction={sortDir}
              onClick={() => toggleSort("ToolName")}
            >
              Tool
            </TableSortLabel>
          </TableCell>
          {showTime ? (
            <>
              <TableCell sortDirection={sortCol === "ScheduledUseMin" ? sortDir : false} align="right">
                <Tooltip title="Expected use for all currently scheduled parts">
                  <TableSortLabel
                    active={sortCol === "ScheduledUseMin"}
                    direction={sortDir}
                    onClick={() => toggleSort("ScheduledUseMin")}
                  >
                    Scheduled Use (minutes)
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
              <TableCell sortDirection={sortCol === "RemainingTotalMin" ? sortDir : false} align="right">
                <Tooltip
                  title={
                    showingMultipleMachines
                      ? "Remaining life summed over all machines"
                      : "Remaining life of all tools in the machine"
                  }
                >
                  <TableSortLabel
                    active={sortCol === "RemainingTotalMin"}
                    direction={sortDir}
                    onClick={() => toggleSort("RemainingTotalMin")}
                  >
                    {showingMultipleMachines ? "Total Remaining Life (minutes)" : "Remaining Life (minutes)"}
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
            </>
          ) : undefined}
          {showCnts ? (
            <>
              <TableCell sortDirection={sortCol === "ScheduledUseCnt" ? sortDir : false} align="right">
                <Tooltip title="Expected use for all currently scheduled parts">
                  <TableSortLabel
                    active={sortCol === "ScheduledUseCnt"}
                    direction={sortDir}
                    onClick={() => toggleSort("ScheduledUseCnt")}
                  >
                    Scheduled Use (count)
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
              <TableCell sortDirection={sortCol === "RemainingTotalCnt" ? sortDir : false} align="right">
                <Tooltip
                  title={
                    showingMultipleMachines
                      ? "Remaining life summed over all machines"
                      : "Remaining life of all tools in the machine"
                  }
                >
                  <TableSortLabel
                    active={sortCol === "RemainingTotalCnt"}
                    direction={sortDir}
                    onClick={() => toggleSort("RemainingTotalCnt")}
                  >
                    {showingMultipleMachines ? "Total Remaining Life (count)" : "Remaining Life (count)"}
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
            </>
          ) : undefined}
          {showingMultipleMachines ? (
            <>
              {showTime ? (
                <TableCell
                  sortDirection={sortCol === "MinRemainingLifeMinutes" ? sortDir : false}
                  align="right"
                >
                  <Tooltip title="Machine with the least remaining life">
                    <TableSortLabel
                      active={sortCol === "MinRemainingLifeMinutes"}
                      direction={sortDir}
                      onClick={() => toggleSort("MinRemainingLifeMinutes")}
                    >
                      Smallest Remaining Life (minutes)
                    </TableSortLabel>
                  </Tooltip>
                </TableCell>
              ) : undefined}
              {showCnts ? (
                <TableCell sortDirection={sortCol === "MinRemainingLifeCnt" ? sortDir : false} align="right">
                  <Tooltip title="Machine with the least remaining life">
                    <TableSortLabel
                      active={sortCol === "MinRemainingLifeCnt"}
                      direction={sortDir}
                      onClick={() => toggleSort("MinRemainingLifeCnt")}
                    >
                      Smallest Remaining Life (count)
                    </TableSortLabel>
                  </Tooltip>
                </TableCell>
              ) : undefined}
            </>
          ) : undefined}
          <TableCell />
        </ToolTableRow>
      </TableHead>
      <TableBody>
        {rows.map((tool) => (
          <ToolRow key={tool.toolName} tool={tool} showingMultipleMachines={showingMultipleMachines} />
        ))}
      </TableBody>
    </Table>
  );
}

function ToolNavHeader() {
  const [machineFilter, setMachineFilter] = useAtom(toolReportMachineFilter);
  const [reloadTime, refreshReport] = useAtom(toolReportRefreshTime);
  const [loading, setLoading] = React.useState(false);
  const machineNames = useAtomValue(machinesWithTools);
  const copyToolReportToClipboard = useCopyToolReportToClipboard();
  const demo = useIsDemo();

  function refresh() {
    setLoading(true);
    refreshReport(new Date())
      .catch(console.log)
      .finally(() => setLoading(false));
  }

  if (demo) {
    return <div />;
  } else if (reloadTime === null) {
    return (
      <main style={{ margin: "2em", display: "flex", justifyContent: "center" }}>
        <Fab
          color="secondary"
          size="large"
          variant="extended"
          style={{ margin: "2em" }}
          onClick={refresh}
          disabled={loading}
        >
          <>
            {loading ? (
              <CircularProgress size={10} style={{ marginRight: "1em" }} />
            ) : (
              <RefreshIcon fontSize="inherit" style={{ marginRight: "1em" }} />
            )}
            Load Tools
          </>
        </Fab>
      </main>
    );
  } else {
    return (
      <Box
        component="nav"
        sx={{
          display: "flex",
          minHeight: "2.5em",
          alignItems: "center",
          maxWidth: "calc(100vw - 24px - 24px)",
        }}
      >
        <Tooltip title="Refresh Tools">
          <div>
            <IconButton onClick={refresh} disabled={loading} size="small">
              {loading ? <CircularProgress size={10} /> : <RefreshIcon fontSize="inherit" />}
            </IconButton>
          </div>
        </Tooltip>
        <span style={{ marginLeft: "1em" }}>
          Tools from <TimeAgo date={reloadTime} />
        </span>
        <div style={{ flexGrow: "1" }} />
        <FormControl size="small">
          <Select
            autoWidth
            displayEmpty
            value={machineFilter ?? FilterAnyMachineKey}
            style={{ marginLeft: "1em" }}
            onChange={(e) => {
              if (e.target.value === FilterAnyMachineKey) {
                setMachineFilter(null);
              } else {
                setMachineFilter(e.target.value);
              }
            }}
          >
            <MenuItem value={FilterAnyMachineKey}>
              <em>All Machines</em>
            </MenuItem>
            {machineNames.map((n) => (
              <MenuItem key={n} value={n}>
                <div style={{ display: "flex", alignItems: "center" }}>
                  <span style={{ marginRight: "1em" }}>{n}</span>
                </div>
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <Tooltip title="Copy to Clipboard">
          <IconButton
            style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
            onClick={copyToolReportToClipboard}
            size="large"
          >
            <ImportExport />
          </IconButton>
        </Tooltip>
      </Box>
    );
  }
}

export function ToolReportPage(): JSX.Element {
  useSetTitle("Tool Report");

  return (
    <Box paddingLeft="24px" paddingRight="24px" paddingTop="10px">
      <ToolNavHeader />
      <main>
        <DisplayLoadingAndError>
          <ToolSummaryTable />
        </DisplayLoadingAndError>
      </main>
    </Box>
  );
}
