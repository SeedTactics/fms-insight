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
import LastPageIcon from "@material-ui/icons/LastPage";
import Toolbar from "@material-ui/core/Toolbar";
import Typography from "@material-ui/core/Typography";
import Select from "@material-ui/core/Select";
import ZoomOutIcon from "@material-ui/icons/ZoomOut";
import InputBase from "@material-ui/core/InputBase";
import MenuItem from "@material-ui/core/MenuItem";
import Button from "@material-ui/core/Button";
import MoreHoriz from "@material-ui/icons/MoreHoriz";
import moment from "moment";
import "react-dates/lib/css/_datepicker.css";
import "./data-table.css";
import "react-dates/initialize";
import { DateRangePicker } from "react-dates";

import { addDays, addHours } from "date-fns";
import { LazySeq } from "../../data/lazyseq";
import { ToOrderable } from "prelude-ts";

export interface Column<Id, Row> {
  readonly id: Id;
  readonly numeric: boolean;
  readonly label: string;
  readonly getDisplay: (c: Row) => string;
  readonly getForSort?: ToOrderable<Row>;
}

export interface DataTableHeadProps<Id, Row> {
  readonly orderBy: Id;
  readonly order: "asc" | "desc";
  readonly columns: ReadonlyArray<Column<Id, Row>>;
  readonly onRequestSort: (id: Id) => void;
  readonly showDetailsCol: boolean;
}

export function DataTableHead<Id extends string | number, Row>(props: DataTableHeadProps<Id, Row>) {
  return (
    <TableHead>
      <TableRow>
        {props.columns.map(col => (
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
        {props.showDetailsCol ? <TableCell padding="checkbox" /> : undefined}
      </TableRow>
    </TableHead>
  );
}

export interface DataTableActionsProps {
  readonly page: number;
  readonly count: number;
  readonly rowsPerPage: number;
  readonly last30_days: boolean;
  readonly setPage: (page: number) => void;
  readonly setRowsPerPage: (rpp: number) => void;
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: ((p: { zoom?: { start: Date; end: Date } }) => void) | undefined;
}

export function DataTableActions(props: DataTableActionsProps) {
  const setDateZoom = props.set_date_zoom_range;
  function setDateRange(numDaysBack: number) {
    const now = new Date();
    if (setDateZoom) {
      setDateZoom({ zoom: { start: addDays(now, -numDaysBack), end: addHours(now, 1) } });
    }
  }
  const [focusedDateEntry, setFocusedDateEntry] = React.useState<"startDate" | "endDate" | null>(null);
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
        onChange={evt => {
          const rpp = parseInt(evt.target.value, 10);
          props.setRowsPerPage(rpp);
          const maxPage = Math.ceil(props.count / rpp) - 1;
          if (props.page > maxPage) {
            props.setPage(maxPage);
          }
        }}
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
      {setDateZoom ? (
        <>
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
                <Button onClick={() => setDateZoom({ zoom: undefined })} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
                  30d
                </Button>
              </Tooltip>
            </>
          ) : (
            <>
              <DateRangePicker
                startDate={moment(
                  props.current_date_zoom ? props.current_date_zoom.start : props.default_date_range[0]
                )}
                startDateId="station-data-table-start-date"
                endDate={moment(
                  addDays(props.current_date_zoom ? props.current_date_zoom.end : props.default_date_range[1], -1)
                )}
                navPrev={<span />}
                navNext={<span />}
                endDateId="station-data-table-start-date"
                noBorder={true}
                numberOfMonths={1}
                withPortal
                openDirection="up"
                hideKeyboardShortcutsPanel
                minimumNights={0}
                onDatesChange={d =>
                  setDateZoom({
                    zoom: {
                      start: d.startDate ? d.startDate.toDate() : props.default_date_range[0],
                      end: d.endDate ? addDays(d.endDate.toDate(), 1) : props.default_date_range[1]
                    }
                  })
                }
                keepOpenOnDateSelect
                isOutsideRange={day =>
                  day.toDate() < props.default_date_range[0] || day.toDate >= props.default_date_range[1]
                }
                focusedInput={focusedDateEntry}
                onFocusChange={setFocusedDateEntry}
              />
              <Tooltip title="Reset Date Range">
                <IconButton onClick={() => setDateZoom({ zoom: undefined })}>
                  <ZoomOutIcon />
                </IconButton>
              </Tooltip>
            </>
          )}
        </>
      ) : (
        undefined
      )}
    </Toolbar>
  );
}

export interface DataTableBodyProps<Id, Row> {
  readonly pageData: Iterable<Row>;
  readonly columns: ReadonlyArray<Column<Id, Row>>;
  readonly onClickDetails?: (r: Row) => void;
}

export class DataTableBody<Id extends string | number, Row> extends React.PureComponent<DataTableBodyProps<Id, Row>> {
  render() {
    const onClickDetails = this.props.onClickDetails;
    return (
      <TableBody>
        {LazySeq.ofIterable(this.props.pageData).map((row, idx) => (
          <TableRow key={idx}>
            {this.props.columns.map(col => (
              <TableCell key={col.id} align={col.numeric ? "right" : "left"}>
                {col.getDisplay(row)}
              </TableCell>
            ))}
            {onClickDetails ? (
              <TableCell padding="checkbox">
                <IconButton onClick={() => onClickDetails(row)}>
                  <MoreHoriz />
                </IconButton>
              </TableCell>
            ) : (
              undefined
            )}
          </TableRow>
        ))}
      </TableBody>
    );
  }
}
