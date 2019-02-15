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

export enum PlannedOrActual {
  Planned = "Planned",
  Actual = "Actual",
  PlannedMinusActual = "PlannedOrActual"
}

export enum ActionType {
  SetSelectedStationCycle = "Gui_SetSelectedStationCycle",
  SetSelectedPalletCycle = "Gui_SetSelectedPalletCycle",
  SetStationOeeHeatmapType = "Gui_SetStationOeeHeatmapType",
  SetCompletedCountHeatmapType = "Gui_SetCompletedCountHeatmapType",
  SetWorkorderDialogOpen = "Gui_SetWorkorderDialog",
  SetInspTypeDialogOpen = "Gui_SetInspTypeDialog",
  SetSerialDialogOpen = "Gui_SetSerialDialogOpen",
  SetAddMatToQueueModeDialogOpen = "Gui_SetAddMatToQueueModeDialogOpen",
  SetAddMatToQueueName = "Gui_SetAddMatToQueueName",
  SetScanQrCodeDialog = "Gui_ScanQrCodeDialog",
  SetManualSerialEntryDialog = "Gui_ManualSerialEntryDialog",
  SetBackupFileOpenened = "Gui_SetBackupFileOpened"
}

export type Action =
  | { type: ActionType.SetSelectedStationCycle; part?: string; station?: string; pallet?: string }
  | { type: ActionType.SetSelectedPalletCycle; pallet: string }
  | { type: ActionType.SetStationOeeHeatmapType; ty: PlannedOrActual }
  | { type: ActionType.SetCompletedCountHeatmapType; ty: PlannedOrActual }
  | { type: ActionType.SetWorkorderDialogOpen; open: boolean }
  | { type: ActionType.SetInspTypeDialogOpen; open: boolean }
  | { type: ActionType.SetSerialDialogOpen; open: boolean }
  | { type: ActionType.SetAddMatToQueueModeDialogOpen; open: boolean }
  | { type: ActionType.SetAddMatToQueueName; queue: string | undefined }
  | { type: ActionType.SetScanQrCodeDialog; open: boolean }
  | { type: ActionType.SetManualSerialEntryDialog; open: boolean }
  | { type: ActionType.SetBackupFileOpenened; open: boolean };

export interface State {
  readonly station_cycle_selected_part?: string;
  readonly station_cycle_selected_station?: string;
  readonly station_cycle_selected_pallet?: string;
  readonly pallet_cycle_selected?: string;
  readonly station_oee_heatmap_type: PlannedOrActual;
  readonly completed_count_heatmap_type: PlannedOrActual;
  readonly workorder_dialog_open: boolean;
  readonly insptype_dialog_open: boolean;
  readonly serial_dialog_open: boolean;
  readonly scan_qr_dialog_open: boolean;
  readonly queue_dialog_mode_open: boolean;
  readonly manual_serial_entry_dialog_open: boolean;
  readonly add_mat_to_queue?: string;
  readonly backup_file_opened: boolean;
}

export const initial: State = {
  station_oee_heatmap_type: PlannedOrActual.Actual,
  completed_count_heatmap_type: PlannedOrActual.Actual,
  workorder_dialog_open: false,
  insptype_dialog_open: false,
  serial_dialog_open: false,
  scan_qr_dialog_open: false,
  manual_serial_entry_dialog_open: false,
  queue_dialog_mode_open: false,
  backup_file_opened: false
};

export function reducer(s: State, a: Action): State {
  if (s === undefined) {
    return initial;
  }
  switch (a.type) {
    case ActionType.SetSelectedStationCycle:
      return {
        ...s,
        station_cycle_selected_part: a.part,
        station_cycle_selected_station: a.station,
        station_cycle_selected_pallet: a.pallet
      };
    case ActionType.SetSelectedPalletCycle:
      return { ...s, pallet_cycle_selected: a.pallet };
    case ActionType.SetStationOeeHeatmapType:
      return { ...s, station_oee_heatmap_type: a.ty };
    case ActionType.SetCompletedCountHeatmapType:
      return { ...s, completed_count_heatmap_type: a.ty };
    case ActionType.SetWorkorderDialogOpen:
      return { ...s, workorder_dialog_open: a.open };
    case ActionType.SetInspTypeDialogOpen:
      return { ...s, insptype_dialog_open: a.open };
    case ActionType.SetSerialDialogOpen:
      return { ...s, serial_dialog_open: a.open };
    case ActionType.SetScanQrCodeDialog:
      return { ...s, scan_qr_dialog_open: a.open };
    case ActionType.SetAddMatToQueueModeDialogOpen:
      return { ...s, queue_dialog_mode_open: a.open };
    case ActionType.SetAddMatToQueueName:
      return { ...s, add_mat_to_queue: a.queue };
    case ActionType.SetManualSerialEntryDialog:
      return { ...s, manual_serial_entry_dialog_open: a.open };
    case ActionType.SetBackupFileOpenened:
      return { ...s, backup_file_opened: a.open };
    default:
      return s;
  }
}
