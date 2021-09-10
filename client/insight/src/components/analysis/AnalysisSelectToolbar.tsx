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
import { Radio } from "@material-ui/core";
import { FormControlLabel } from "@material-ui/core";
import { atom, selector, useRecoilState, useRecoilTransaction_UNSTABLE } from "recoil";
import { startOfMonth } from "date-fns";

import MonthSelect from "../MonthSelect";
import { useLoadSpecificMonth } from "../../cell-status";

const toolbarStyle = {
  display: "flex",
  backgroundColor: "#E0E0E0",
  paddingLeft: "24px",
  paddingRight: "24px",
  minHeight: "2.5em",
  alignItems: "center",
  justifyContent: "space-evenly",
};

const selectType = atom<"Last30" | "SpecificMonth">({
  key: "analysisSelectType",
  default: "Last30",
});

const selectMonth = atom<Date>({ key: "analysisSelectMonth", default: startOfMonth(new Date()) });

export type SelectedAnalysisPeriod = { type: "Last30" } | { type: "SpecificMonth"; month: Date };
export const selectedAnalysisPeriod = selector<SelectedAnalysisPeriod>({
  key: "selectedAnalysisPeriod",
  get: ({ get }) => {
    const ty = get(selectType);
    if (ty === "Last30") {
      return { type: "Last30" };
    } else {
      return { type: "SpecificMonth", month: get(selectMonth) };
    }
  },
});

export default React.memo(function AnalysisSelectToolbar() {
  const [selTy, setSelTy] = useRecoilState(selectType);
  const [selMonth, setSelMonth] = useRecoilState(selectMonth);
  const loadSpecificMonth = useLoadSpecificMonth();

  const analyzeMonth = useRecoilTransaction_UNSTABLE(
    ({ get, set }) =>
      (m: Date) => {
        set(selectType, "SpecificMonth");
        if (get(selectMonth) !== m) {
          set(selectMonth, m);
          loadSpecificMonth(m);
        }
      },
    [loadSpecificMonth]
  );

  return (
    <nav style={toolbarStyle}>
      <FormControlLabel
        control={
          <Radio checked={selTy === "Last30"} onChange={(e, checked) => (checked ? setSelTy("Last30") : null)} />
        }
        label="Last 30 days"
      />
      <div style={{ display: "flex", alignItems: "center" }}>
        <FormControlLabel
          control={
            <Radio
              checked={selTy === "SpecificMonth"}
              onChange={(e, checked) => (checked ? analyzeMonth(selMonth) : null)}
            />
          }
          label="Select Month"
        />
        <MonthSelect
          curMonth={selMonth}
          onSelectMonth={(m) => {
            if (selTy === "SpecificMonth") {
              // if month type is selected, reload data
              analyzeMonth(m);
            } else {
              // otherwise, just store month
              setSelMonth(m);
            }
          }}
        />
      </div>
    </nav>
  );
});
