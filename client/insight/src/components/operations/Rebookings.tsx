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
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Fab,
  Stack,
  Table,
  Typography,
} from "@mui/material";
import { useAtomValue } from "jotai";
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
  useCancelRebooking,
} from "../../cell-status/rebookings.js";
import { OrderedMap } from "@seedtactics/immutable-collections";
import { Add } from "@mui/icons-material";

enum ColumnId {
  BookingId,
  Part,
  Quantity,
  RequestTime,
  Priority,
  Workorder,
  Serial,
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

const columns: ReadonlyArray<Col> = [
  {
    id: ColumnId.BookingId,
    numeric: false,
    label: "ID",
    getDisplay: (r) => r.bookingId,
    Cell: ({ row }: { row: Row }) =>
      row.canceled ? (
        <Typography sx={{ textDecoration: "line-through" }}>{row.bookingId}</Typography>
      ) : (
        row.bookingId
      ),
  },
  {
    id: ColumnId.Part,
    numeric: false,
    label: "Part",
    getDisplay: (r) => r.partName,
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
    id: ColumnId.Serial,
    numeric: false,
    label: "Serial",
    getDisplay: (r) => r.material?.serial ?? "",
  },
  {
    id: ColumnId.Canceled,
    numeric: false,
    label: "Canceled",
    getDisplay: (r) => r.canceled?.toLocaleString() ?? "",
    getForSort: (r) => r.canceled?.getTime() ?? 0,
  },
  {
    id: ColumnId.Job,
    numeric: false,
    label: "Scheduled Job",
    getDisplay: (r) => r.job ?? "",
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
        .toSortedArray(sort.sortOn),
    [sort.sortOn, rebookings, canceled, scheduled],
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
        <Stack direction="row" spacing={1}>
          <Typography>Part: {rebooking?.partName}</Typography>
          <Typography>Quantity: {rebooking?.quantity}</Typography>
          <Typography>Request Time: {rebooking?.timeUTC.toLocaleString()}</Typography>
          <Typography>Priority: {rebooking?.priority}</Typography>
          <Typography>Workorder: {rebooking?.workorder}</Typography>
          <Typography>Serial: {rebooking?.material?.serial}</Typography>
          <Typography>Note: {rebooking?.notes}</Typography>
        </Stack>
        {rebooking?.job && (
          <>
            <Divider />
            <Stack direction="row" spacing={1}>
              <Typography>Scheduled Job: {rebooking?.job}</Typography>
              <Typography>Scheduled Time: {rebooking?.schTime?.toLocaleString()}</Typography>
            </Stack>
          </>
        )}
        {rebooking?.canceled && <Typography>Canceled: {rebooking.canceled.toLocaleString()}</Typography>}
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

const NewRebookingDialog = memo(function NewRebookingDialog() {
  const [open, setOpen] = useState(false);

  function create() {
    // TODO
  }

  return (
    <>
      <Dialog open={open} onClose={() => setOpen(false)}>
        <DialogTitle>Create New</DialogTitle>
        <DialogContent></DialogContent>
        <DialogActions>
          <Button color="secondary" onClick={create}>
            Create
          </Button>
          <Button onClick={() => setOpen(false)}>Cancel</Button>
        </DialogActions>
      </Dialog>
      <Fab onClick={() => setOpen(true)} sx={{ position: "fixed", bottom: "24px", right: "24px" }}>
        <Add />
      </Fab>
    </>
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
    </Box>
  );
}
