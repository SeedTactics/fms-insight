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

import {
  Autocomplete,
  Box,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Fab,
  FormControlLabel,
  InputAdornment,
  Stack,
  Table,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import { atom, useAtom, useAtomValue } from "jotai";
import { fmsInformation } from "../../network/server-settings.js";
import {
  Column,
  DataTableActions,
  DataTableBody,
  DataTableHead,
  useColSort,
  useTablePage,
} from "../analysis/DataTable.js";
import { useSetTitle } from "../routes.js";
import { IRebooking } from "../../network/api.js";
import { memo, useMemo, useState } from "react";
import {
  canceledRebookings,
  last30Rebookings,
  last30ScheduledBookings,
  NewRebooking,
  useCancelRebooking,
  useNewRebooking,
} from "../../cell-status/rebookings.js";
import { LazySeq, OrderedMap } from "@seedtactics/immutable-collections";
import { Add } from "@mui/icons-material";
import { currentStatus } from "../../cell-status/current-status.js";
import { last30Jobs } from "../../cell-status/scheduled-jobs.js";
import { PartIdenticon } from "../station-monitor/Material.js";

enum ColumnId {
  BookingId,
  Part,
  Quantity,
  RequestTime,
  Priority,
  Workorder,
  Canceled,
  Job,
  SchTime,
  // Procs
  // Notes
}

type Row = Readonly<IRebooking> & {
  readonly job?: string;
  readonly schTime?: Date;
  readonly canceled?: Date;
};

type Col = Column<ColumnId, Row>;

const filterDialogOpen = atom(false);
const hideScheduledAtom = atom(false);
const hideCanceledAtom = atom(false);

const columns: ReadonlyArray<Col> = [
  {
    id: ColumnId.BookingId,
    numeric: false,
    label: "ID",
    getDisplay: (r) => r.bookingId,
    Cell: ({ row }: { row: Row }) =>
      row.canceled ? (
        <Typography sx={{ textDecoration: "line-through", fontSize: "inherit" }}>{row.bookingId}</Typography>
      ) : (
        row.bookingId
      ),
  },
  {
    id: ColumnId.Part,
    numeric: false,
    label: "Part",
    getDisplay: (r) => r.partName,
    Cell: ({ row }: { row: Row }) => (
      <Stack direction="row" spacing={1} alignItems="center">
        <PartIdenticon part={row.partName} size={25} />
        {row.partName}
      </Stack>
    ),
  },
  {
    id: ColumnId.Quantity,
    numeric: true,
    label: "Quantity",
    getDisplay: (r) => r.quantity.toString(),
    getForSort: (r) => r.quantity,
  },
  {
    id: ColumnId.RequestTime,
    numeric: false,
    label: "Request Time",
    getDisplay: (r) => r.timeUTC.toLocaleString(),
    getForSort: (r) => r.timeUTC.getTime(),
  },
  {
    id: ColumnId.Priority,
    numeric: false,
    label: "Priority",
    getDisplay: (r) => (r.priority ? r.priority.toString() : ""),
    getForSort: (r) => r.priority ?? 0,
  },
  {
    id: ColumnId.Workorder,
    numeric: false,
    label: "Workorder",
    getDisplay: (r) => r.workorder ?? "",
  },
  {
    id: ColumnId.Canceled,
    numeric: false,
    label: "Canceled",
    getDisplay: (r) => r.canceled?.toLocaleString() ?? "",
    getForSort: (r) => r.canceled?.getTime() ?? 0,
    openFilterDialog: filterDialogOpen,
  },
  {
    id: ColumnId.Job,
    numeric: false,
    label: "Scheduled Job",
    getDisplay: (r) => r.job ?? "",
    openFilterDialog: filterDialogOpen,
  },
  {
    id: ColumnId.SchTime,
    numeric: false,
    label: "Scheduled Time",
    getDisplay: (r) => (r.schTime ? r.schTime.toLocaleString() : ""),
    getForSort: (r) => (r.schTime ? r.schTime.getTime() : 0),
  },
];

const BookingTable = memo(function BookingTable({
  setRebookingToShow,
}: {
  setRebookingToShow: (r: Row) => void;
}) {
  const sort = useColSort(ColumnId.RequestTime, columns, "desc");
  const tpage = useTablePage();
  const rebookings: OrderedMap<string, Row> = useAtomValue(last30Rebookings);
  const canceled = useAtomValue(canceledRebookings);
  const scheduled = useAtomValue(last30ScheduledBookings);
  const unschOnly = useAtomValue(hideScheduledAtom);
  const hideCanceled = useAtomValue(hideCanceledAtom);

  const rows = useMemo(
    () =>
      rebookings
        .adjust(scheduled, (r, sch) =>
          r
            ? {
                ...r,
                job: sch?.jobUnique,
                schTime: sch?.scheduledTime,
              }
            : undefined,
        )
        .adjust(canceled, (r, t) => (r ? { ...r, canceled: t } : undefined))
        .valuesToAscLazySeq()
        .transform((s) => (unschOnly ? s.filter((r) => !r.job) : s))
        .transform((s) => (hideCanceled ? s.filter((r) => !r.canceled) : s))
        .toSortedArray(sort.sortOn),
    [sort.sortOn, rebookings, canceled, scheduled, unschOnly, hideCanceled],
  );

  return (
    <div>
      <Table>
        <DataTableHead columns={columns} sort={sort} showDetailsCol copyToClipboardRows={rows} />
        <DataTableBody
          columns={columns}
          pageData={rows}
          rowsPerPage={tpage.rowsPerPage}
          onClickDetails={(_, row) => setRebookingToShow(row)}
        />
      </Table>
      <DataTableActions tpage={tpage} count={rows.length} />
    </div>
  );
});

const longDateFormat = new Intl.DateTimeFormat(undefined, {
  year: "numeric",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "numeric",
});

const RebookingDialog = memo(function RebookingDialog({
  rebooking,
  close,
}: {
  rebooking: Row | undefined;
  close: (r: undefined) => void;
}) {
  const [sendCancel, canceling] = useCancelRebooking();

  function cancel() {
    if (rebooking) {
      sendCancel(rebooking.bookingId)
        .then(() => close(undefined))
        .catch(console.log);
    }
  }

  return (
    <Dialog open={rebooking !== undefined} onClose={() => close(undefined)}>
      <DialogTitle>
        {rebooking?.canceled ? (
          <Typography sx={{ textDecoration: "line-through" }}>{rebooking?.bookingId}</Typography>
        ) : (
          rebooking?.bookingId
        )}
      </DialogTitle>
      <DialogContent>
        <Stack direction="column" spacing={1}>
          {rebooking?.canceled && (
            <Typography variant="h5">Canceled at {longDateFormat.format(rebooking.canceled)}</Typography>
          )}
          <Stack direction="row" spacing={1}>
            <span>Part:</span>
            {rebooking?.partName && <PartIdenticon part={rebooking?.partName} size={25} />}
            <span>{rebooking?.partName}</span>
          </Stack>
          <Typography>Quantity: {rebooking?.quantity}</Typography>
          <Typography>Request Time: {longDateFormat.format(rebooking?.timeUTC)}</Typography>
          <Typography>Priority: {rebooking?.priority}</Typography>
          {rebooking?.workorder && <Typography>Workorder: {rebooking?.workorder}</Typography>}
          <Typography>Note: {rebooking?.notes}</Typography>
          {rebooking?.job && (
            <>
              <Divider />
              <Typography>Scheduled Job: {rebooking?.job}</Typography>
              <Typography>Scheduled Time: {rebooking?.schTime?.toLocaleString()}</Typography>
            </>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {!rebooking?.canceled && !rebooking?.job && (
          <Button onClick={cancel} color="secondary" disabled={canceling}>
            {canceling ? <CircularProgress size={24} /> : undefined}
            Cancel Rebooking Request
          </Button>
        )}
        <Button onClick={() => close(undefined)}>Close</Button>
      </DialogActions>
    </Dialog>
  );
});

const partNamesAtom = atom<ReadonlyArray<string>>((get) =>
  LazySeq.ofObject(get(currentStatus).jobs)
    .map(([_, j]) => j.partName)
    .concat(
      get(last30Jobs)
        .valuesToLazySeq()
        .map((j) => j.partName),
    )
    .distinctAndSortBy((p) => p)
    .toRArray(),
);

const NewRebookingDialog = memo(function NewRebookingDialog() {
  const [open, setOpen] = useState(false);
  const [rebooking, setRebooking] = useState<NewRebooking>({ part: "" });
  const partNames = useAtomValue(partNamesAtom);
  const [createNew, creating] = useNewRebooking();

  const allowCreate = rebooking.part !== "" && !!rebooking.qty && !isNaN(rebooking.qty);

  function close() {
    setOpen(false);
    setRebooking({ part: "" });
  }

  function create() {
    if (!allowCreate) {
      return;
    }
    createNew(rebooking).then(close).catch(console.log);
  }

  return (
    <>
      <Dialog open={open} onClose={close}>
        <DialogTitle>Create New</DialogTitle>
        <DialogContent>
          <Stack direction="column" spacing={2} sx={{ mt: "0.5em", minWidth: "25em" }}>
            <Autocomplete
              freeSolo
              selectOnFocus
              clearOnBlur
              handleHomeEndKeys
              disableClearable
              options={partNames}
              value={rebooking.part}
              onChange={(_, v) => setRebooking((r) => ({ ...r, part: v }))}
              renderInput={(params) => (
                <TextField
                  {...params}
                  label="Part Name"
                  slotProps={{
                    input: {
                      ...params.InputProps,
                      startAdornment: (
                        <>
                          {rebooking.part !== "" ? (
                            <InputAdornment position="start">
                              <PartIdenticon part={rebooking.part} size={25} />
                            </InputAdornment>
                          ) : undefined}
                          {params.InputProps.startAdornment}
                        </>
                      ),
                    },
                  }}
                />
              )}
              renderOption={(props, option) => (
                <li {...props}>
                  <Stack direction="row" spacing="1" alignItems="center">
                    <PartIdenticon part={option} size={25} />
                    {option}
                  </Stack>
                </li>
              )}
              filterOptions={(options, params) => {
                const filtered = options.filter((o) =>
                  o.toLowerCase().includes(params.inputValue.toLowerCase()),
                );
                return filtered.length === 0 && params.inputValue !== "" ? [params.inputValue] : filtered;
              }}
            />
            <TextField
              label="Quantity"
              type="number"
              value={rebooking.qty && !isNaN(rebooking.qty) ? rebooking.qty : ""}
              onChange={(e) => setRebooking((r) => ({ ...r, qty: parseInt(e.target.value) }))}
            />
            <TextField
              label="Priority (optional)"
              type="number"
              value={rebooking.priority && !isNaN(rebooking.priority) ? rebooking.priority : ""}
              onChange={(e) => setRebooking((r) => ({ ...r, priority: parseInt(e.target.value) }))}
            />
            <TextField
              label="Workorder (optional)"
              value={rebooking.workorder ?? ""}
              onChange={(e) => setRebooking((r) => ({ ...r, workorder: e.target.value }))}
            />
            <TextField
              label="Notes (optional)"
              multiline
              minRows={2}
              value={rebooking.notes ?? ""}
              onChange={(e) => setRebooking((r) => ({ ...r, notes: e.target.value }))}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button color="secondary" onClick={create} disabled={!allowCreate || creating}>
            {creating ? <CircularProgress size={24} /> : undefined}
            Create
          </Button>
          <Button onClick={close}>Cancel</Button>
        </DialogActions>
      </Dialog>
      <Tooltip title="Add Rebooking">
        <Fab
          onClick={() => setOpen(true)}
          sx={{ position: "fixed", bottom: "24px", right: "24px" }}
          color="primary"
        >
          <Add />
        </Fab>
      </Tooltip>
    </>
  );
});

const FilterDialog = memo(function UnschFilterDialog() {
  const [open, setOpen] = useAtom(filterDialogOpen);
  const [showOnlyUnscheduled, setShowOnlyUnscheduled] = useAtom(hideScheduledAtom);
  const [hideCanceled, setHideCanceled] = useAtom(hideCanceledAtom);

  return (
    <Dialog open={open} onClose={() => setOpen(false)}>
      <DialogTitle>Filter</DialogTitle>
      <DialogContent>
        <Stack direction="column" spacing={2}>
          <FormControlLabel
            control={
              <Checkbox
                checked={showOnlyUnscheduled}
                onChange={(e) => setShowOnlyUnscheduled(e.target.checked)}
              />
            }
            label="Hide scheduled"
          />
          <FormControlLabel
            control={<Checkbox checked={hideCanceled} onChange={(e) => setHideCanceled(e.target.checked)} />}
            label="Hide canceled"
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setOpen(false)}>Close</Button>
      </DialogActions>
    </Dialog>
  );
});

export function RebookingsPage() {
  const fmsInfo = useAtomValue(fmsInformation);
  useSetTitle(fmsInfo.supportsRebookings ?? "Rebookings");
  const [rebookingToShow, setRebookingToShow] = useState<Row | undefined>(undefined);

  return (
    <Box component="main" sx={{ padding: "24px" }}>
      <BookingTable setRebookingToShow={setRebookingToShow} />
      <RebookingDialog rebooking={rebookingToShow} close={setRebookingToShow} />
      <NewRebookingDialog />
      <FilterDialog />
    </Box>
  );
}
