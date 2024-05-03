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
import { Dialog, DialogActions, DialogContent, DialogTitle, styled, TableBody } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableSortLabel } from "@mui/material";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { Toolbar } from "@mui/material";
import { Typography } from "@mui/material";
import { Select } from "@mui/material";
import { InputBase } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Button } from "@mui/material";
import { Calendar } from "react-calendar";
import {
  MoreHoriz,
  FirstPage as FirstPageIcon,
  KeyboardArrowLeft,
  KeyboardArrowRight,
  LastPage as LastPageIcon,
  ZoomOut as ZoomOutIcon,
  ZoomIn as ZoomInIcon,
  SkipPrevious as SkipPrevIcon,
  SkipNext as SkipNextIcon,
  ImportExport,
} from "@mui/icons-material";
import copy from "copy-to-clipboard";

import { addDays, addHours, addMonths } from "date-fns";
import { ToComparable, ToComparableBase } from "@seedtactics/immutable-collections";
import { SelectedAnalysisPeriod } from "../../network/load-specific-month.js";

export interface Column<Id, Row> {
  readonly id: Id;
  readonly numeric: boolean;
  readonly label: string;
  readonly getDisplay: (c: Row) => string;
  readonly getForSort?: ToComparableBase<Row>;
  readonly getForExport?: (c: Row) => string;
  readonly Cell?: React.ComponentType<{ readonly row: Row }>;
  readonly expanded?: boolean;
  readonly ignoreDuringExport?: boolean;
}

export type ColSort<Id, Row> = {
  readonly orderBy: Id;
  readonly order: "asc" | "desc";
  readonly sortOn: ToComparable<Row>;
  readonly handleRequestSort: (property: Id) => void;
};

export type TablePage = {
  readonly page: number;
  readonly rowsPerPage: number;
  readonly setPage: (p: number) => void;
  readonly setRowsPerPage: (r: React.SetStateAction<number>) => void;
};

export enum DataTableActionZoomType {
  Last30Days = "Last30",
  ZoomIntoRange = "IntoRange",
  ExtendDays = "Extend",
}

export interface DataTableActionZoomIntoRange {
  readonly type: DataTableActionZoomType.ZoomIntoRange;
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: (p: { start: Date; end: Date } | undefined) => void;
}

export type DataTableActionZoom =
  | {
      readonly type: DataTableActionZoomType.Last30Days;
      readonly set_days_back: (numDaysBack: number | null) => void;
    }
  | DataTableActionZoomIntoRange
  | {
      readonly type: DataTableActionZoomType.ExtendDays;
      readonly curStart: Date;
      readonly curEnd: Date;
      readonly extend: (numDays: number) => void;
    };

export type TableZoom = {
  readonly zoomRange: { readonly start: Date; readonly end: Date } | undefined;
  readonly zoom: DataTableActionZoom | undefined;
};

const DataCell = styled(TableCell, { shouldForwardProp: (p) => p !== "expand" })<{
  expand?: boolean;
}>(({ expand }) => ({
  width: expand ? "100%" : undefined,
}));

export interface DataTableHeadProps<Id, Row> {
  readonly sort: ColSort<Id, Row>;
  readonly columns: ReadonlyArray<Column<Id, Row>>;
  readonly showDetailsCol: boolean;
  readonly copyToClipboardRows?: Iterable<Row>;
}

export function DataTableHead<Id extends string | number, Row>(
  props: DataTableHeadProps<Id, Row>,
): JSX.Element {
  const clipboardRows = props.copyToClipboardRows;
  return (
    <TableHead>
      <TableRow>
        {props.columns.map((col) => (
          <DataCell
            key={col.id}
            align={col.numeric ? "right" : "left"}
            expand={col.expanded}
            sortDirection={props.sort.orderBy === col.id ? props.sort.order : false}
          >
            <Tooltip title="Sort" placement={col.numeric ? "bottom-end" : "bottom-start"} enterDelay={300}>
              <TableSortLabel
                active={props.sort.orderBy === col.id}
                direction={props.sort.order}
                onClick={() => props.sort.handleRequestSort(col.id)}
              >
                {col.label}
              </TableSortLabel>
            </Tooltip>
          </DataCell>
        ))}
        {props.showDetailsCol ? (
          <DataCell padding="checkbox">
            {clipboardRows ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                  onClick={() => copyTableToClipboard(props.columns, clipboardRows)}
                  size="large"
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : undefined}
          </DataCell>
        ) : undefined}
      </TableRow>
    </TableHead>
  );
}

const dateFormat = new Intl.DateTimeFormat([], { year: "numeric", month: "short", day: "numeric" });
const monthFormat = new Intl.DateTimeFormat([], { year: "numeric", month: "long" });

const StyledCalendar = styled(Calendar)(({ theme }) => ({
  width: "350px",
  "& .react-calendar__month-view__weekdays": {
    textAlign: "center",
    textTransform: "uppercase",
    fontWeight: "bold",
  },
  "& .react-calendar__tile": {
    textAlign: "center",
    padding: ".75em .5em",
    margin: 0,
    border: 0,
    background: "none",
    outline: "none",
    cursor: "pointer",
    "&:hover": {
      backgroundColor: "rgb(230, 230, 230)",
    },
    "&--active": {
      backgroundColor: theme.palette.primary.light,
      "&:hover": {
        backgroundColor: theme.palette.primary.dark,
      },
    },
  },
  // react-calendar__tile--hover is when selecting range of days.
  "&.react-calendar--selectRange .react-calendar__tile--hover": {
    backgroundColor: "rgb(230, 230, 230)",
  },
}));

interface SelectDateRangeProps {
  readonly zoom: DataTableActionZoomIntoRange;
}

function SelectDateRange(props: SelectDateRangeProps) {
  const [open, setOpen] = React.useState(false);
  const start = props.zoom.current_date_zoom
    ? props.zoom.current_date_zoom.start
    : props.zoom.default_date_range[0];
  const end = addDays(
    props.zoom.current_date_zoom ? props.zoom.current_date_zoom.end : props.zoom.default_date_range[1],
    -1,
  );

  function onChange(d: ReadonlyArray<Date>) {
    props.zoom.set_date_zoom_range({
      start: d[0],
      end: addDays(d[1], 1),
    });
    setOpen(false);
  }

  // @types/react-calendar has the wrong type for onChange
  // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-explicit-any
  const matchingOnChange: (d: Date | ReadonlyArray<Date | null> | null) => void = onChange as any;

  return (
    <>
      <span>
        {dateFormat.format(props.zoom.current_date_zoom?.start ?? props.zoom.default_date_range[0])} -{" "}
        {dateFormat.format(props.zoom.current_date_zoom?.end ?? props.zoom.default_date_range[1])}
      </span>
      <Tooltip title="Zoom To Date Range">
        <IconButton onClick={() => setOpen(true)} size="large">
          <ZoomInIcon />
        </IconButton>
      </Tooltip>
      <Tooltip title="Reset Date Range">
        <IconButton onClick={() => props.zoom.set_date_zoom_range(undefined)} size="large">
          <ZoomOutIcon />
        </IconButton>
      </Tooltip>
      <Dialog open={open} onClose={() => setOpen(false)}>
        <DialogTitle>Select Date Range {monthFormat.format(props.zoom.default_date_range[0])}</DialogTitle>
        <DialogContent>
          <StyledCalendar
            minDate={props.zoom.default_date_range[0]}
            maxDate={props.zoom.default_date_range[1]}
            calendarType="gregory"
            selectRange
            showNavigation={false}
            showNeighboringMonth={false}
            value={[start, end]}
            onChange={matchingOnChange}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

export interface DataTableActionsProps {
  readonly tpage: TablePage;
  readonly count: number;
  readonly zoom?: DataTableActionZoom;
}

export const DataTableActions = React.memo(function DataTableActions({
  zoom,
  tpage,
  count,
}: DataTableActionsProps): JSX.Element {
  let zoomCtrl;
  if (zoom && zoom.type === DataTableActionZoomType.Last30Days) {
    zoomCtrl = (
      <>
        <Tooltip title="Last 24 hours">
          <Button onClick={() => zoom.set_days_back(1)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            24h
          </Button>
        </Tooltip>
        <Tooltip title="Last 2 days">
          <Button onClick={() => zoom.set_days_back(2)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            2d
          </Button>
        </Tooltip>
        <Tooltip title="Last 3 days">
          <Button onClick={() => zoom.set_days_back(3)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            3d
          </Button>
        </Tooltip>
        <Tooltip title="Last 4 days">
          <Button onClick={() => zoom.set_days_back(4)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            4d
          </Button>
        </Tooltip>
        <Tooltip title="Last 5 days">
          <Button onClick={() => zoom.set_days_back(5)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            5d
          </Button>
        </Tooltip>
        <Tooltip title="Last 6 days">
          <Button onClick={() => zoom.set_days_back(6)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            6d
          </Button>
        </Tooltip>
        <Tooltip title="Last 1 week">
          <Button onClick={() => zoom.set_days_back(7)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            1w
          </Button>
        </Tooltip>
        <Tooltip title="Last 2 weeks">
          <Button onClick={() => zoom.set_days_back(7 * 2)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            2w
          </Button>
        </Tooltip>
        <Tooltip title="Last 3 weeks">
          <Button onClick={() => zoom.set_days_back(7 * 3)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            3w
          </Button>
        </Tooltip>
        <Tooltip title="Last 30 days">
          <Button onClick={() => zoom.set_days_back(null)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            30d
          </Button>
        </Tooltip>
      </>
    );
  } else if (zoom && zoom.type === DataTableActionZoomType.ZoomIntoRange) {
    zoomCtrl = <SelectDateRange zoom={zoom} />;
  } else if (zoom && zoom.type === DataTableActionZoomType.ExtendDays) {
    zoomCtrl = (
      <>
        <Tooltip title="Extend 1 day previous">
          <IconButton onClick={() => zoom.extend(-1)} size="large">
            <SkipPrevIcon />
          </IconButton>
        </Tooltip>
        <Typography variant="caption">
          {zoom.curStart.toLocaleDateString() + " to " + zoom.curEnd.toLocaleDateString()}
        </Typography>
        <Tooltip title="Extend 1 day">
          <IconButton onClick={() => zoom.extend(1)} size="large">
            <SkipNextIcon />
          </IconButton>
        </Tooltip>
      </>
    );
  }

  return (
    <Toolbar>
      <Typography color="textSecondary" variant="caption">
        Rows per page:
      </Typography>
      <Select
        style={{ marginLeft: 8, marginRight: "1em" }}
        value={tpage.rowsPerPage}
        SelectDisplayProps={{ style: { color: "rgba(0, 0, 0, 0.54)" } }}
        input={<InputBase />}
        onChange={(evt) => {
          const rpp = parseInt(evt.target.value as string, 10);
          tpage.setRowsPerPage(rpp);
          const maxPage = Math.ceil(count / rpp) - 1;
          if (tpage.page > maxPage) {
            tpage.setPage(maxPage);
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
        {`${count === 0 ? 0 : tpage.page * tpage.rowsPerPage + 1}-${Math.min(
          count,
          (tpage.page + 1) * tpage.rowsPerPage,
        )} of ${count}`}
      </Typography>
      <IconButton
        onClick={() => tpage.setPage(0)}
        disabled={tpage.page === 0}
        aria-label="First Page"
        size="large"
      >
        <FirstPageIcon />
      </IconButton>
      <IconButton
        onClick={() => tpage.setPage(tpage.page - 1)}
        disabled={tpage.page === 0}
        aria-label="Previous Page"
        size="large"
      >
        <KeyboardArrowLeft />
      </IconButton>
      <IconButton
        onClick={() => tpage.setPage(tpage.page + 1)}
        disabled={tpage.page >= Math.ceil(count / tpage.rowsPerPage) - 1}
        aria-label="Next Page"
        size="large"
      >
        <KeyboardArrowRight />
      </IconButton>
      <IconButton
        onClick={() => tpage.setPage(Math.max(0, Math.ceil(count / tpage.rowsPerPage) - 1))}
        disabled={tpage.page >= Math.ceil(count / tpage.rowsPerPage) - 1}
        aria-label="Last Page"
        size="large"
      >
        <LastPageIcon />
      </IconButton>
      {zoom ? (
        <>
          <div style={{ flexGrow: 1 }} />
          {zoomCtrl}
        </>
      ) : undefined}
    </Toolbar>
  );
});

export interface DataTableBodyProps<Id, Row> {
  readonly pageData: Iterable<Row>;
  readonly columns: ReadonlyArray<Column<Id, Row>>;
  readonly onClickDetails?: (event: React.MouseEvent, r: Row) => void;
  readonly rowsPerPage?: number;
  readonly emptyMessage?: string;
}

export class DataTableBody<Id extends string | number, Row> extends React.PureComponent<
  DataTableBodyProps<Id, Row>
> {
  override render(): JSX.Element {
    const onClickDetails = this.props.onClickDetails;
    const rows = [...this.props.pageData];
    const emptyRows =
      this.props.rowsPerPage !== undefined ? Math.max(0, this.props.rowsPerPage - rows.length) : 0;
    return (
      <TableBody>
        {rows.map((row, idx) => (
          <TableRow key={idx}>
            {this.props.columns.map((col) => (
              <DataCell key={col.id} align={col.numeric ? "right" : "left"} expand={col.expanded}>
                {col.Cell ? <col.Cell row={row} /> : col.getDisplay(row)}
              </DataCell>
            ))}
            {onClickDetails ? (
              <DataCell padding="checkbox">
                <IconButton onClick={(e) => onClickDetails(e, row)} size="large">
                  <MoreHoriz />
                </IconButton>
              </DataCell>
            ) : undefined}
          </TableRow>
        ))}
        {emptyRows > 0 ? (
          <TableRow style={{ height: 53 * emptyRows }}>
            <TableCell
              colSpan={this.props.columns.length + (this.props.onClickDetails ? 1 : 0)}
              style={{ textAlign: "center" }}
            >
              {rows.length === 0 ? this.props.emptyMessage : undefined}
            </TableCell>
          </TableRow>
        ) : undefined}
      </TableBody>
    );
  }
}

export function useTableZoomForPeriod(period: SelectedAnalysisPeriod): TableZoom {
  const [curZoom, setCurZoom] = React.useState<{ start: Date; end: Date } | undefined>(undefined);

  return React.useMemo(() => {
    let zoom: DataTableActionZoom;
    if (period.type === "Last30") {
      zoom = {
        type: DataTableActionZoomType.Last30Days,
        set_days_back: (numDaysBack) => {
          if (numDaysBack) {
            const now = new Date();
            setCurZoom({ start: addDays(now, -numDaysBack), end: addHours(now, 1) });
          } else {
            setCurZoom(undefined);
          }
        },
      };
    } else {
      zoom = {
        type: DataTableActionZoomType.ZoomIntoRange,
        default_date_range: [period.month, addMonths(period.month, 1)],
        current_date_zoom: curZoom,
        set_date_zoom_range: setCurZoom,
      };
    }

    return { zoom, zoomRange: curZoom };
  }, [period, curZoom, setCurZoom]);
}

export function useTablePage(): TablePage {
  const [page, setPage] = React.useState(0);
  const [rowsPerPage, setRowsPerPage] = React.useState(10);

  return React.useMemo(
    () => ({ page, setPage, rowsPerPage, setRowsPerPage }),
    [page, setPage, rowsPerPage, setRowsPerPage],
  );
}

export function useColSort<Id, Row>(defSortCol: Id, cols: ReadonlyArray<Column<Id, Row>>): ColSort<Id, Row> {
  const [orderBy, setOrderBy] = React.useState(defSortCol);
  const [order, setOrder] = React.useState<"asc" | "desc">("asc");

  return React.useMemo(() => {
    function handleRequestSort(property: Id) {
      if (orderBy === property) {
        setOrder(order === "asc" ? "desc" : "asc");
      } else {
        setOrderBy(property);
        setOrder("asc");
      }
    }

    let sortOn: ToComparable<Row> = {
      asc: cols[0].getForSort ?? cols[0].getDisplay,
    };
    for (const col of cols) {
      if (col.id === orderBy && order === "asc") {
        sortOn = { asc: col.getForSort ?? col.getDisplay };
      } else if (col.id === orderBy) {
        sortOn = { desc: col.getForSort ?? col.getDisplay };
      }
    }

    return { orderBy, order, sortOn, handleRequestSort };
  }, [orderBy, setOrderBy, order, setOrder, cols]);
}

export function buildClipboardTableAsString<Id, Row>(
  columns: ReadonlyArray<Column<Id, Row>>,
  rows: Iterable<Row>,
) {
  let table = "<table>\n<thead><tr>";
  for (const col of columns) {
    if (!col.ignoreDuringExport) {
      table += "<th>" + col.label + "</th>";
    }
  }
  table += "</tr></thead>\n<tbody>\n";

  for (const row of rows) {
    table += "<tr>";
    for (const col of columns) {
      if (!col.ignoreDuringExport) {
        const val = col.getForExport ? col.getForExport(row) : col.getDisplay(row);
        table += "<td>" + val + "</td>";
      }
    }
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";

  return table;
}

export function copyTableToClipboard<Id, Row>(
  columns: ReadonlyArray<Column<Id, Row>>,
  rows: Iterable<Row>,
): void {
  copy(buildClipboardTableAsString(columns, rows));
}
