/* Copyright (c) 2019, John Lenz

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
import Table from "@material-ui/core/Table";
import TableBody from "@material-ui/core/TableBody";
import TableCell from "@material-ui/core/TableCell";
import TableHead from "@material-ui/core/TableHead";
import TableRow from "@material-ui/core/TableRow";
import TableSortLabel from "@material-ui/core/TableSortLabel";
import Tooltip from "@material-ui/core/Tooltip";
import IconButton from "@material-ui/core/IconButton";
import FirstPageIcon from "@material-ui/icons/FirstPage";
import KeyboardArrowLeft from "@material-ui/icons/KeyboardArrowLeft";
import KeyboardArrowRight from "@material-ui/icons/KeyboardArrowRight";
import TextField from "@material-ui/core/TextField";
import LastPageIcon from "@material-ui/icons/LastPage";
import CalendarIcon from "@material-ui/icons/CalendarToday";
import Toolbar from "@material-ui/core/Toolbar";
import Typography from "@material-ui/core/Typography";
import Select from "@material-ui/core/Select";
import ZoomOutIcon from "@material-ui/icons/ZoomOut";
import InputBase from "@material-ui/core/InputBase";
import Dialog from "@material-ui/core/Dialog";
import DialogTitle from "@material-ui/core/DialogTitle";
import DialogContent from "@material-ui/core/DialogContent";
import DialogContentText from "@material-ui/core/DialogContentText";
import MenuItem from "@material-ui/core/MenuItem";
import Button from "@material-ui/core/Button";
import { HashMap } from "prelude-ts";

import { PartCycleData } from "../../data/events.cycles";
import { LazySeq } from "../../data/lazyseq";
import { addDays, format, addHours } from "date-fns";

enum ColumnId {
  Date,
  Part,
  Station,
  Pallet,
  Serial,
  Workorder,
  ElapsedMin,
  ActiveMin
}

interface Column {
  readonly id: ColumnId;
  readonly numeric: boolean;
  readonly label: string;
  readonly getDisplay: (c: PartCycleData) => string;
  readonly getForSort?: (c: PartCycleData) => string | number;
}

const columns: ReadonlyArray<Column> = [
  { id: ColumnId.Date, numeric: false, label: "Date", getDisplay: c => c.x.toString(), getForSort: c => c.x.getTime() },
  { id: ColumnId.Part, numeric: false, label: "Part", getDisplay: c => c.part },
  {
    id: ColumnId.Station,
    numeric: false,
    label: "Station",
    getDisplay: c => c.stationGroup + " " + c.stationNumber.toString()
  },
  {
    id: ColumnId.Pallet,
    numeric: false,
    label: "Pallet",
    getDisplay: c => c.pallet
  },
  {
    id: ColumnId.Serial,
    numeric: false,
    label: "Serial",
    getDisplay: c => c.serial || ""
  },
  {
    id: ColumnId.Workorder,
    numeric: false,
    label: "Workorder",
    getDisplay: c => c.workorder || ""
  },
  {
    id: ColumnId.ElapsedMin,
    numeric: true,
    label: "Elapsed Min",
    getDisplay: c => c.y.toFixed(1)
  },
  {
    id: ColumnId.ActiveMin,
    numeric: true,
    label: "Active Min",
    getDisplay: c => c.active.toFixed(1)
  }
];

interface StationTableHeadProps {
  readonly orderBy: ColumnId;
  readonly order: "asc" | "desc";
  readonly onRequestSort: (id: ColumnId) => void;
}

function StationTableHead(props: StationTableHeadProps) {
  return (
    <TableHead>
      <TableRow>
        {columns.map(col => (
          <TableCell
            key={col.id}
            align={col.numeric ? "right" : "left"}
            sortDirection={props.orderBy === col.id ? props.order : false}
          >
            <Tooltip title="Sort" placement={col.numeric ? "bottom-end" : "bottom-start"} enterDelay={300}>
              <TableSortLabel
                active={props.orderBy === col.id}
                direction={props.order}
                onClick={() => props.onRequestSort(col.id)}
              >
                {col.label}
              </TableSortLabel>
            </Tooltip>
          </TableCell>
        ))}
      </TableRow>
    </TableHead>
  );
}

interface StationTableActionsProps {
  readonly page: number;
  readonly count: number;
  readonly rowsPerPage: number;
  readonly last30_days: boolean;
  readonly setPage: (page: number) => void;
  readonly setRowsPerPage: (rpp: number) => void;
  readonly end_time: Date;
  readonly set_date_zoom_range: (p: { zoom?: { start: Date; end: Date } }) => void;
  readonly openDateSelect: () => void;
}

function StationTableActions(props: StationTableActionsProps) {
  function setDateRange(numDaysBack: number) {
    const now = new Date();
    props.set_date_zoom_range({ zoom: { start: addDays(now, -numDaysBack), end: addHours(now, 1) } });
  }
  return (
    <Toolbar>
      <Typography color="textSecondary" variant="caption">
        Rows per page:
      </Typography>
      <Select
        style={{ marginLeft: 8, marginRight: "1em" }}
        value={props.rowsPerPage}
        SelectDisplayProps={{ style: { color: "rgba(0, 0, 0, 0.54)" } }}
        input={<InputBase />}
        onChange={evt => props.setRowsPerPage(parseInt(evt.target.value, 10))}
      >
        {[10, 15, 20, 50].map(rowsPerPageOption => (
          <MenuItem key={rowsPerPageOption} value={rowsPerPageOption}>
            {rowsPerPageOption}
          </MenuItem>
        ))}
      </Select>
      <Typography color="textSecondary" variant="caption">
        {`${props.count === 0 ? 0 : props.page * props.rowsPerPage + 1}-${Math.min(
          props.count,
          (props.page + 1) * props.rowsPerPage
        )} of ${props.count}`}
      </Typography>
      <IconButton onClick={() => props.setPage(0)} disabled={props.page === 0} aria-label="First Page">
        <FirstPageIcon />
      </IconButton>
      <IconButton onClick={() => props.setPage(props.page - 1)} disabled={props.page === 0} aria-label="Previous Page">
        <KeyboardArrowLeft />
      </IconButton>
      <IconButton
        onClick={() => props.setPage(props.page + 1)}
        disabled={props.page >= Math.ceil(props.count / props.rowsPerPage) - 1}
        aria-label="Next Page"
      >
        <KeyboardArrowRight />
      </IconButton>
      <IconButton
        onClick={() => props.setPage(Math.max(0, Math.ceil(props.count / props.rowsPerPage) - 1))}
        disabled={props.page >= Math.ceil(props.count / props.rowsPerPage) - 1}
        aria-label="Last Page"
      >
        <LastPageIcon />
      </IconButton>
      <div style={{ flexGrow: 1 }} />
      {props.last30_days ? (
        <>
          <Tooltip title="Last 24 hours">
            <Button onClick={() => setDateRange(1)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              24h
            </Button>
          </Tooltip>
          <Tooltip title="Last 2 days">
            <Button onClick={() => setDateRange(2)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              2d
            </Button>
          </Tooltip>
          <Tooltip title="Last 3 days">
            <Button onClick={() => setDateRange(3)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              3d
            </Button>
          </Tooltip>
          <Tooltip title="Last 4 days">
            <Button onClick={() => setDateRange(4)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              4d
            </Button>
          </Tooltip>
          <Tooltip title="Last 5 days">
            <Button onClick={() => setDateRange(5)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              5d
            </Button>
          </Tooltip>
          <Tooltip title="Last 6 days">
            <Button onClick={() => setDateRange(6)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              6d
            </Button>
          </Tooltip>
          <Tooltip title="Last 1 week">
            <Button onClick={() => setDateRange(7)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              1w
            </Button>
          </Tooltip>
          <Tooltip title="Last 2 weeks">
            <Button onClick={() => setDateRange(7 * 2)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              2w
            </Button>
          </Tooltip>
          <Tooltip title="Last 3 weeks">
            <Button onClick={() => setDateRange(7 * 3)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
              3w
            </Button>
          </Tooltip>
          <Tooltip title="Last 30 days">
            <Button
              onClick={() => props.set_date_zoom_range({ zoom: undefined })}
              style={{ color: "rgba(0, 0, 0, 0.54)" }}
            >
              30d
            </Button>
          </Tooltip>
        </>
      ) : (
        <>
          <Tooltip title="Set Date Range">
            <IconButton onClick={props.openDateSelect}>
              <CalendarIcon />
            </IconButton>
          </Tooltip>
          <Tooltip title="Reset Date Range">
            <IconButton onClick={() => props.set_date_zoom_range({ zoom: undefined })}>
              <ZoomOutIcon />
            </IconButton>
          </Tooltip>
        </>
      )}
    </Toolbar>
  );
}

interface SetDateRangeProps {
  readonly open: boolean;
  readonly close: () => void;
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: (p: { zoom?: { start: Date; end: Date } }) => void;
}

function encodeDateForInput(d: Date): string {
  return format(d, "YYYY-MM-DD");
}

function SetDateRange(props: SetDateRangeProps) {
  return (
    <Dialog onClose={props.close} open={props.open}>
      <DialogTitle>Set Zoom</DialogTitle>
      <DialogContent>
        <DialogContentText>
          <em>You can also zoom by clicking and dragging on the chart.</em>
        </DialogContentText>
        <div>
          <div style={{ marginTop: "0.5em" }}>
            <TextField
              label="Starting Day"
              type="date"
              inputProps={{ step: 1 }}
              value={encodeDateForInput(
                props.current_date_zoom ? props.current_date_zoom.start : props.default_date_range[0]
              )}
              onChange={e =>
                props.set_date_zoom_range({
                  zoom: {
                    start: new Date((e.target as HTMLInputElement).valueAsDate),
                    end: props.current_date_zoom ? props.current_date_zoom.end : props.default_date_range[1]
                  }
                })
              }
            />
          </div>
          <div style={{ marginTop: "0.5em" }}>
            <TextField
              label="Ending Day"
              type="date"
              inputProps={{ step: 1 }}
              value={encodeDateForInput(
                props.current_date_zoom ? props.current_date_zoom.end : props.default_date_range[1]
              )}
              onChange={e =>
                props.set_date_zoom_range({
                  zoom: {
                    start: props.current_date_zoom ? props.current_date_zoom.start : props.default_date_range[0],
                    end: new Date((e.target as HTMLInputElement).valueAsDate)
                  }
                })
              }
            />
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

interface StationDataTableProps {
  readonly points: HashMap<string, ReadonlyArray<PartCycleData>>;
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: (p: { zoom?: { start: Date; end: Date } }) => void;
  readonly last30_days: boolean;
}

function extractData(
  points: HashMap<string, ReadonlyArray<PartCycleData>>,
  currentZoom: { start: Date; end: Date } | undefined,
  orderBy: ColumnId,
  order: "asc" | "desc"
): ReadonlyArray<PartCycleData> {
  let getData: ((p: PartCycleData) => string | number) | undefined;
  for (let col of columns) {
    if (col.id === orderBy) {
      getData = col.getForSort || col.getDisplay;
    }
  }
  if (getData === undefined) {
    getData = columns[0].getForSort || columns[0].getDisplay;
  }
  const getDataC = getData;

  const data = LazySeq.ofIterable(points.valueIterable()).flatMap(x => x);
  const arr = currentZoom
    ? data.filter(p => p.x >= currentZoom.start && p.x <= currentZoom.end).toArray()
    : data.toArray();
  return arr.sort((a, b) => {
    const aVal = getDataC(a);
    const bVal = getDataC(b);
    if (aVal === bVal) {
      // sort by date
      if (order === "desc") {
        return b.x.getTime() - a.x.getTime();
      } else {
        return a.x.getTime() - b.x.getTime();
      }
    } else {
      if (order === "desc") {
        return aVal > bVal ? -1 : 1;
      } else {
        return aVal > bVal ? 1 : -1;
      }
    }
  });
}

export default React.memo(function StationDataTable(props: StationDataTableProps) {
  const [orderBy, setOrderBy] = React.useState(ColumnId.Date);
  const [order, setOrder] = React.useState<"asc" | "desc">("asc");
  const [page, setPage] = React.useState(0);
  const [rowsPerPage, setRowsPerPage] = React.useState(10);
  const [dialogOpen, setDialogOpen] = React.useState(false);

  function handleRequestSort(property: ColumnId) {
    if (orderBy === property) {
      setOrder(order === "asc" ? "desc" : "asc");
    } else {
      setOrderBy(property);
      setOrder("asc");
    }
  }

  const allData = extractData(props.points, props.current_date_zoom, orderBy, order);
  const totalDataLength = allData.length;
  const pageData = allData.slice(page * rowsPerPage, (page + 1) * rowsPerPage);
  return (
    <div>
      <Table>
        <StationTableHead onRequestSort={handleRequestSort} orderBy={orderBy} order={order} />
        <TableBody>
          {pageData.map((row, idx) => (
            <TableRow key={idx}>
              {columns.map(col => (
                <TableCell key={col.id} align={col.numeric ? "right" : "left"}>
                  {col.getDisplay(row)}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <StationTableActions
        page={page}
        count={totalDataLength}
        last30_days={props.last30_days}
        rowsPerPage={rowsPerPage}
        setPage={setPage}
        setRowsPerPage={setRowsPerPage}
        set_date_zoom_range={props.set_date_zoom_range}
        end_time={props.default_date_range[1]}
        openDateSelect={() => setDialogOpen(true)}
      />
      <SetDateRange
        open={dialogOpen}
        close={() => setDialogOpen(false)}
        set_date_zoom_range={props.set_date_zoom_range}
        default_date_range={props.default_date_range}
        current_date_zoom={props.current_date_zoom}
      />
    </div>
  );
});
