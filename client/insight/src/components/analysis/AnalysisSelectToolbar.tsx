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
import * as React from "react";
import { Radio } from "@mui/material";
import { FormControlLabel } from "@mui/material";
import { useRecoilValue } from "recoil";

import MonthSelect from "../MonthSelect";
import {
  selectedAnalysisPeriod,
  selectedMonth,
  useSetSpecificMonthWithoutLoading,
  useLoadSpecificMonth,
  useSetLast30,
} from "../../network/load-specific-month";

const toolbarStyle = {
  display: "flex",
  backgroundColor: "#E0E0E0",
  paddingLeft: "24px",
  paddingRight: "24px",
  minHeight: "2.5em",
  alignItems: "center",
  justifyContent: "space-evenly",
};

export default React.memo(function AnalysisSelectToolbar() {
  const period = useRecoilValue(selectedAnalysisPeriod);
  const selMonth = useRecoilValue(selectedMonth);
  const analyzeMonth = useLoadSpecificMonth();
  const setMonthWithoutLoading = useSetSpecificMonthWithoutLoading();
  const setLast30 = useSetLast30();

  return (
    <nav style={toolbarStyle}>
      <FormControlLabel
        control={<Radio checked={period.type === "Last30"} onChange={(e, checked) => (checked ? setLast30() : null)} />}
        label="Last 30 days"
      />
      <div style={{ display: "flex", alignItems: "center" }}>
        <FormControlLabel
          control={
            <Radio
              checked={period.type === "SpecificMonth"}
              onChange={(e, checked) => (checked ? analyzeMonth(selMonth) : null)}
            />
          }
          label="Select Month"
        />
        <MonthSelect
          curMonth={selMonth}
          onSelectMonth={(m) => {
            if (period.type === "SpecificMonth") {
              // if month type is selected, reload data
              analyzeMonth(m);
            } else {
              // otherwise, just store month
              setMonthWithoutLoading(m);
            }
          }}
        />
      </div>
    </nav>
  );
});
