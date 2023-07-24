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

import { ButtonBase, Stack, TextField, Tooltip } from "@mui/material";
import * as React from "react";
import { Clear as ClearIcon } from "@mui/icons-material";
import { addDays, addMinutes } from "date-fns";
import { atom, useAtom, useAtomValue } from "jotai";
import { atomWithStorage } from "jotai/utils";

const shiftStartStorage = atomWithStorage<ReadonlyArray<[number, number]>>("shift-starts", [
  [1, 6 * 60],
  [2, 14 * 60],
  [3, 22 * 60],
]);

const shiftStartAtom = atom(
  (get) => new Map(get(shiftStartStorage)),
  (get, set, update: (shifts: Map<number, number>) => void) => {
    const shifts = new Map(get(shiftStartStorage));
    update(shifts);
    set(shiftStartStorage, Array.from(shifts.entries()));
  },
);

export type ShiftStartAndEnd = {
  readonly start: Date;
  readonly end: Date;
};

export function useShifts(day: Date): ReadonlyArray<ShiftStartAndEnd> {
  const shifts = useAtomValue(shiftStartAtom);
  return React.useMemo(() => {
    const starts: Array<number> = [];
    for (let i = 1; i <= 3; i++) {
      const cur = shifts.get(i);
      if (cur === undefined) continue;
      if (starts.length >= 1 && starts[starts.length - 1] >= cur) continue;
      starts.push(cur);
    }

    if (starts.length === 0) {
      return [
        {
          start: day,
          end: addDays(day, 1),
        },
      ];
    }

    const finalShiftEnd = addMinutes(addDays(day, 1), starts[0]);

    const startAndEnd: Array<ShiftStartAndEnd> = [];
    for (let i = 0; i < starts.length; i++) {
      startAndEnd.push({
        start: addMinutes(day, starts[i]),
        end: i === starts.length - 1 ? finalShiftEnd : addMinutes(day, starts[i + 1]),
      });
    }
    return startAndEnd;
  }, [shifts, day]);
}

function timespanToMinutes(t: string) {
  const [h, m] = t.split(":");
  return parseInt(h, 10) * 60 + parseInt(m, 10);
}

function minutesToTimespan(m: number) {
  const h = Math.floor(m / 60);
  const min = m % 60;
  return `${h.toString().padStart(2, "0")}:${min.toString().padStart(2, "0")}`;
}

function shiftError(shiftNum: number, starts: ReadonlyMap<number, number>): boolean {
  const cur = starts.get(shiftNum);
  if (cur === undefined) {
    return false;
  }

  if (shiftNum === 1) {
    const after = starts.get(2) ?? starts.get(3);
    return after !== undefined && after <= cur;
  } else if (shiftNum === 2) {
    const before = starts.get(1);
    const after = starts.get(3);
    return (before !== undefined && before >= cur) || (after !== undefined && after <= cur);
  } else if (shiftNum === 3) {
    const before = starts.get(2) ?? starts.get(1);
    return before !== undefined && before >= cur;
  } else {
    return false;
  }
}

function ShiftStartInput({ shiftNum }: { shiftNum: number }) {
  const [shifts, setShifts] = useAtom(shiftStartAtom);
  const val = shifts.get(shiftNum);
  return (
    <TextField
      type="time"
      label={`Shift ${shiftNum} Start`}
      error={shiftError(shiftNum, shifts)}
      value={val === undefined ? "" : minutesToTimespan(val)}
      onChange={(e) => {
        const val = e.target.value;
        if (val === "") {
          setShifts((draft) => draft.delete(shiftNum));
        } else {
          setShifts((draft) => draft.set(shiftNum, timespanToMinutes(val)));
        }
      }}
      variant="standard"
      size="small"
      InputLabelProps={{ shrink: true }}
      InputProps={
        shiftNum === 1
          ? undefined
          : {
              endAdornment: (
                <Tooltip title="Clear/Disable Shift">
                  <ButtonBase sx={{ mb: "4px" }} onClick={() => setShifts((draft) => draft.delete(shiftNum))}>
                    <ClearIcon fontSize="small" />
                  </ButtonBase>
                </Tooltip>
              ),
            }
      }
    />
  );
}

export function ShiftStart() {
  return (
    <Stack direction="row" spacing={2}>
      <ShiftStartInput shiftNum={1} />
      <ShiftStartInput shiftNum={2} />
      <ShiftStartInput shiftNum={3} />
    </Stack>
  );
}
