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
import { useState, Fragment, ReactNode } from "react";
import { Fab, styled, Box, FormControl } from "@mui/material";
import { CircularProgress } from "@mui/material";
import TimeAgo from "react-timeago";
import { Table } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableRow } from "@mui/material";
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
  useCopyToolReportToClipboard,
  ToolInMachine,
  toolReportEstimatedToolCounts,
} from "../../data/tools-programs.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { PartIdenticon } from "../station-monitor/Material.js";
import { useIsDemo, useSetTitle } from "../routes.js";
import { DisplayLoadingAndError } from "../ErrorsAndLoading.js";
import { atom, useAtom, useAtomValue } from "jotai";

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

function FormatMinAndCnt({
  min,
  cnt,
  showZero,
}: {
  min: number | null;
  cnt: number | null;
  showZero?: boolean;
}) {
  min ??= 0;
  cnt ??= 0;
  if (min === 0 && cnt === 0) {
    if (showZero) {
      return "0";
    } else {
      return "";
    }
  } else if (min > 0 && cnt === 0) {
    return min.toFixed(1) + " min";
  } else if (min === 0 && cnt > 0) {
    return cntFormat.format(cnt) + " cnt";
  } else {
    return min.toFixed(1) + " min / " + cntFormat.format(cnt) + " cnt";
  }
}

function PartDetailTable({ tool, machine }: { tool: ToolReport; machine?: string }) {
  const parts = machine
    ? LazySeq.of(tool.parts).filter((p) => p.machines.has(machine))
    : LazySeq.of(tool.parts);

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
          <TableCell>Part</TableCell>
          <TableCell>Program</TableCell>
          {!machine ? <TableCell>Machines</TableCell> : undefined}
          <TableCell align="right">Quantity</TableCell>
          <TableCell align="right">Use/Cycle (min)</TableCell>
          <TableCell align="right">Use/Cycle (cnt)</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {parts.isEmpty() ? (
          <TableRow>
            <TableCell colSpan={5}>No programs use this machine</TableCell>
          </TableRow>
        ) : (
          parts.map((p, idx) => (
            <TableRow key={idx}>
              <TableCell>
                <Box
                  sx={{
                    display: "flex",
                    alignItems: "center",
                  }}
                >
                  <PartIdenticon part={p.partAndProg.part} size={20} />
                  <span>{p.partAndProg.part}</span>
                </Box>
              </TableCell>
              <TableCell>{p.partAndProg.operation}</TableCell>
              {!machine ? (
                <TableCell>
                  {p.machines.foldl((acc, m) => (acc.length > 0 ? acc + ", " : "") + m, "")}
                </TableCell>
              ) : undefined}
              <TableCell align="right">{p.quantity}</TableCell>
              <TableCell align="right">
                {p.scheduledUseMinutes > 0 ? p.scheduledUseMinutes.toFixed(1) : ""}
              </TableCell>
              <TableCell align="right">
                {p.scheduledUseCnt > 0 ? cntFormat.format(p.scheduledUseCnt) : ""}
              </TableCell>
            </TableRow>
          ))
        )}
      </TableBody>
    </Table>
  );
}

function MachineDetailTable({ machines }: { machines: ReadonlyArray<ToolInMachine> }) {
  const showSerial = useAtomValue(toolReportHasSerial);

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
          <TableCell align="right">Current Use </TableCell>
          <TableCell align="right">Lifetime </TableCell>
          <TableCell align="right">Remaining Use </TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {byMachine.toAscLazySeq().map(([mach, tools]) => (
          <Fragment key={mach}>
            {tools.valuesToAscLazySeq().map((m, idx) => (
              <TableRow key={idx}>
                <TableCell>{m.machineName}</TableCell>
                <TableCell align="right">{m.pocket}</TableCell>
                {showSerial ? <TableCell>{m.serial ?? ""}</TableCell> : undefined}
                <TableCell align="right">
                  <FormatMinAndCnt min={m.currentUseMinutes} cnt={m.currentUseCnt} />
                </TableCell>
                <TableCell align="right">
                  <FormatMinAndCnt min={m.lifetimeMinutes} cnt={m.lifetimeCnt} />
                </TableCell>
                <TableCell align="right">
                  <FormatMinAndCnt min={m.remainingMinutes} cnt={m.remainingCnt} showZero />
                </TableCell>
              </TableRow>
            ))}
            {byMachine.size > 1 && tools.size > 1 ? (
              <ToolDetailSummaryRow>
                <TableCell colSpan={showSerial ? 4 : 3} />
                <TableCell>Subtotal</TableCell>
                <TableCell align="right">
                  <FormatMinAndCnt
                    min={tools.valuesToAscLazySeq().sumBy((m) => m.remainingMinutes ?? 0)}
                    cnt={tools.valuesToAscLazySeq().sumBy((m) => m.remainingCnt ?? 0)}
                  />
                </TableCell>
              </ToolDetailSummaryRow>
            ) : undefined}
          </Fragment>
        ))}
        <TableRow>
          <TableCell colSpan={showSerial ? 4 : 3} />
          <TableCell>Total</TableCell>
          <TableCell align="right">
            <FormatMinAndCnt
              min={LazySeq.of(machines).sumBy((m) => m.remainingMinutes ?? 0)}
              cnt={LazySeq.of(machines).sumBy((m) => m.remainingCnt ?? 0)}
            />
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  );
}

function ToolSummaryHeader() {
  const cntsWereEstimated = useAtomValue(toolReportEstimatedToolCounts);

  return (
    <ToolTableRow>
      <TableCell />
      <TableCell>Tool</TableCell>
      <TableCell align="right">
        <Tooltip
          title={
            "Expected use for all currently scheduled parts." +
            (cntsWereEstimated ? " Counts are estimates." : "")
          }
        >
          <span>Scheduled Use</span>
        </Tooltip>
      </TableCell>
      <TableCell align="right">
        <Tooltip
          title={
            "Remaining use summed over all machines." + (cntsWereEstimated ? " Counts are estimates." : "")
          }
        >
          <span>Total Remaining Use</span>
        </Tooltip>
      </TableCell>
      <TableCell align="right">
        <Tooltip
          title={
            "Machine with the least remaining use." + (cntsWereEstimated ? " Counts are estimates." : "")
          }
        >
          <span>Smallest Remaining Use</span>
        </Tooltip>
      </TableCell>
      <TableCell />
    </ToolTableRow>
  );
}

function ToolSummaryRow({ tool }: { tool: ToolReport }) {
  const [open, setOpen] = useState<boolean>(false);

  const schUseMin = LazySeq.of(tool.parts).sumBy((p) => p.scheduledUseMinutes * p.quantity);
  const totalLifeMin = LazySeq.of(tool.machines).sumBy((m) => m.remainingMinutes ?? 0);
  const schUseCnt = LazySeq.of(tool.parts).sumBy((p) => p.scheduledUseCnt * p.quantity);
  const totalLifeCnt = LazySeq.of(tool.machines).sumBy((m) => m.remainingCnt ?? 0);

  const numCols = 6;

  return (
    <>
      <ToolTableRow
        $noBorderBottom
        $highlightedRow={schUseMin > totalLifeMin || schUseCnt > totalLifeCnt}
        $noticeRow={
          schUseMin <= totalLifeMin &&
          schUseCnt <= totalLifeCnt &&
          ((tool.minRemainingMinutes !== null && schUseMin > tool.minRemainingMinutes) ||
            (tool.minRemainingCnt !== null && schUseCnt > tool.minRemainingCnt))
        }
      >
        <TableCell>
          <IconButton size="small" onClick={() => setOpen(!open)}>
            {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
          </IconButton>
        </TableCell>
        <TableCell>{tool.toolName}</TableCell>
        <TableCell align="right">
          <FormatMinAndCnt min={schUseMin} cnt={schUseCnt} />
        </TableCell>
        <TableCell align="right">
          <FormatMinAndCnt min={totalLifeMin} cnt={totalLifeCnt} />
        </TableCell>
        <TableCell align="right">
          <FormatMinAndCnt min={tool.minRemainingMinutes} cnt={tool.minRemainingCnt} showZero />
        </TableCell>
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
              {tool.parts.length === 0 ? undefined : (
                <div>
                  <PartDetailTable tool={tool} />
                </div>
              )}
              <div>
                <MachineDetailTable machines={tool.machines} />
              </div>
            </Box>
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

function ToolMachineHeader({ machine }: { machine: string }) {
  const cntsWereEstimated = useAtomValue(toolReportEstimatedToolCounts);
  const showSerial = useAtomValue(toolReportHasSerial);

  return (
    <ToolTableRow>
      <TableCell />
      <TableCell>Tool In {machine}</TableCell>
      <TableCell align="right">Pocket</TableCell>
      {showSerial ? <TableCell>Serial</TableCell> : undefined}
      <TableCell align="right">
        <Tooltip
          title={
            "Expected use for all currently scheduled parts." +
            (cntsWereEstimated ? " Counts are estimates." : "")
          }
        >
          <span>Scheduled Use</span>
        </Tooltip>
      </TableCell>
      <TableCell align="right">
        <Tooltip
          title={"Current recorded usage of this tool." + (cntsWereEstimated ? " Counts are estimates." : "")}
        >
          <span>Current Use</span>
        </Tooltip>
      </TableCell>
      <TableCell align="right">
        <Tooltip
          title={
            "Current configured lifetime of this tool." + (cntsWereEstimated ? " Counts are estimates." : "")
          }
        >
          <span>Lifetime</span>
        </Tooltip>{" "}
      </TableCell>
      <TableCell align="right">
        <Tooltip title={"Lifetime minus current use." + (cntsWereEstimated ? " Counts are estimates." : "")}>
          <span>Remaining Use</span>
        </Tooltip>
      </TableCell>
      <TableCell />
    </ToolTableRow>
  );
}

function ToolMachineRow({ report, pocket }: { report: ToolReport; pocket: ToolInMachine }) {
  const [open, setOpen] = useState<boolean>(false);
  const showSerial = useAtomValue(toolReportHasSerial);

  const numCols = showSerial ? 9 : 8;

  return (
    <>
      <ToolTableRow $noBorderBottom>
        <TableCell>
          <IconButton size="small" onClick={() => setOpen(!open)}>
            {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
          </IconButton>
        </TableCell>
        <TableCell>{report.toolName}</TableCell>
        <TableCell align="right">{pocket.pocket}</TableCell>
        {showSerial ? <TableCell>{pocket.serial ?? ""}</TableCell> : undefined}
        <TableCell align="right">TODO</TableCell>
        <TableCell align="right">
          <FormatMinAndCnt min={pocket.currentUseMinutes} cnt={pocket.currentUseCnt} />
        </TableCell>
        <TableCell align="right">
          <FormatMinAndCnt min={pocket.lifetimeMinutes} cnt={pocket.lifetimeCnt} />
        </TableCell>
        <TableCell align="right">
          <FormatMinAndCnt min={pocket.remainingMinutes} cnt={pocket.remainingCnt} />
        </TableCell>
        <TableCell />
      </ToolTableRow>
      <TableRow>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={numCols}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <PartDetailTable tool={report} machine={pocket.machineName} />
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

const FilterAnyMachineKey = "__Insight__FilterAnyMachine__";

const toolReportMachineFilter = atom<string | null>(null);

export function ToolSummaryTable(): ReactNode {
  const machineFilter = useAtomValue(toolReportMachineFilter);
  const tools = useAtomValue(currentToolReport);

  if (tools === null) {
    return <div />;
  }

  return (
    <Table stickyHeader>
      <TableHead>
        {machineFilter === null ? <ToolSummaryHeader /> : <ToolMachineHeader machine={machineFilter} />}
      </TableHead>
      <TableBody>
        {tools.map((tool) =>
          machineFilter === null ? (
            <ToolSummaryRow key={tool.toolName} tool={tool} />
          ) : (
            <Fragment key={tool.toolName}>
              {tool.machines
                .filter((m) => m.machineName === machineFilter)
                .map((m) => (
                  <ToolMachineRow key={m.pocket} report={tool} pocket={m} />
                ))}
            </Fragment>
          ),
        )}
      </TableBody>
    </Table>
  );
}

function ToolNavHeader() {
  const [machineFilter, setMachineFilter] = useAtom(toolReportMachineFilter);
  const [reloadTime, refreshReport] = useAtom(toolReportRefreshTime);
  const [loading, setLoading] = useState(false);
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

export function ToolReportPage(): ReactNode {
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
