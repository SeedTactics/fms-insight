/* Copyright (c) 2022, John Lenz

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
import { addHours } from "date-fns";
import { Box, Typography } from "@mui/material";
import { Button } from "@mui/material";

import { MaterialDialog, MatSummary } from "./Material.js";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "./SelectWorkorder.js";
import { Tooltip } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentOperator } from "../../data/operators.js";
import { fmsInformation } from "../../network/server-settings.js";
import { atom, useRecoilValue, useSetRecoilState } from "recoil";
import {
  materialDialogOpen,
  materialInDialogEvents,
  materialInDialogInfo,
  useCompleteCloseout,
} from "../../cell-status/material-details.js";
import {
  last30MaterialSummary,
  MaterialSummaryAndCompletedData,
} from "../../cell-status/material-summary.js";
import { LogType } from "../../network/api.js";
import { instructionUrl } from "../../network/backend.js";
import { QuarantineMatButton } from "./QuarantineButton.js";
import { useSetTitle } from "../routes.js";
import { useAtom, useAtomValue } from "jotai";

function CompleteButton() {
  const fmsInfo = useRecoilValue(fmsInformation);
  const mat = useAtomValue(materialInDialogInfo);
  const [complete, isCompleting] = useCompleteCloseout();
  const operator = useRecoilValue(currentOperator);
  const [toShow, setToShow] = useAtom(materialDialogOpen);

  if (mat === null) return null;

  const requireScan = fmsInfo.requireScanAtCloseout;
  const requireWork = fmsInfo.requireWorkorderBeforeAllowCloseoutComplete;
  let disallowCompleteReason: string | undefined;

  if (requireScan) {
    const usedScan =
      toShow !== null && (toShow.type === "Barcode" || toShow.type === "ManuallyEnteredSerial");
    if (!usedScan) {
      disallowCompleteReason = "Scan Required";
    }
  } else if (requireWork) {
    if (mat.workorderId === undefined || mat.workorderId === "") {
      disallowCompleteReason = "No workorder assigned";
    }
  }

  function markComplete() {
    if (mat === null) return;
    complete({
      mat,
      operator: operator,
    });
    setToShow(null);
  }

  if (disallowCompleteReason) {
    return (
      <Tooltip title={disallowCompleteReason} placement="top">
        <div>
          <Button color="primary" disabled>
            Close Out
          </Button>
        </div>
      </Tooltip>
    );
  } else {
    return (
      <Button color="primary" disabled={isCompleting} onClick={markComplete}>
        Close Out
      </Button>
    );
  }
}

function InstrButton() {
  const material = useAtomValue(materialInDialogInfo);
  const matEvents = useAtomValue(materialInDialogEvents);
  const operator = useRecoilValue(currentOperator);

  if (material === null || material.partName === "") return null;

  const maxProc =
    LazySeq.of(matEvents)
      .filter(
        (e) =>
          e.details?.["PalletCycleInvalidated"] !== "1" &&
          (e.type === LogType.LoadUnloadCycle ||
            e.type === LogType.MachineCycle ||
            e.type === LogType.AddToQueue)
      )
      .flatMap((e) => e.material)
      .filter((e) => e.id === material.materialID)
      .maxBy((e) => e.proc)?.proc ?? null;
  const url = instructionUrl(material.partName, "closeout", material.materialID, null, maxProc, operator);
  return (
    <Button href={url} target="bms-instructions" color="primary">
      Instructions
    </Button>
  );
}

function AssignWorkorderButton() {
  const setWorkorderDialogOpen = useSetRecoilState(selectWorkorderDialogOpen);
  const mat = useAtomValue(materialInDialogInfo);
  if (mat === null) return null;

  return (
    <Button color="primary" onClick={() => setWorkorderDialogOpen(true)}>
      Assign Workorder
    </Button>
  );
}

const CloseoutMaterialDialog = React.memo(function CloseoutDialog() {
  return (
    <MaterialDialog
      allowNote
      buttons={
        <>
          <InstrButton />
          <QuarantineMatButton />
          <AssignWorkorderButton />
          <CompleteButton />
        </>
      }
    />
  );
});

const currentNearestMinutes = atom<Date>({
  key: "closeout/nearestminute",
  default: new Date(),
  effects: [
    ({ setSelf }) => {
      const interval = setInterval(() => {
        setSelf(new Date());
      }, 1000 * 60);
      return () => clearInterval(interval);
    },
  ],
});

export function Closeout(): JSX.Element {
  const matSummary = useRecoilValue(last30MaterialSummary);
  const nearestMinute = useRecoilValue(currentNearestMinutes);

  const material = React.useMemo(() => {
    const cutoff = addHours(nearestMinute, -48);
    const closedCutoff = addHours(nearestMinute, -2);
    const uncompleted: Array<MaterialSummaryAndCompletedData> = [];
    const closed: Array<MaterialSummaryAndCompletedData> = [];
    for (const m of matSummary.matsById.values()) {
      if (m.completed_last_proc_machining === true && m.last_unload_time && m.last_unload_time >= cutoff) {
        if (m.closeout_completed === undefined) {
          uncompleted.push(m);
        } else if (m.closeout_completed >= closedCutoff) {
          closed.push(m);
        }
      }
    }

    // sort ascending
    uncompleted.sort((e1, e2) =>
      e1.last_unload_time && e2.last_unload_time
        ? e1.last_unload_time.getTime() - e2.last_unload_time.getTime()
        : 0
    );
    // sort descending
    closed.sort((e1, e2) =>
      e1.last_unload_time && e2.last_unload_time
        ? e2.last_unload_time.getTime() - e1.last_unload_time.getTime()
        : 0
    );

    return { uncompleted, closed };
  }, [matSummary, nearestMinute]);

  return (
    <>
      <Box sx={{ display: { md: "flex" } }}>
        <Box
          padding="8px"
          sx={{
            minHeight: { md: "calc(100vh - 64px)" },
            width: { md: "50vw" },
            borderRight: { md: "1px solid black" },
            borderBottom: { sm: "1px solid black", md: "none" },
          }}
        >
          <Typography variant="h4">Recently Completed</Typography>
          <Box display="flex" justifyContent="flex-start" flexWrap="wrap">
            {material.uncompleted.map((m, idx) => (
              <MatSummary key={idx} mat={m} />
            ))}
          </Box>
        </Box>
        <Box padding="8px" sx={{ width: { md: "50vw" } }}>
          <Typography variant="h4">Recently Closed Out</Typography>
          <Box display="flex" justifyContent="flex-start" flexWrap="wrap">
            {material.closed.map((m, idx) => (
              <MatSummary key={idx} mat={m} />
            ))}
          </Box>
        </Box>
      </Box>
      <SelectWorkorderDialog />
      <CloseoutMaterialDialog />
    </>
  );
}

export function CloseoutPage(): JSX.Element {
  useSetTitle("Close Out");

  return (
    <Box
      component="main"
      sx={{
        backgroundColor: "#F8F8F8",
        minHeight: { sm: "calc(100vh - 64px - 40px)", md: "calc(100vh - 64px)" },
      }}
    >
      <Closeout />
    </Box>
  );
}
