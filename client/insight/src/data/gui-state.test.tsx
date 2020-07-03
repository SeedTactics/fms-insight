/* Copyright (c) 2018, John Lenz

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

import * as gui from "./gui-state";
import { addHours } from "date-fns";

it("creates initial state", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const s = gui.reducer(undefined as any, undefined as any);
  expect(s).toBe(gui.initial);
});

it("selects a pallet for the pallet cycle chart", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetSelectedPalletCycle,
    pallet: "pal555",
  });
  expect(s.pallet_cycle_selected).toEqual("pal555");
});

it("sets the pallet zoom range", () => {
  const now = new Date();
  let s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetPalletCycleDateZoom,
    zoom: { start: addHours(now, -2), end: now },
  });
  expect(s.pallet_cycle_date_zoom).toEqual({ start: addHours(now, -2), end: now });

  s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetPalletCycleDateZoom,
    zoom: undefined,
  });
  expect(s.pallet_cycle_date_zoom).toBeUndefined();
});

it("opens the workorder dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetWorkorderDialogOpen,
    open: true,
  });
  expect(s.workorder_dialog_open).toBe(true);
});

it("closes the workorder dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetWorkorderDialogOpen,
    open: false,
  });
  expect(s.workorder_dialog_open).toBe(false);
});

it("opens the insp type dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetInspTypeDialogOpen,
    open: true,
  });
  expect(s.insptype_dialog_open).toBe(true);
});

it("closes the insp type dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetInspTypeDialogOpen,
    open: false,
  });
  expect(s.insptype_dialog_open).toBe(false);
});

it("opens the serial dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetSerialDialogOpen,
    open: true,
  });
  expect(s.serial_dialog_open).toBe(true);
});

it("closes the serial dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetSerialDialogOpen,
    open: false,
  });
  expect(s.serial_dialog_open).toBe(false);
});

it("opens the qr code scan dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetScanQrCodeDialog,
    open: true,
  });
  expect(s.scan_qr_dialog_open).toBe(true);
});

it("closes the qr code scan dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetScanQrCodeDialog,
    open: false,
  });
  expect(s.scan_qr_dialog_open).toBe(false);
});

it("sets the add mat dialog state", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetAddMatToQueueModeDialogOpen,
    open: true,
  });
  expect(s.queue_dialog_mode_open).toBe(true);
});

it("sets the add mat queue name", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetAddMatToQueueName,
    queue: "aaaa",
  });
  expect(s.add_mat_to_queue).toEqual("aaaa");
});

it("opens the manual serial entry dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetManualSerialEntryDialog,
    open: true,
  });
  expect(s.manual_serial_entry_dialog_open).toBe(true);
});

it("closes the manual serial entry dialog", () => {
  const s = gui.reducer(gui.initial, {
    type: gui.ActionType.SetManualSerialEntryDialog,
    open: false,
  });
  expect(s.manual_serial_entry_dialog_open).toBe(false);
});
