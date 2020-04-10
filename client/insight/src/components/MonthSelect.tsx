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
import Input from "@material-ui/core/Input";
import InputAdornment from "@material-ui/core/InputAdornment";
import IconButton from "@material-ui/core/IconButton";
import CalendarIcon from "@material-ui/icons/CalendarToday";
import Dialog from "@material-ui/core/Dialog";
import DialogContent from "@material-ui/core/DialogContent";
import { format, startOfMonth, addYears } from "date-fns";
import LeftArrowIcon from "@material-ui/icons/KeyboardArrowLeft";
import RightArrowIcon from "@material-ui/icons/KeyboardArrowRight";
import Button from "@material-ui/core/Button";
import { LazySeq } from "../data/lazyseq";
import { Typography } from "@material-ui/core";

export interface MonthSelectProps {
  curMonth: Date;
  onSelectMonth: (d: Date) => void;
}

const months: ReadonlyArray<ReadonlyArray<Date>> = LazySeq.ofRange(0, 12, 1)
  .map((m) => new Date(2019, m, 1))
  .chunk(3)
  .map((chunk) => chunk.toArray())
  .toArray();

export default React.memo(function MonthSelect(props: MonthSelectProps) {
  const [dialogOpen, setDialogOpen] = React.useState(false);
  const [tempDialogCurMonth, setTempDialogCurMonth] = React.useState<Date | undefined>(undefined);
  return (
    <>
      <Input
        type="text"
        value={format(tempDialogCurMonth || props.curMonth, "LLLL yyyy")}
        readOnly
        endAdornment={
          <InputAdornment position="end">
            <IconButton
              aria-label="Open month select"
              data-testid="open-month-select"
              onClick={() => {
                setTempDialogCurMonth(startOfMonth(props.curMonth));
                setDialogOpen(true);
              }}
            >
              <CalendarIcon />
            </IconButton>
          </InputAdornment>
        }
      />
      <Dialog
        open={dialogOpen}
        onClose={() => {
          setDialogOpen(false);
          setTempDialogCurMonth(undefined);
        }}
      >
        <DialogContent>
          <div style={{ display: "flex", alignItems: "center" }}>
            <IconButton
              data-testid="select-month-dialog-previous-year"
              onClick={() => setTempDialogCurMonth(addYears(tempDialogCurMonth || props.curMonth, -1))}
            >
              <LeftArrowIcon />
            </IconButton>
            <div style={{ flexGrow: 1 }}>
              <Typography variant="h5" align="center" data-testid="select-month-dialog-current-year">
                {format(tempDialogCurMonth || props.curMonth, "yyyy")}
              </Typography>
            </div>
            <IconButton onClick={() => setTempDialogCurMonth(addYears(tempDialogCurMonth || props.curMonth, 1))}>
              <RightArrowIcon />
            </IconButton>
          </div>
          <div data-testid="select-month-dialog-choose-month">
            {months.map((monthRow, monthRowIdx) => (
              <div key={monthRowIdx} style={monthRowIdx > 0 ? { marginTop: "1.2em" } : undefined}>
                {monthRow.map((month, monthIdx) => (
                  <Button
                    color="primary"
                    key={monthIdx}
                    style={monthIdx > 0 ? { marginLeft: "1.2em" } : undefined}
                    onClick={() => {
                      props.onSelectMonth(
                        new Date((tempDialogCurMonth || props.curMonth).getFullYear(), month.getMonth(), 1)
                      );
                      setTempDialogCurMonth(undefined);
                      setDialogOpen(false);
                    }}
                  >
                    {format(month, "MMM")}
                  </Button>
                ))}
              </div>
            ))}
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
});
