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
import { Box, Fab, styled } from "@mui/material";
import { CircularProgress } from "@mui/material";
import { Card } from "@mui/material";
import { CardContent } from "@mui/material";
import TimeAgo from "react-timeago";
import RefreshIcon from "@mui/icons-material/Refresh";
import { CardHeader } from "@mui/material";
import ProgramIcon from "@mui/icons-material/Receipt";
import CodeIcon from "@mui/icons-material/Code";
import { Table } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableSortLabel } from "@mui/material";
import { Typography } from "@mui/material";
import { Tooltip } from "@mui/material";
import {
  programReportRefreshTime,
  currentProgramReport,
  useRefreshProgramReport,
  CellControllerProgram,
  programToShowContent,
  programContent,
  programToShowHistory,
  programFilter,
} from "../../data/tools-programs";
import { TableBody } from "@mui/material";
import { IconButton } from "@mui/material";
import KeyboardArrowDownIcon from "@mui/icons-material/KeyboardArrowDown";
import KeyboardArrowUpIcon from "@mui/icons-material/KeyboardArrowUp";
import FirstPageIcon from "@mui/icons-material/FirstPage";
import KeyboardArrowLeft from "@mui/icons-material/KeyboardArrowLeft";
import KeyboardArrowRight from "@mui/icons-material/KeyboardArrowRight";
import HistoryIcon from "@mui/icons-material/History";
import { Collapse } from "@mui/material";
import { LazySeq } from "../../util/lazyseq";
import { PartIdenticon } from "../station-monitor/Material";
import { useSetRecoilState, useRecoilState, useRecoilValue } from "recoil";
import { Dialog } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { Button } from "@mui/material";
import { DialogActions } from "@mui/material";
import { useIsDemo } from "../routes";
import { DisplayLoadingAndErrorCard } from "../ErrorsAndLoading";
import { IProgramRevision } from "../../network/api";
import { MachineBackend } from "../../network/backend";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";

interface ProgramRowProps {
  readonly program: CellControllerProgram;
  readonly showCellCtrlCol: boolean;
  readonly showRevCol: boolean;
}

const ProgramTableRow = styled(TableRow)(() => ({
  "& > *": {
    borderBottom: "unset",
  },
}));

function programFilename(program: string): string {
  if (program.length === 0) return "";

  const idx = program.lastIndexOf("\\");
  if (idx >= 0 && idx < program.length - 2) {
    return program.substr(idx + 1);
  } else {
    return program;
  }
}

function ProgramRow(props: ProgramRowProps) {
  const [open, setOpen] = React.useState<boolean>(false);
  const setProgramToShowContent = useSetRecoilState(programToShowContent);
  const setProgramToShowHistory = useSetRecoilState(programToShowHistory);

  const numCols = 8 + (props.showCellCtrlCol ? 1 : 0) + (props.showRevCol ? 1 : 0);

  return (
    <>
      <ProgramTableRow>
        <TableCell>
          {props.program.toolUse === null || props.program.toolUse.tools.length === 0 ? undefined : (
            <IconButton size="small" onClick={() => setOpen(!open)}>
              {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
            </IconButton>
          )}
        </TableCell>
        <TableCell>{programFilename(props.program.programName)}</TableCell>
        {props.showCellCtrlCol ? <TableCell>{props.program.cellControllerProgramName}</TableCell> : undefined}
        <TableCell>
          {props.program.partName !== null ? (
            <Box
              sx={{
                display: "flex",
                alignItems: "center",
              }}
            >
              <PartIdenticon part={props.program.partName} size={20} />
              <span>
                {props.program.partName}-{props.program.process}
              </span>
            </Box>
          ) : undefined}
        </TableCell>
        <TableCell>{props.program.comment ?? ""}</TableCell>
        {props.showRevCol ? (
          <TableCell>{props.program.revision === null ? "" : props.program.revision.toFixed()}</TableCell>
        ) : undefined}
        <TableCell align="right">
          {props.program.statisticalCycleTime === null
            ? ""
            : props.program.statisticalCycleTime.medianMinutesForSingleMat.toFixed(2)}
        </TableCell>
        <TableCell align="right">
          {props.program.statisticalCycleTime === null
            ? ""
            : props.program.statisticalCycleTime.MAD_aboveMinutes.toFixed(2)}
        </TableCell>
        <TableCell align="right">
          {props.program.statisticalCycleTime === null
            ? ""
            : props.program.statisticalCycleTime.MAD_belowMinutes.toFixed(2)}
        </TableCell>
        <TableCell>
          <Tooltip title="Load Program Content">
            <IconButton size="small" onClick={() => setProgramToShowContent(props.program)}>
              <CodeIcon />
            </IconButton>
          </Tooltip>
          {props.program.revision !== null ? (
            <Tooltip title="Revision History">
              <IconButton size="small" onClick={() => setProgramToShowHistory(props.program)}>
                <HistoryIcon />
              </IconButton>
            </Tooltip>
          ) : undefined}
        </TableCell>
      </ProgramTableRow>
      <TableRow>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={numCols}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <Box sx={{ mr: "1em", ml: "3em" }}>
              {props.program.toolUse === null || props.program.toolUse.tools.length === 0 ? undefined : (
                <Table
                  size="small"
                  sx={{
                    width: "auto",
                    ml: "10em",
                    mr: "1em",
                  }}
                >
                  <TableHead>
                    <TableRow>
                      <TableCell>Tool</TableCell>
                      <TableCell align="right">Estimated Usage (min)</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {LazySeq.ofIterable(props.program.toolUse.tools).map((t, idx) => (
                      <TableRow key={idx}>
                        <TableCell>{t.toolName}</TableCell>
                        <TableCell align="right">{t.cycleUsageMinutes.toFixed(1)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </Box>
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

type SortColumn =
  | "ProgramName"
  | "CellProgName"
  | "Comment"
  | "Revision"
  | "PartName"
  | "MedianTime"
  | "DeviationAbove"
  | "DeviationBelow";

export function ProgramSummaryTable(): JSX.Element {
  const report = useRecoilValue(currentProgramReport);
  const [sortCol, setSortCol] = React.useState<SortColumn>("ProgramName");
  const [sortDir, setSortDir] = React.useState<"asc" | "desc">("asc");
  const [filter, setFilter] = useRecoilState(programFilter);

  if (report === null) {
    return <div />;
  }

  const rows = report.programs.toLazySeq().sortWith((a: CellControllerProgram, b: CellControllerProgram) => {
    let c = 0;
    switch (sortCol) {
      case "ProgramName":
        c = programFilename(a.programName).localeCompare(programFilename(b.programName));
        break;
      case "CellProgName":
        c = a.cellControllerProgramName.localeCompare(b.cellControllerProgramName);
        break;
      case "Comment":
        if (a.comment === null && b.comment === null) {
          c = 0;
        } else if (a.comment === null) {
          c = 1;
        } else if (b.comment === null) {
          c = -1;
        } else {
          c = a.comment.localeCompare(b.comment);
        }
        break;
      case "Revision":
        if (a.revision === null && b.revision === null) {
          c = 0;
        } else if (a.revision === null) {
          c = 1;
        } else if (b.revision === null) {
          c = -1;
        } else {
          c = a.revision - b.revision;
        }
        break;
      case "PartName":
        if (a.partName === null && b.partName === null) {
          c = 0;
        } else if (a.partName === null) {
          c = 1;
        } else if (b.partName === null) {
          c = -1;
        } else {
          c = a.partName.localeCompare(b.partName);
          if (c === 0) {
            c = (a.process ?? 1) - (b.process ?? 1);
          }
        }
        break;
      case "MedianTime":
        if (a.statisticalCycleTime === null && b.statisticalCycleTime === null) {
          c = 0;
        } else if (a.statisticalCycleTime === null) {
          c = 1;
        } else if (b.statisticalCycleTime === null) {
          c = -1;
        } else {
          c = a.statisticalCycleTime.medianMinutesForSingleMat - b.statisticalCycleTime.medianMinutesForSingleMat;
        }
        break;
      case "DeviationAbove":
        if (a.statisticalCycleTime === null && b.statisticalCycleTime === null) {
          c = 0;
        } else if (a.statisticalCycleTime === null) {
          c = 1;
        } else if (b.statisticalCycleTime === null) {
          c = -1;
        } else {
          c = a.statisticalCycleTime.MAD_aboveMinutes - b.statisticalCycleTime.MAD_aboveMinutes;
        }
        break;
      case "DeviationBelow":
        if (a.statisticalCycleTime === null && b.statisticalCycleTime === null) {
          c = 0;
        } else if (a.statisticalCycleTime === null) {
          c = 1;
        } else if (b.statisticalCycleTime === null) {
          c = -1;
        } else {
          c = a.statisticalCycleTime.MAD_belowMinutes - b.statisticalCycleTime.MAD_belowMinutes;
        }
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
    if (s === sortCol) {
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
            <ProgramIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Cell Controller Programs</div>
            <div style={{ flexGrow: 1 }} />
            <Select
              autoWidth
              value={filter}
              onChange={(e) => setFilter(e.target.value as "AllPrograms" | "ActivePrograms")}
            >
              <MenuItem key="AllPrograms" value="AllPrograms">
                All Programs
              </MenuItem>
              <MenuItem key="ActivePrograms" value="ActivePrograms">
                Active Programs
              </MenuItem>
            </Select>
          </div>
        }
      />
      <CardContent>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell />
              <TableCell sortDirection={sortCol === "ProgramName" ? sortDir : false}>
                <TableSortLabel
                  active={sortCol === "ProgramName"}
                  direction={sortDir}
                  onClick={() => toggleSort("ProgramName")}
                >
                  Program Name
                </TableSortLabel>
              </TableCell>
              {report.cellNameDifferentFromProgName ? (
                <TableCell sortDirection={sortCol === "CellProgName" ? sortDir : false}>
                  <TableSortLabel
                    active={sortCol === "CellProgName"}
                    direction={sortDir}
                    onClick={() => toggleSort("CellProgName")}
                  >
                    Cell Controller Program
                  </TableSortLabel>
                </TableCell>
              ) : undefined}
              <TableCell sortDirection={sortCol === "PartName" ? sortDir : false}>
                <TableSortLabel
                  active={sortCol === "PartName"}
                  direction={sortDir}
                  onClick={() => toggleSort("PartName")}
                >
                  Part
                </TableSortLabel>
              </TableCell>
              <TableCell sortDirection={sortCol === "Comment" ? sortDir : false}>
                <TableSortLabel
                  active={sortCol === "Comment"}
                  direction={sortDir}
                  onClick={() => toggleSort("Comment")}
                >
                  Comment
                </TableSortLabel>
              </TableCell>
              {report.hasRevisions ? (
                <TableCell sortDirection={sortCol === "Revision" ? sortDir : false}>
                  <TableSortLabel
                    active={sortCol === "Revision"}
                    direction={sortDir}
                    onClick={() => toggleSort("Revision")}
                  >
                    Revision
                  </TableSortLabel>
                </TableCell>
              ) : undefined}
              <TableCell sortDirection={sortCol === "MedianTime" ? sortDir : false} align="right">
                <TableSortLabel
                  active={sortCol === "MedianTime"}
                  direction={sortDir}
                  onClick={() => toggleSort("MedianTime")}
                >
                  Median Time / Material (min)
                </TableSortLabel>
              </TableCell>
              <TableCell sortDirection={sortCol === "DeviationAbove" ? sortDir : false} align="right">
                <TableSortLabel
                  active={sortCol === "DeviationAbove"}
                  direction={sortDir}
                  onClick={() => toggleSort("DeviationAbove")}
                >
                  Deviation Above Median
                </TableSortLabel>
              </TableCell>
              <TableCell sortDirection={sortCol === "DeviationBelow" ? sortDir : false} align="right">
                <TableSortLabel
                  active={sortCol === "DeviationBelow"}
                  direction={sortDir}
                  onClick={() => toggleSort("DeviationBelow")}
                >
                  Deviation Below Median
                </TableSortLabel>
              </TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {LazySeq.ofIterable(rows).map((program, idx) => (
              <ProgramRow
                key={idx}
                program={program}
                showCellCtrlCol={report.cellNameDifferentFromProgName}
                showRevCol={report.hasRevisions}
              />
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function ProgramContentCode() {
  const ct = useRecoilValue(programContent);
  const [highlighted, setHighlighted] = React.useState<string | null>(null);

  const worker = React.useMemo(
    () => new Worker(new URL("./ProgramHighlight.ts", import.meta.url), { type: "module" }),
    []
  );

  React.useEffect(() => {
    let set = (h: string) => setHighlighted(h);
    worker.onmessage = (e) => set(e.data as string);
    return () => {
      // cleanup
      set = () => null;
      worker.terminate();
      setHighlighted(null);
    };
  }, [worker]);

  React.useEffect(() => {
    if (ct && ct !== "") {
      worker.postMessage(ct);
    }
  }, [ct]);

  return (
    <pre>
      {highlighted === null ? (
        <code className="gcode">{ct}</code>
      ) : (
        <code className="gcode" dangerouslySetInnerHTML={{ __html: highlighted }} />
      )}
    </pre>
  );
}

export function ProgramContentDialog(): JSX.Element {
  const [program, setProgramToShowContent] = useRecoilState(programToShowContent);
  const history = useRecoilValue(programToShowHistory);

  // when history is open, content is shown on the history dialog
  return (
    <Dialog open={program !== null && history === null} onClose={() => setProgramToShowContent(null)} maxWidth="lg">
      <DialogTitle>
        {program?.partName ? (
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <PartIdenticon part={program.partName} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>
              {program?.programName ?? "Program"} {program?.revision ? " rev" + program.revision.toFixed() : ""}{" "}
              <Typography variant="subtitle1" component="span">
                ({program.partName}-{program.process ?? 1})
              </Typography>
            </div>
          </div>
        ) : (
          <>
            {program?.programName ?? "Program"} {program?.revision ? " rev" + program.revision.toFixed() : ""}
          </>
        )}
      </DialogTitle>
      <DialogContent>
        {program === null || history !== null ? (
          <div />
        ) : (
          <DisplayLoadingAndErrorCard>
            <ProgramContentCode />
          </DisplayLoadingAndErrorCard>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setProgramToShowContent(null)}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}

interface ProgramRevisionTableProps {
  readonly page: number;
  readonly loading: boolean;
  readonly revisions: Iterable<Readonly<IProgramRevision>>;
}

const revisionsPerPage = 10;

function ProgramRevisionTable(props: ProgramRevisionTableProps) {
  const program = useRecoilValue(programToShowHistory);
  const setProgramToShowContent = useSetRecoilState(programToShowContent);

  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableCell>Revision</TableCell>
          <TableCell>Comment</TableCell>
          <TableCell>Cell Controller Program</TableCell>
          <TableCell />
        </TableRow>
      </TableHead>
      <TableBody>
        {props.loading ? (
          <>
            <TableRow>
              <TableCell colSpan={4}>
                <CircularProgress />
              </TableCell>
            </TableRow>
            {LazySeq.ofRange(0, revisionsPerPage - 1).map((i) => (
              <TableRow key={i}>
                <TableCell colSpan={4} />
              </TableRow>
            ))}
          </>
        ) : (
          LazySeq.ofIterable(props.revisions)
            .drop(props.page * revisionsPerPage)
            .take(revisionsPerPage)
            .map((rev) => (
              <TableRow key={rev.revision}>
                <TableCell>{rev.revision}</TableCell>
                <TableCell>{rev.comment ?? ""}</TableCell>
                <TableCell>{rev.cellControllerProgramName ?? ""}</TableCell>
                <TableCell>
                  <Tooltip title="Load Program Content">
                    <IconButton
                      size="small"
                      onClick={() =>
                        setProgramToShowContent({
                          ...rev,
                          partName: program?.partName ?? null,
                          process: program?.process ?? null,
                        })
                      }
                    >
                      <CodeIcon />
                    </IconButton>
                  </Tooltip>
                </TableCell>
              </TableRow>
            ))
        )}
      </TableBody>
    </Table>
  );
}

interface LastPage {
  readonly page: number;
  readonly hasMore: boolean;
}

export function ProgramHistoryDialog(): JSX.Element {
  const [program, setProgram] = useRecoilState(programToShowHistory);
  const [programForContent, setProgramForContent] = useRecoilState(programToShowContent);

  const [revisions, setRevisions] = React.useState<ReadonlyArray<Readonly<IProgramRevision>> | null>(null);
  const [lastLoadedPage, setLastLoadedPage] = React.useState<LastPage>({ page: 0, hasMore: false });
  const [page, setPage] = React.useState<number>(0);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<string | Error | null>(null);

  React.useEffect(() => {
    if (program === null) {
      setRevisions(null);
      setPage(0);
      setLastLoadedPage({ page: 0, hasMore: false });
    } else if (program !== null && revisions === null) {
      // load initial
      setLoading(true);
      setError(null);
      MachineBackend.getProgramRevisionsInDescendingOrderOfRevision(program.programName, revisionsPerPage, undefined)
        .then((revs) => {
          setRevisions(revs);
          setLastLoadedPage({ page: 0, hasMore: revs.length === revisionsPerPage });
        })
        .catch(setError)
        .finally(() => setLoading(false));
    }
  }, [program, revisions]);

  function advancePage() {
    if (page < lastLoadedPage.page) {
      setPage(page + 1);
    } else if (lastLoadedPage.hasMore && program !== null && revisions !== null && revisions.length > 0) {
      setLoading(true);
      setError(null);
      const rev = revisions[revisions.length - 1];
      MachineBackend.getProgramRevisionsInDescendingOrderOfRevision(
        program.programName,
        revisionsPerPage,
        rev ? rev.revision - 1 : undefined
      )
        .then((revs) => {
          setRevisions((oldRevs) => (oldRevs === null ? revs : oldRevs.concat(revs)));
          setLastLoadedPage({ page: page + 1, hasMore: revs.length === revisionsPerPage });
          setPage(page + 1);
        })
        .catch(setError)
        .finally(() => setLoading(false));
    }
  }

  return (
    <Dialog open={program !== null} onClose={() => setProgram(null)} maxWidth="lg">
      <DialogTitle>
        {program?.partName ? (
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <PartIdenticon part={program.partName} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>
              {program.programName ?? "Program"}{" "}
              <Typography variant="subtitle1" component="span">
                ({program.partName}-{program.process ?? 1})
              </Typography>
            </div>
          </div>
        ) : (
          <>{program?.programName ?? "Program"}</>
        )}
      </DialogTitle>
      <DialogContent>
        {error !== null ? (
          <Card>
            <CardContent>{typeof error === "string" ? error : error.message}</CardContent>
          </Card>
        ) : undefined}
        {programForContent !== null ? (
          <DisplayLoadingAndErrorCard>
            <ProgramContentCode />
          </DisplayLoadingAndErrorCard>
        ) : revisions !== null ? (
          <ProgramRevisionTable page={page} revisions={revisions} loading={loading} />
        ) : loading ? (
          <CircularProgress />
        ) : undefined}
      </DialogContent>
      <DialogActions>
        <div style={{ display: "flex", alignItems: "center", width: "100%" }}>
          {programForContent === null ? (
            <>
              <Tooltip title="Latest Revisions">
                <span>
                  <IconButton onClick={() => setPage(0)} disabled={loading || page === 0} size="large">
                    <FirstPageIcon />
                  </IconButton>
                </span>
              </Tooltip>
              <Tooltip title="Previous Page">
                <span>
                  <IconButton onClick={() => setPage(page - 1)} disabled={loading || page === 0} size="large">
                    <KeyboardArrowLeft />
                  </IconButton>
                </span>
              </Tooltip>
              <Tooltip title="Next Page">
                <span>
                  <IconButton
                    onClick={() => advancePage()}
                    disabled={loading || (page === lastLoadedPage.page && !lastLoadedPage.hasMore)}
                    size="large"
                  >
                    <KeyboardArrowRight />
                  </IconButton>
                </span>
              </Tooltip>
            </>
          ) : undefined}
          <div style={{ flexGrow: 1 }} />
          {programForContent !== null ? (
            <Button onClick={() => setProgramForContent(null)}>Back to History</Button>
          ) : undefined}
          <Button onClick={() => setProgram(null)}>Close</Button>
        </div>
      </DialogActions>
    </Dialog>
  );
}

function ProgNavHeader() {
  const reloadTime = useRecoilValue(programReportRefreshTime);
  const [loading, setLoading] = React.useState(false);
  const refreshPrograms = useRefreshProgramReport();
  const demo = useIsDemo();

  function refresh() {
    setLoading(true);
    refreshPrograms().finally(() => setLoading(false));
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
            Load Programs
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
            <IconButton onClick={refresh} disabled={loading} size="small">
              {loading ? <CircularProgress size={10} /> : <RefreshIcon fontSize="inherit" />}
            </IconButton>
          </div>
        </Tooltip>
        <span style={{ marginLeft: "1em" }}>
          Programs from <TimeAgo date={reloadTime} />
        </span>
      </nav>
    );
  }
}

export function ProgramReportPage(): JSX.Element {
  React.useEffect(() => {
    document.title = "Programs - FMS Insight";
  }, []);

  return (
    <>
      <ProgNavHeader />
      <main style={{ padding: "24px" }}>
        <DisplayLoadingAndErrorCard>
          <ProgramSummaryTable />
        </DisplayLoadingAndErrorCard>
      </main>
      <ProgramContentDialog />
      <ProgramHistoryDialog />
    </>
  );
}
