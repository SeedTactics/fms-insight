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
import { ReactNode, memo, useState, useMemo, Suspense, useTransition } from "react";
import {
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  keyframes,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  ListSubheader,
  MenuItem,
  Select,
  Stack,
  styled,
  TableSortLabel,
  TextField,
} from "@mui/material";
import { IconButton } from "@mui/material";
import { Tooltip } from "@mui/material";
import { Typography } from "@mui/material";
import { Table } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableBody } from "@mui/material";
import { MaterialDialog, PartIdenticon } from "../station-monitor/Material.js";
import {
  KeyboardArrowDown as KeyboardArrowDownIcon,
  KeyboardArrowUp as KeyboardArrowUpIcon,
  ImportExport,
  MoreHoriz,
  Warning as WarningIcon,
  Check,
  ErrorOutline,
  SavedSearch,
  Search,
  Clear,
  Loop,
} from "@mui/icons-material";
import { Collapse } from "@mui/material";
import { addWorkorderComment, currentStatus } from "../../cell-status/current-status.js";
import { LazySeq, ToComparableBase } from "@seedtactics/immutable-collections";
import { currentRoute, RouteLocation, useSetTitle } from "../routes.js";
import { IActiveWorkorder, WorkorderSerialCloseout, WorkorderMaterial } from "../../network/api.js";
import { durationToMinutes } from "../../util/parseISODuration.js";
import {
  materialDialogOpen,
  materialInDialogInfo,
  useCompleteCloseout,
} from "../../cell-status/material-details.js";
import copy from "copy-to-clipboard";
import { atom, useAtom, useAtomValue, useSetAtom } from "jotai";
import { atomWithStorage } from "jotai/utils";
import { WorkorderGantt } from "./WorkorderGantt.js";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "../station-monitor/SelectWorkorder.js";
import { LogBackend } from "../../network/backend.js";

const currentWorkorderIdToSearch = atom<string | null, [string | null], void>(
  (get) => {
    const r = get(currentRoute);
    if (r.route === RouteLocation.Operations_CurrentWorkorders) {
      return r.workorder ?? null;
    } else {
      return null;
    }
  },
  (get, set, workId: string | null) => {
    const r = get(currentRoute);
    if (r.route === RouteLocation.Operations_CurrentWorkorders) {
      if (workId === "") {
        workId = null;
      }
      set(currentRoute, {
        route: RouteLocation.Operations_CurrentWorkorders,
        workorder: workId ?? undefined,
      });
    }
  },
);

const recentSearchedWorkorders = atomWithStorage<ReadonlyArray<string>>("recentSearchedWorkorders", []);

const currentSearchedWorkorder = atom<Promise<ReadonlyArray<Readonly<IActiveWorkorder>> | null>>(
  async (get, { signal }) => {
    const workId = get(currentWorkorderIdToSearch);
    if (workId === null) {
      return null;
    }
    return (await LogBackend.getActiveWorkorder(workId, signal)) ?? [];
  },
);

const WorkorderTableRow = styled(TableRow)({
  "& > *": {
    borderBottom: "unset !important",
  },
});

const numFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 });

const WorkorderDetails = memo(function WorkorderDetails({
  workorder,
}: {
  readonly workorder: IActiveWorkorder;
}) {
  const setMatToShow = useSetAtom(materialDialogOpen);
  const setCommentDialog = useSetAtom(workorderCommentDialogAtom);

  const stationNames = LazySeq.ofObject(workorder.activeStationTime ?? {})
    .concat(LazySeq.ofObject(workorder.elapsedStationTime ?? {}))
    .map(([st]) => st)
    .distinctAndSortBy((s) => s);

  function show(wmat: WorkorderMaterial) {
    setMatToShow({
      type: "MatSummary",
      summary: {
        ...wmat,
        partName: workorder.part,
      },
    });
  }

  return (
    <Stack direction="row" flexWrap="wrap" justifyContent="space-around" ml="1em" mr="1em">
      <div>
        <Table size="small" stickyHeader>
          <TableHead>
            <TableRow>
              <TableCell>Serial</TableCell>
              <TableCell>Quarantine?</TableCell>
              <TableCell sx={{ whiteSpace: "nowrap" }}>Inspect Failed?</TableCell>
              <TableCell sx={{ whiteSpace: "nowrap" }}>Close Out</TableCell>
              <TableCell padding="checkbox" />
            </TableRow>
          </TableHead>
          <TableBody>
            {(workorder.material ?? []).map((s) => (
              <TableRow key={s.materialID}>
                <TableCell>{s.serial ?? ""}</TableCell>
                <TableCell sx={{ textAlign: "center" }} padding="checkbox">
                  {s.quarantined ? <SavedSearch fontSize="inherit" /> : ""}
                </TableCell>
                <TableCell sx={{ textAlign: "center" }} padding="checkbox">
                  {s.inspectionFailed ? <ErrorOutline fontSize="inherit" /> : ""}
                </TableCell>
                <TableCell sx={{ textAlign: "center" }} padding="checkbox">
                  {s.closeout === WorkorderSerialCloseout.None ? (
                    ""
                  ) : s.closeout === WorkorderSerialCloseout.ClosedOut ? (
                    <Check fontSize="inherit" />
                  ) : (
                    <ErrorOutline fontSize="inherit" />
                  )}
                </TableCell>
                <TableCell padding="checkbox">
                  <IconButton onClick={() => show(s)} size="large">
                    <MoreHoriz fontSize="inherit" />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <div>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Station</TableCell>
              <TableCell>Active Minutes</TableCell>
              <TableCell>Elapsed Minutes</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {stationNames.map((st) => (
              <TableRow key={st}>
                <TableCell>{st}</TableCell>
                <TableCell>
                  {numFormat.format(durationToMinutes(workorder.activeStationTime?.[st] ?? 0))}
                </TableCell>
                <TableCell>
                  {numFormat.format(durationToMinutes(workorder.elapsedStationTime?.[st] ?? 0))}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <div>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Time</TableCell>
              <TableCell>Comment</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {(workorder.comments ?? []).map((c, idx) => (
              <TableRow key={idx}>
                <TableCell>
                  {c.timeUTC.toLocaleString(undefined, {
                    year: "numeric",
                    month: "numeric",
                    day: "numeric",
                    hour: "numeric",
                    minute: "numeric",
                  })}
                </TableCell>
                <TableCell>{c.comment}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <Button sx={{ mt: "0.5em" }} onClick={() => setCommentDialog(workorder)}>
          Add Comment
        </Button>
      </div>
    </Stack>
  );
});

function utcDateOnlyToString(d: Date | null | undefined): string {
  if (d) {
    return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())).toLocaleDateString();
  } else {
    return "";
  }
}

function isAbnormal(m: WorkorderMaterial): boolean {
  if (m.closeout === WorkorderSerialCloseout.ClosedOut) {
    return false;
  }
  return m.quarantined || m.inspectionFailed || m.closeout === WorkorderSerialCloseout.CloseOutFailed;
}

function WorkorderRow({
  workorder,
  showSim,
}: {
  readonly workorder: IActiveWorkorder;
  readonly showSim: boolean;
}) {
  const [open, setOpen] = useState<boolean>(false);

  const colCnt = showSim ? 10 : 8;

  return (
    <>
      <WorkorderTableRow>
        <TableCell>{workorder.workorderId}</TableCell>
        <TableCell>
          <Box
            sx={{
              display: "flex",
              alignItems: "center",
            }}
          >
            <Box sx={{ mr: "0.2em" }}>
              <PartIdenticon part={workorder.part} size={25} />
            </Box>
            <div>
              <Typography variant="body2" component="span" display="block">
                {workorder.part}
              </Typography>
            </div>
          </Box>
        </TableCell>
        <TableCell>{workorder.dueDate === null ? "" : workorder.dueDate.toLocaleDateString()}</TableCell>
        <TableCell align="right">{workorder.priority}</TableCell>
        <TableCell align="right">{workorder.plannedQuantity}</TableCell>
        <TableCell align="right">{workorder.material?.length ?? 0}</TableCell>
        <TableCell align="right">{workorder.material?.filter(isAbnormal).length ?? 0}</TableCell>
        <TableCell align="right">{workorder.completedQuantity}</TableCell>
        {showSim ? (
          <>
            <TableCell align="right">{utcDateOnlyToString(workorder.simulatedStart)}</TableCell>
            <TableCell align="right">{utcDateOnlyToString(workorder.simulatedFilled)}</TableCell>
          </>
        ) : undefined}
        <TableCell>
          <Tooltip title="Show Details">
            <IconButton size="small" onClick={() => setOpen(!open)}>
              {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
            </IconButton>
          </Tooltip>
        </TableCell>
      </WorkorderTableRow>
      <TableRow>
        <TableCell sx={{ pb: "0", pt: "0" }} colSpan={colCnt}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <WorkorderDetails workorder={workorder} />
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

enum SortColumn {
  WorkorderId,
  Part,
  PlannedQty,
  DueDate,
  Priority,
  CompletedQty,
  AssignedQty,
  AbnormalQty,
  SimulatedStart,
  SimulatedFilled,
}

function sortWorkorders(
  workorders: ReadonlyArray<IActiveWorkorder>,
  sortBy: SortColumn,
  order: "asc" | "desc",
): ReadonlyArray<IActiveWorkorder> {
  let sortCol: ToComparableBase<IActiveWorkorder>;
  switch (sortBy) {
    case SortColumn.WorkorderId:
      sortCol = (j) => j.workorderId;
      break;
    case SortColumn.Part:
      sortCol = (j) => j.part;
      break;
    case SortColumn.PlannedQty:
      sortCol = (j) => j.plannedQuantity;
      break;
    case SortColumn.DueDate:
      sortCol = (j) => j.dueDate.getTime();
      break;
    case SortColumn.Priority:
      sortCol = (j) => j.priority;
      break;
    case SortColumn.CompletedQty:
      sortCol = (j) => j.completedQuantity;
      break;
    case SortColumn.AbnormalQty:
      sortCol = (j) => j.material?.filter(isAbnormal).length ?? 0;
      break;
    case SortColumn.AssignedQty:
      sortCol = (j) => j.material?.length ?? 0;
      break;
    case SortColumn.SimulatedStart:
      sortCol = (j) => j.simulatedStart?.getTime() ?? null;
      break;
    case SortColumn.SimulatedFilled:
      sortCol = (j) => j.simulatedFilled?.getTime() ?? null;
      break;
  }
  return LazySeq.of(workorders).toSortedArray(order === "asc" ? { asc: sortCol } : { desc: sortCol });
}

function WorkorderRows({
  sortBy,
  order,
  showSim,
}: {
  sortBy: SortColumn;
  order: "asc" | "desc";
  showSim: boolean;
}) {
  const currentSt = useAtomValue(currentStatus);
  const searched = useAtomValue(currentSearchedWorkorder);
  const sorted = useMemo(
    () => sortWorkorders(searched ?? currentSt.workorders ?? [], sortBy, order),
    [currentSt.workorders, sortBy, order, searched],
  );

  if (searched && searched.length === 0) {
    return (
      <TableRow>
        <TableCell colSpan={showSim ? 10 : 8}>No workorders found</TableCell>
      </TableRow>
    );
  } else {
    return (
      <>
        {sorted.map((workorder) => (
          <WorkorderRow
            key={`${workorder.workorderId}-${workorder.part}`}
            workorder={workorder}
            showSim={showSim}
          />
        ))}
      </>
    );
  }
}

function copyWorkordersToClipboard(workorders: ReadonlyArray<IActiveWorkorder>, showSim: boolean) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Workorder</th>";
  table += "<th>Part</th>";
  table += "<th>Due Date</th>";
  table += "<th>Priority</th>";
  table += "<th>Planned Qty</th>";
  table += "<th>Completed Qty</th>";
  if (showSim) {
    table += "<th>Simulated Start</th>";
    table += "<th>Simulated Filled</th>";
  }
  table += "<th>Serials</th>";
  table += "<th>Active Time (mins)</th>";
  table += "<th>Elapsed Time (mins)</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (const w of workorders) {
    table += "<tr>";
    table += `<td>${w.workorderId}</td>`;
    table += `<td>${w.part}</td>`;
    table += `<td>${w.dueDate === null ? "" : w.dueDate.toLocaleDateString()}</td>`;
    table += `<td>${w.priority}</td>`;
    table += `<td>${w.plannedQuantity}</td>`;
    table += `<td>${w.completedQuantity}</td>`;
    if (showSim) {
      table += `<td>${utcDateOnlyToString(w.simulatedStart)}</td>`;
      table += `<td>${utcDateOnlyToString(w.simulatedFilled)}</td>`;
    }
    table += `<td>${(w.material ?? []).map((w) => w.serial ?? "").join(";")}</td>`;
    table += `<td>${LazySeq.ofObject(w.activeStationTime ?? {})
      .map(([st, t]) => `${st}: ${durationToMinutes(t)}`)
      .toRArray()
      .join(";")}</td>`;
    table += `<td>${LazySeq.ofObject(w.elapsedStationTime ?? {})
      .map(([st, t]) => `${st}: ${durationToMinutes(t)}`)
      .toRArray()
      .join(";")}</td>`;
    table += "</tr>\n";
  }
  table += "</tbody></table>\n";

  copy(table);
}

function SortColHeader(props: {
  readonly col: SortColumn;
  readonly align: "left" | "right";
  readonly order: "asc" | "desc";
  readonly setOrder: (o: "asc" | "desc") => void;
  readonly sortBy: SortColumn;
  readonly setSortBy: (c: SortColumn) => void;
  readonly children: ReactNode;
  readonly extraIcon?: ReactNode;
}) {
  return (
    <TableCell align={props.align} sortDirection={props.sortBy === props.col ? props.order : false}>
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

const tableOrGantt = atom<"table" | "gantt">("table");

const SimulatedWarning = memo(function SimulatedWarning({ showSim }: { showSim: boolean }) {
  const [selected, setSelected] = useAtom(tableOrGantt);

  return (
    <Stack direction="row" spacing={2} justifyContent="flex-end" alignItems="center">
      {showSim ? (
        <Stack direction="row" spacing={2} alignItems="center" flexGrow={1}>
          <WarningIcon fontSize="small" />
          <Typography variant="caption">Projected dates are estimates</Typography>
        </Stack>
      ) : undefined}
      <FormControl size="small">
        <Select
          variant="outlined"
          value={selected}
          onChange={(e) => setSelected(e.target.value as "table" | "gantt")}
        >
          <MenuItem value="table">Table</MenuItem>
          <MenuItem value="gantt">Gantt</MenuItem>
        </Select>
      </FormControl>
    </Stack>
  );
});

const rotateKeyframes = keyframes`
  0% {
    transform: rotate(0deg);
  }
  100% {
    transform: rotate(360deg);
  }
  `;

const RotatingLoadingIcon = styled(Loop)(() => ({
  animation: `${rotateKeyframes} 1.5s linear infinite`,
}));

function WorkorderSearchButton() {
  const [open, setOpen] = useState<boolean>(false);
  const [loading, startTrans] = useTransition();

  const [searchWorkId, setSearchWorkId] = useAtom(currentWorkorderIdToSearch);
  const [curWorkId, setCurWorkId] = useState<string | null>(null);
  const [prevSearches, setPrevSearches] = useAtom(recentSearchedWorkorders);

  function search(workId: string | null) {
    if (workId !== null && workId !== "") {
      startTrans(() => {
        setSearchWorkId(workId);
      });
      setOpen(false);
      setCurWorkId(null);
      setPrevSearches((prev) => {
        const n = prev.filter((w) => w !== workId);
        n.unshift(workId);
        return n.slice(0, 5);
      });
    }
  }

  return (
    <>
      {loading ? (
        <Tooltip title={"Searching.... Click to cancel"}>
          <IconButton size="small" onClick={() => setSearchWorkId(null)}>
            <RotatingLoadingIcon fontSize="inherit" />
          </IconButton>
        </Tooltip>
      ) : searchWorkId ? (
        <Tooltip title="Clear Search">
          <IconButton size="small" onClick={() => setSearchWorkId(null)}>
            <Clear fontSize="inherit" />
          </IconButton>
        </Tooltip>
      ) : (
        <Tooltip title="Search">
          <IconButton
            size="small"
            onClick={(e) => {
              setOpen(true);
              e.currentTarget.blur();
            }}
          >
            <Search fontSize="inherit" />
          </IconButton>
        </Tooltip>
      )}
      <Dialog open={open} onClose={() => setOpen(false)}>
        <DialogTitle>Search Workorder</DialogTitle>
        <DialogContent>
          <TextField
            variant="outlined"
            fullWidth
            autoFocus
            sx={{ mt: "0.5em", minWidth: "15em" }}
            label="Workorder ID"
            value={curWorkId ?? ""}
            onChange={(e) => setCurWorkId(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                search(curWorkId);
                e.preventDefault();
              }
            }}
          />

          {prevSearches.length > 0 ? (
            <>
              <List dense sx={{ mt: "1em" }} subheader={<ListSubheader>Previous Searches</ListSubheader>}>
                {prevSearches.map((w) => (
                  <ListItem key={w} onClick={() => search(w)}>
                    <ListItemButton>
                      <ListItemText primary={w} />
                    </ListItemButton>
                  </ListItem>
                ))}
              </List>
            </>
          ) : undefined}
        </DialogContent>
        <DialogActions>
          <Button color="primary" onClick={() => search(curWorkId)}>
            Search
          </Button>
          <Button color="secondary" onClick={() => setOpen(false)}>
            Cancel
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

const WorkorderHeader = memo(function WorkorderHeader(props: {
  readonly workorders: ReadonlyArray<IActiveWorkorder>;
  readonly showSim: boolean;
  readonly order: "asc" | "desc";
  readonly setOrder: (o: "asc" | "desc") => void;
  readonly sortBy: SortColumn;
  readonly setSortBy: (c: SortColumn) => void;
  readonly disableSearch: boolean | undefined;
}) {
  const sort = {
    sortBy: props.sortBy,
    setSortBy: props.setSortBy,
    order: props.order,
    setOrder: props.setOrder,
  };

  return (
    <TableHead>
      <TableRow>
        <SortColHeader
          align="left"
          col={SortColumn.WorkorderId}
          {...sort}
          extraIcon={props.disableSearch ? undefined : <WorkorderSearchButton />}
        >
          Workorder
        </SortColHeader>
        <SortColHeader align="left" col={SortColumn.Part} {...sort}>
          Part
        </SortColHeader>
        <SortColHeader align="left" col={SortColumn.DueDate} {...sort}>
          Due Date
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.Priority} {...sort}>
          Priority
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.PlannedQty} {...sort}>
          Planned Quantity
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.AssignedQty} {...sort}>
          Started Quantity
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.AbnormalQty} {...sort}>
          Abnormal Quantity
        </SortColHeader>
        <SortColHeader align="right" col={SortColumn.CompletedQty} {...sort}>
          Completed Quantity
        </SortColHeader>
        {props.showSim ? (
          <>
            <SortColHeader align="right" col={SortColumn.SimulatedStart} {...sort}>
              Projected Start
            </SortColHeader>
            <SortColHeader align="right" col={SortColumn.SimulatedFilled} {...sort}>
              Projected Filled
            </SortColHeader>
          </>
        ) : undefined}
        <TableCell>
          <Tooltip title="Copy to Clipboard">
            <IconButton
              style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
              size="large"
              onClick={() => copyWorkordersToClipboard(props.workorders, props.showSim)}
            >
              <ImportExport />
            </IconButton>
          </Tooltip>
        </TableCell>
      </TableRow>
    </TableHead>
  );
});

const workorderCommentDialogAtom = atom<Readonly<IActiveWorkorder> | null>(null);

function WorkorderCommentDialog() {
  const [workorder, setWorkorder] = useAtom(workorderCommentDialogAtom);
  const [comment, setComment] = useState<string | null>(null);
  const addComment = useSetAtom(addWorkorderComment);

  function close() {
    setWorkorder(null);
    setComment(null);
  }

  function save() {
    if (workorder && comment && comment !== "") {
      addComment({ workorder: workorder.workorderId, comment: comment });
    }
    close();
  }

  return (
    <Dialog open={workorder !== null} onClose={close}>
      {workorder !== null ? (
        <>
          <DialogTitle>
            <div style={{ display: "flex", alignItems: "center" }}>
              <div>
                <PartIdenticon part={workorder.part} size={40} />
              </div>
              <div style={{ marginLeft: "1em", flexGrow: 1 }}>Add Comment For {workorder.workorderId}</div>
            </div>
          </DialogTitle>
          <DialogContent>
            <TextField
              variant="outlined"
              sx={{ mt: "1em" }}
              fullWidth
              autoFocus
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && comment !== null && comment !== "") {
                  save();
                  e.preventDefault();
                }
              }}
            />
          </DialogContent>
          <DialogActions>
            <Button color="primary" onClick={save}>
              Save Comment
            </Button>
            <Button color="primary" onClick={close}>
              Cancel
            </Button>
          </DialogActions>
        </>
      ) : undefined}
    </Dialog>
  );
}

function SearchingWorkorder() {
  const [workId, setWorkId] = useAtom(currentWorkorderIdToSearch);
  return (
    <TableRow>
      <TableCell colSpan={10}>
        <Stack direction="column" mt="3em" flex="flex" alignItems="center" width="100%">
          <Stack direction="row" spacing="3">
            <CircularProgress />
            {workId && workId !== "" ? (
              <Typography variant="h6">Searching for workorder {workId}</Typography>
            ) : (
              <Typography variant="h6">Searching</Typography>
            )}
          </Stack>
          <Button onClick={() => setWorkId(null)}>Cancel</Button>
        </Stack>
      </TableCell>
    </TableRow>
  );
}

function WorkorderTable({
  showSim,
  disableSearch,
}: {
  showSim: boolean;
  disableSearch: boolean | undefined;
}) {
  const [sortBy, setSortBy] = useState<SortColumn>(SortColumn.WorkorderId);
  const [order, setOrder] = useState<"asc" | "desc">("asc");
  const currentSt = useAtomValue(currentStatus);
  return (
    <Table stickyHeader>
      <WorkorderHeader
        workorders={currentSt?.workorders ?? []}
        sortBy={sortBy}
        setSortBy={setSortBy}
        showSim={showSim}
        order={order}
        setOrder={setOrder}
        disableSearch={disableSearch}
      />
      <TableBody>
        <Suspense fallback={<SearchingWorkorder />}>
          <WorkorderRows sortBy={sortBy} order={order} showSim={showSim} />
        </Suspense>
      </TableBody>
    </Table>
  );
}

function WorkSerialButtons() {
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

const WorkSerialDialog = memo(function WorkSerialDialog() {
  return <MaterialDialog buttons={<WorkSerialButtons />} />;
});

export const CurrentWorkordersPage = memo(function RecentWorkordersPage({
  disableSearch,
}: {
  disableSearch?: boolean;
}): ReactNode {
  useSetTitle("Workorders");
  const currentSt = useAtomValue(currentStatus);
  const display = useAtomValue(tableOrGantt);

  const showSim = useMemo(
    () => currentSt.workorders?.some((w) => !!w.simulatedStart || !!w.simulatedFilled) ?? false,
    [currentSt.workorders],
  );

  return (
    <Box component="main" padding="24px">
      {showSim ? <SimulatedWarning showSim={showSim} /> : undefined}
      {display === "table" ? (
        <WorkorderTable showSim={showSim} disableSearch={disableSearch} />
      ) : (
        <WorkorderGantt />
      )}
      <WorkorderCommentDialog />
      <WorkSerialDialog />
      <SelectWorkorderDialog />
    </Box>
  );
});
