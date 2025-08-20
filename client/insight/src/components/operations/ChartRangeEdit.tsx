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
  Last30ChartEnd,
  last30ChartEndTimes,
  Last30ChartRangeAtom,
  Last30ChartStart,
  last30ChartStartTimes,
  last30WeekdayStartIdx,
  last30WeekdayStartMinuteOffset,
} from "../../data/chart-times";
import { useAtom, useAtomValue } from "jotai";
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  Divider,
  IconButton,
  MenuItem,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import { Edit } from "@mui/icons-material";
import { addMinutes, startOfToday } from "date-fns";

function RadioLabel({ label, date }: { label: string; date: Date | null }) {
  return (
    <Box>
      <Typography>{label}</Typography>
      {date ? <Typography variant="subtitle2">({date.toLocaleString()})</Typography> : undefined}
    </Box>
  );
}

function CustomDateLabel({
  date,
  setDate,
  enabled,
}: {
  enabled: boolean;
  date: Date | null;
  setDate: (date: Date) => void;
}) {
  const last30 = useAtomValue(last30ChartStartTimes("Last30"));
  return (
    <TextField
      type="datetime-local"
      value={
        date ? new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16) : ""
      }
      onChange={(event) => setDate(new Date(event.target.value))}
      disabled={!enabled}
      label="Custom Date"
      slotProps={{
        inputLabel: { shrink: true },
        htmlInput: {
          min: new Date(last30.getTime() - last30.getTimezoneOffset() * 60000).toISOString().slice(0, 16),
        },
      }}
    />
  );
}

function RangeStart({ chartAtom }: { chartAtom: Last30ChartRangeAtom }) {
  const [chartRange, setChartRange] = useAtom(chartAtom);
  const dayStart = useAtomValue(last30WeekdayStartMinuteOffset);
  const [customDate, setCustomDate] = useState<Date | null>(
    chartRange.startType instanceof Date ? chartRange.startType : addMinutes(startOfToday(), dayStart),
  );

  function setTy(type: Last30ChartStart | "CustomDate") {
    if (type === "CustomDate") {
      setChartRange({ start: customDate ?? new Date() });
    } else {
      setChartRange({ start: type });
    }
  }

  function setDate(date: Date) {
    setCustomDate(date);
    setChartRange({ start: date });
  }

  const stOfToday = useAtomValue(last30ChartStartTimes("StartOfToday"));
  const startOfYesterday = useAtomValue(last30ChartStartTimes("StartOfYesterday"));
  const startOfWeek = useAtomValue(last30ChartStartTimes("StartOfWeek"));
  const startOfLastWeek = useAtomValue(last30ChartStartTimes("StartOfLastWeek"));
  const last30 = useAtomValue(last30ChartStartTimes("Last30"));

  return (
    <Box>
      <Typography variant="h6">Range Start</Typography>
      <RadioGroup
        value={chartRange.startType instanceof Date ? "CustomDate" : chartRange.startType}
        onChange={(event) => setTy(event.target.value as Last30ChartStart | "CustomDate")}
      >
        <Stack direction="column" spacing={2}>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="StartOfToday" />
            <RadioLabel label="Start of Today" date={stOfToday} />
          </Stack>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="StartOfYesterday" />
            <RadioLabel label="Start of Yesterday" date={startOfYesterday} />
          </Stack>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="StartOfWeek" />
            <RadioLabel label="Start of This Week" date={startOfWeek} />
          </Stack>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="StartOfLastWeek" />
            <RadioLabel label="Start of Last Week" date={startOfLastWeek} />
          </Stack>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="Last30" />
            <RadioLabel label="Last 30 Days" date={last30} />
          </Stack>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="CustomDate" />
            <CustomDateLabel
              enabled={chartRange.startType instanceof Date}
              date={customDate}
              setDate={setDate}
            />
          </Stack>
        </Stack>
      </RadioGroup>
    </Box>
  );
}

function RangeEnd({ chartAtom }: { chartAtom: Last30ChartRangeAtom }) {
  const [chartRange, setChartRange] = useAtom(chartAtom);
  const dayStart = useAtomValue(last30WeekdayStartMinuteOffset);
  const [customDate, setCustomDate] = useState<Date>(
    chartRange.endType instanceof Date ? chartRange.endType : addMinutes(startOfToday(), dayStart),
  );

  function setTy(type: Last30ChartEnd | "CustomDate") {
    if (type === "CustomDate") {
      setChartRange({ end: customDate ?? new Date() });
    } else {
      setChartRange({ end: type });
    }
  }

  function setDate(date: Date) {
    setCustomDate(date);
    setChartRange({ end: date });
  }

  const endOfYesterday = useAtomValue(last30ChartEndTimes("EndOfYesterday"));
  const endOfLastWeek = useAtomValue(last30ChartEndTimes("EndOfLastWeek"));

  return (
    <Box>
      <Typography variant="h6">Range End</Typography>
      <RadioGroup
        value={chartRange.endType instanceof Date ? "CustomDate" : chartRange.endType}
        onChange={(event) => setTy(event.target.value as Last30ChartEnd | "CustomDate")}
      >
        <Stack direction="column" spacing={2}>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="Now" />
            <Typography>Now</Typography>
          </Stack>
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="EndOfYesterday" />
            <RadioLabel label="End of Yesterday" date={endOfYesterday} />
          </Stack>
          <Box sx={{ height: "3em" }} />
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="EndOfLastWeek" />
            <RadioLabel label="End of Last Week" date={endOfLastWeek} />
          </Stack>
          <Box sx={{ height: "3em" }} />
          <Stack direction="row" alignItems="center" sx={{ minHeight: "3em" }}>
            <Radio value="CustomDate" />
            <CustomDateLabel
              enabled={chartRange.endType instanceof Date}
              date={customDate}
              setDate={setDate}
            />
          </Stack>
        </Stack>
      </RadioGroup>
    </Box>
  );
}

function minutesToTimespan(m: number) {
  const h = Math.floor(m / 60);
  const min = m % 60;
  return `${h.toString().padStart(2, "0")}:${min.toString().padStart(2, "0")}`;
}

function RangeDialog({
  chartAtom,
  open,
  setOpen,
}: {
  chartAtom: Last30ChartRangeAtom;
  open: boolean;
  setOpen: (open: boolean) => void;
}) {
  const [weekdayStart, setWeekdayStart] = useAtom(last30WeekdayStartIdx);
  const [weekdayStartMinuteOffset, setWeekdayStartMinuteOffset] = useAtom(last30WeekdayStartMinuteOffset);

  return (
    <Dialog open={open} onClose={() => setOpen(false)} maxWidth="md">
      <DialogContent>
        <Stack direction="column" spacing={2} divider={<Divider orientation="horizontal" flexItem />}>
          <Stack direction="row" spacing={2} divider={<Divider orientation="vertical" flexItem />}>
            <RangeStart chartAtom={chartAtom} />
            <RangeEnd chartAtom={chartAtom} />
          </Stack>
          <Stack direction="row" justifyContent="space-around">
            <TextField
              select
              label="First Day Of Week"
              sx={{ minWidth: "10em" }}
              value={weekdayStart}
              onChange={(e) => setWeekdayStart(parseInt(e.target.value) as 0 | 1 | 2 | 3 | 4 | 5 | 6)}
            >
              <MenuItem value={0}>Sunday</MenuItem>
              <MenuItem value={1}>Monday</MenuItem>
              <MenuItem value={2}>Tuesday</MenuItem>
              <MenuItem value={3}>Wednesday</MenuItem>
              <MenuItem value={4}>Thursday</MenuItem>
              <MenuItem value={5}>Friday</MenuItem>
              <MenuItem value={6}>Saturday</MenuItem>
            </TextField>
            <TextField
              type="time"
              label="Day Start Time"
              sx={{ minWidth: "10em" }}
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
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setOpen(false)}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}

function formatStart(ty: Last30ChartStart | Date, d: Date | null) {
  if (ty instanceof Date) {
    return ty.toLocaleString();
  }
  switch (ty) {
    case "StartOfToday":
      return `Start of Today${d ? ` (${d.toLocaleString()})` : ""}`;
    case "StartOfYesterday":
      return `Start of Yesterday${d ? ` (${d.toLocaleString()})` : ""}`;
    case "StartOfWeek":
      return `Start of This Week${d ? ` (${d.toLocaleString()})` : ""}`;
    case "StartOfLastWeek":
      return `Start of Last Week${d ? ` (${d.toLocaleString()})` : ""}`;
    case "Last30":
      return `Last 30 Days${d ? ` (${d.toLocaleString()})` : ""}`;
  }
}

function formatEnd(ty: Last30ChartEnd | Date, d: Date | null) {
  if (ty instanceof Date) {
    return ty.toLocaleString();
  }
  switch (ty) {
    case "Now":
      return "Now";
    case "EndOfYesterday":
      return `End of Yesterday${d ? ` (${d.toLocaleString()})` : ""}`;
    case "EndOfLastWeek":
      return `End of Last Week${d ? ` (${d.toLocaleString()})` : ""}`;
  }
}

export const Last30ChartRangeToolbar = memo(function Last30ChartRangeToolbar({
  chartAtom,
}: {
  chartAtom: Last30ChartRangeAtom;
}) {
  const chartRange = useAtomValue(chartAtom);
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <>
      <Stack direction="row" spacing={2} alignItems="center">
        <Typography variant="body2" color="textSecondary">
          Range: {formatStart(chartRange.startType, chartRange.startDate)} -{" "}
          {formatEnd(chartRange.endType, chartRange.endDate)}
        </Typography>
        <Tooltip title="Edit Range">
          <IconButton onClick={() => setDialogOpen(true)}>
            <Edit />
          </IconButton>
        </Tooltip>
      </Stack>
      <RangeDialog open={dialogOpen} setOpen={setDialogOpen} chartAtom={chartAtom} />
    </>
  );
});
