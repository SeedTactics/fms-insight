/* Copyright (c) 2020, John Lenz

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
import Fab from "@material-ui/core/Fab";
import CircularProgress from "@material-ui/core/CircularProgress";
import Card from "@material-ui/core/Card";
import CardContent from "@material-ui/core/CardContent";
import TimeAgo from "react-timeago";
import RefreshIcon from "@material-ui/icons/Refresh";
import CardHeader from "@material-ui/core/CardHeader";
import ToolIcon from "@material-ui/icons/Dns";
import Table from "@material-ui/core/Table";
import TableHead from "@material-ui/core/TableHead";
import TableCell from "@material-ui/core/TableCell";
import TableRow from "@material-ui/core/TableRow";
import TableSortLabel from "@material-ui/core/TableSortLabel";
import Tooltip from "@material-ui/core/Tooltip";
import { ToolReport, currentToolReport, toolReportRefreshTime } from "../../data/tools-programs";
import TableBody from "@material-ui/core/TableBody";
import IconButton from "@material-ui/core/IconButton";
import KeyboardArrowDownIcon from "@material-ui/icons/KeyboardArrowDown";
import KeyboardArrowUpIcon from "@material-ui/icons/KeyboardArrowUp";
import Collapse from "@material-ui/core/Collapse";
import { LazySeq } from "../../data/lazyseq";
import { makeStyles } from "@material-ui/core/styles";
import { PartIdenticon } from "../station-monitor/Material";
import clsx from "clsx";
import { useRecoilState, useRecoilValueLoadable, useRecoilValue } from "recoil";
import { useIsDemo } from "../IsDemo";
import { DisplayLoadingAndErrorCard } from "../ErrorsAndLoading";

interface ToolRowProps {
  readonly tool: ToolReport;
}

const useRowStyles = makeStyles({
  mainRow: {
    "& > *": {
      borderBottom: "unset",
    },
  },
  collapseCell: {
    paddingBottom: 0,
    paddingTop: 0,
  },
  detailContainer: {
    display: "flex",
    flexWrap: "wrap",
    justifyContent: "space-around",
    marginLeft: "1em",
    marginRight: "1em",
  },
  detailTable: {
    width: "auto",
    marginLeft: "10em",
    marginBottom: "1em",
  },
  partNameContainer: {
    display: "flex",
    alignItems: "center",
  },
  highlightedRow: {
    backgroundColor: "#BDBDBD",
  },
  noticeRow: {
    backgroundColor: "#E0E0E0",
  },
});

function ToolRow(props: ToolRowProps) {
  const [open, setOpen] = React.useState<boolean>(false);
  const classes = useRowStyles();

  const schUse = props.tool.parts.sumOn((p) => p.scheduledUseMinutes * p.quantity);
  const totalLife = props.tool.machines.sumOn((m) => m.remainingMinutes);

  return (
    <>
      <TableRow
        className={clsx({
          [classes.mainRow]: true,
          [classes.highlightedRow]: schUse > totalLife,
          [classes.noticeRow]: schUse <= totalLife && schUse > props.tool.minRemainingMinutes,
        })}
      >
        <TableCell>
          <IconButton size="small" onClick={() => setOpen(!open)}>
            {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
          </IconButton>
        </TableCell>
        <TableCell>{props.tool.toolName}</TableCell>
        <TableCell align="right">{schUse}</TableCell>
        <TableCell align="right">{totalLife}</TableCell>
        <TableCell align="right">{props.tool.minRemainingMinutes}</TableCell>
        <TableCell>{props.tool.minRemainingMachine}</TableCell>
      </TableRow>
      <TableRow>
        <TableCell className={classes.collapseCell} colSpan={6}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <div className={classes.detailContainer}>
              {props.tool.parts.isEmpty() ? undefined : (
                <Table size="small" className={classes.detailTable}>
                  <TableHead>
                    <TableRow>
                      <TableCell>Part</TableCell>
                      <TableCell>Program</TableCell>
                      <TableCell align="right">Quantity</TableCell>
                      <TableCell align="right">Use/Cycle (min)</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {LazySeq.ofIterable(props.tool.parts).map((p, idx) => (
                      <TableRow key={idx}>
                        <TableCell>
                          <div className={classes.partNameContainer}>
                            <PartIdenticon part={p.partName} size={20} />
                            <span>
                              {p.partName}-{p.process}
                            </span>
                          </div>
                        </TableCell>
                        <TableCell>{p.program}</TableCell>
                        <TableCell align="right">{p.quantity}</TableCell>
                        <TableCell align="right">{p.scheduledUseMinutes}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
              <Table size="small" className={classes.detailTable}>
                <TableHead>
                  <TableRow>
                    <TableCell>Machine</TableCell>
                    <TableCell align="right">Pocket</TableCell>
                    <TableCell align="right">Current Use (min)</TableCell>
                    <TableCell align="right">Lifetime (min)</TableCell>
                    <TableCell align="right">Remaining Use (min)</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {LazySeq.ofIterable(props.tool.machines).map((m, idx) => (
                    <TableRow key={idx}>
                      <TableCell>{m.machineName}</TableCell>
                      <TableCell align="right">{m.pocket}</TableCell>
                      <TableCell align="right">{m.currentUseMinutes}</TableCell>
                      <TableCell align="right">{m.lifetimeMinutes}</TableCell>
                      <TableCell align="right">{m.remainingMinutes}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

type SortColumn = "ToolName" | "ScheduledUse" | "RemainingTotalLife" | "MinRemainingLife" | "MinRemainingMachine";

function ToolSummaryTable() {
  const tools = useRecoilValue(currentToolReport);
  const [sortCol, setSortCol] = React.useState<SortColumn>("ToolName");
  const [sortDir, setSortDir] = React.useState<"asc" | "desc">("asc");

  if (tools === null) {
    return <div />;
  }

  const rows = tools.sortBy((a: ToolReport, b: ToolReport) => {
    let c: number = 0;
    switch (sortCol) {
      case "ToolName":
        c = a.toolName.localeCompare(b.toolName);
        break;
      case "ScheduledUse":
        c =
          a.parts.sumOn((p) => p.scheduledUseMinutes * p.quantity) -
          b.parts.sumOn((p) => p.scheduledUseMinutes * p.quantity);
        break;
      case "RemainingTotalLife":
        c = a.machines.sumOn((m) => m.remainingMinutes) - b.machines.sumOn((m) => m.remainingMinutes);
        break;
      case "MinRemainingLife":
        c = a.minRemainingMinutes - b.minRemainingMinutes;
        break;
      case "MinRemainingMachine":
        c = a.minRemainingMachine.localeCompare(b.minRemainingMachine);
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
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <ToolIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Tools</div>
          </div>
        }
      />
      <CardContent>
        <Table>
          <TableHead>
            <TableRow>
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
              <TableCell sortDirection={sortCol === "ScheduledUse" ? sortDir : false} align="right">
                <Tooltip title="Expected use for all currently scheduled parts">
                  <TableSortLabel
                    active={sortCol === "ScheduledUse"}
                    direction={sortDir}
                    onClick={() => toggleSort("ScheduledUse")}
                  >
                    Scheduled Use (min)
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
              <TableCell sortDirection={sortCol === "RemainingTotalLife" ? sortDir : false} align="right">
                <Tooltip title="Remaining life summed over all machines">
                  <TableSortLabel
                    active={sortCol === "RemainingTotalLife"}
                    direction={sortDir}
                    onClick={() => toggleSort("RemainingTotalLife")}
                  >
                    Total Remaining Life (min)
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
              <TableCell sortDirection={sortCol === "MinRemainingLife" ? sortDir : false} align="right">
                <Tooltip title="Machine with the least remaining life">
                  <TableSortLabel
                    active={sortCol === "MinRemainingLife"}
                    direction={sortDir}
                    onClick={() => toggleSort("MinRemainingLife")}
                  >
                    Smallest Remaining Life (min)
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
              <TableCell sortDirection={sortCol === "MinRemainingMachine" ? sortDir : false}>
                <Tooltip title="Machine with the least remaining life">
                  <TableSortLabel
                    active={sortCol === "MinRemainingMachine"}
                    direction={sortDir}
                    onClick={() => toggleSort("MinRemainingMachine")}
                  >
                    Machine With Smallest Remaining Life
                  </TableSortLabel>
                </Tooltip>
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((tool) => (
              <ToolRow key={tool.toolName} tool={tool} />
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function ToolNavHeader() {
  const [reloadTime, setReloadTime] = useRecoilState(toolReportRefreshTime);
  const report = useRecoilValueLoadable(currentToolReport);
  const demo = useIsDemo();
  const loading = report.state === "loading";

  React.useEffect(() => {
    if (demo && reloadTime === null) {
      setReloadTime(new Date());
    }
  }, []);

  if (reloadTime === null) {
    return (
      <main style={{ margin: "2em", display: "flex", justifyContent: "center" }}>
        <Fab
          color="secondary"
          size="large"
          variant="extended"
          style={{ margin: "2em" }}
          onClick={() => setReloadTime(new Date())}
          disabled={loading}
        >
          <>
            <RefreshIcon style={{ marginRight: "1em" }} />
            Load Tools
          </>
        </Fab>
      </main>
    );
  } else {
    return (
      <nav
        style={{
          display: "flex",
          backgroundColor: "#E0E0E0",
          paddingLeft: "24px",
          paddingRight: "24px",
          minHeight: "2.5em",
          alignItems: "center",
        }}
      >
        <Tooltip title="Refresh Tools">
          <div>
            <IconButton onClick={() => setReloadTime(new Date())} disabled={loading} size="small">
              {loading ? <CircularProgress size={10} /> : <RefreshIcon fontSize="inherit" />}
            </IconButton>
          </div>
        </Tooltip>
        <span style={{ marginLeft: "1em" }}>
          Tools from <TimeAgo date={reloadTime} />
        </span>
      </nav>
    );
  }
}

export function ToolReportPage() {
  React.useEffect(() => {
    document.title = "Tool Report - FMS Insight";
  }, []);
  const demo = useIsDemo();

  return (
    <>
      {demo ? undefined : <ToolNavHeader />}
      <main style={{ padding: "24px" }}>
        <DisplayLoadingAndErrorCard>
          <ToolSummaryTable />
        </DisplayLoadingAndErrorCard>
      </main>
    </>
  );
}
