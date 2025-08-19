/* Copyright (c) 2025, John Lenz

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

import { memo, useState } from "react";
import {
  Last30ChartRangeAtom,
  last30ChartStartTimes,
  last30WeekdayStartIdx,
  last30WeekdayStartMinuteOffset,
} from "../../data/chart-times";
import { useAtom, useAtomValue } from "jotai";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  FormControl,
  FormControlLabel,
  MenuItem,
  Radio,
  RadioGroup,
  Select,
  Stack,
  TextField,
} from "@mui/material";

function RadioLabel({ label, date }: { label: string; date: Date | null }) {
  return (
    <span>
      {label} {date ? `(${date.toLocaleString()})` : ""}
    </span>
  );
}

function CustomDateLabel() {
  return <span>Custom Date</span>;
}

function minutesToTimespan(m: number) {
  const h = Math.floor(m / 60);
  const min = m % 60;
  return `${h.toString().padStart(2, "0")}:${min.toString().padStart(2, "0")}`;
}

function RangeDialog({ chartAtom }: { chartAtom: Last30ChartRangeAtom }) {
  const [chartRange, setChartRange] = useAtom(chartAtom);
  const [open, setOpen] = useState(false);

  const [weekdayStart, setWeekdayStart] = useAtom(last30WeekdayStartIdx);
  const [weekdayStartMinuteOffset, setWeekdayStartMinuteOffset] = useAtom(last30WeekdayStartMinuteOffset);

  const startOfToday = useAtomValue(last30ChartStartTimes("StartOfToday"));
  const startOfYesterday = useAtomValue(last30ChartStartTimes("StartOfYesterday"));
  const startOfWeek = useAtomValue(last30ChartStartTimes("StartOfWeek"));
  const startOfLastWeek = useAtomValue(last30ChartStartTimes("StartOfLastWeek"));
  const last30 = useAtomValue(last30ChartStartTimes("Last30"));

  return (
    <Dialog open={open} onClose={() => setOpen(false)}>
      <DialogContent>
        <Stack direction="column" spacing={2}>
          <FormControl>
            <RadioGroup
              value={chartRange.startType instanceof Date ? "CustomDate" : chartRange.startType}
              onChange={(event) => setChartRange(event.target.value)}
            >
              <FormControlLabel
                value="StartOfToday"
                control={<Radio />}
                label={<RadioLabel label="Start of Today" date={startOfToday} />}
              />
              <FormControlLabel
                value="StartOfYesterday"
                control={<Radio />}
                label={<RadioLabel label="Start of Yesterday" date={startOfYesterday} />}
              />
              <FormControlLabel
                value="StartOfWeek"
                control={<Radio />}
                label={<RadioLabel label="Start of Week" date={startOfWeek} />}
              />
              <FormControlLabel
                value="StartOfLastWeek"
                control={<Radio />}
                label={<RadioLabel label="Start of Last Week" date={startOfLastWeek} />}
              />
              <FormControlLabel
                value="Last30"
                control={<Radio />}
                label={<RadioLabel label="Last 30 Days" date={last30} />}
              />
              <FormControlLabel value="CustomDate" control={<Radio />} label={<CustomDateLabel />} />
            </RadioGroup>
          </FormControl>
          <Stack direction="row" justifyContent="space-around">
            <Select
              label="First Day Of Week"
              value={weekdayStart}
              onChange={(e) => setWeekdayStart(e.target.value)}
            >
              <MenuItem value={0}>Sunday</MenuItem>
              <MenuItem value={1}>Monday</MenuItem>
              <MenuItem value={2}>Tuesday</MenuItem>
              <MenuItem value={3}>Wednesday</MenuItem>
              <MenuItem value={4}>Thursday</MenuItem>
              <MenuItem value={5}>Friday</MenuItem>
              <MenuItem value={6}>Saturday</MenuItem>
            </Select>
          </Stack>
          <TextField
            type="time"
            label="Day Start Time"
            value={minutesToTimespan(weekdayStartMinuteOffset)}
            onChange={(e) => {
              const val = e.target.value;
              if (val === "") {
                setWeekdayStartMinuteOffset(0);
              } else {
                const [h, m] = val.split(":").map(Number);
                setWeekdayStartMinuteOffset(h * 60 + m);
              }
            }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setOpen(false)}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}

export const Last30ChartRangeToolbar = memo(function Last30ChartRangeToolbar({
  chartAtom,
}: {
  chartAtom: Last30ChartRangeAtom;
}) {
  const [chartRange, setChartRange] = useAtom(chartAtom);
  return null;
});
