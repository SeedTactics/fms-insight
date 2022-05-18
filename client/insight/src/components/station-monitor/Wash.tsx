/* Copyright (c) 2021, John Lenz

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
import { Grid } from "@mui/material";
import { Button } from "@mui/material";

import { MaterialDialog, WhiteboardRegion, MatSummary, InstructionButton } from "./Material";
import { SelectWorkorderDialog } from "./SelectWorkorder";
import { Tooltip } from "@mui/material";
import { LazySeq } from "../../util/lazyseq";
import { currentOperator } from "../../data/operators";
import { fmsInformation } from "../../network/server-settings";
import { useRecoilState, useRecoilValue, useSetRecoilState } from "recoil";
import {
  loadWorkordersForMaterialInDialog,
  materialDetail,
  materialToShowInDialog,
  useAddExistingMaterialToQueue,
  useCompleteWash,
} from "../../cell-status/material-details";
import { last30MaterialSummary } from "../../cell-status/material-summary";

const WashDialog = React.memo(function WashDialog() {
  const operator = useRecoilValue(currentOperator);
  const fmsInfo = useRecoilValue(fmsInformation);
  const displayMat = useRecoilValue(materialDetail);
  const [matToDisplay, setMatToDisplay] = useRecoilState(materialToShowInDialog);
  const [completeWash, completingWash] = useCompleteWash();
  const [addToQueue, addingToQueue] = useAddExistingMaterialToQueue();
  const setWorkorderDialogOpen = useSetRecoilState(loadWorkordersForMaterialInDialog);

  function markWashComplete() {
    if (!displayMat) {
      return;
    }

    completeWash({
      mat: displayMat,
      operator: operator,
    });
    setMatToDisplay(null);
  }
  function openAssignWorkorder() {
    if (!displayMat) {
      return;
    }
    setWorkorderDialogOpen(true);
  }

  const requireScan = fmsInfo.requireScanAtWash;
  const requireWork = fmsInfo.requireWorkorderBeforeAllowWashComplete;
  let disallowCompleteReason: string | undefined;

  if (requireScan && displayMat && matToDisplay?.type !== "Serial") {
    disallowCompleteReason = "Scan required at wash";
  } else if (requireWork && displayMat) {
    if (displayMat.workorderId === undefined || displayMat.workorderId === "") {
      disallowCompleteReason = "No workorder assigned";
    }
  }

  const quarantineQueue = fmsInfo.quarantineQueue || null;

  return (
    <MaterialDialog
      display_material={displayMat}
      onClose={() => setMatToDisplay(null)}
      allowNote
      buttons={
        <>
          {displayMat && displayMat.partName !== "" ? (
            <InstructionButton material={displayMat} type="wash" operator={operator} pallet={null} />
          ) : undefined}
          {displayMat && quarantineQueue !== null ? (
            <Tooltip title={"Move to " + quarantineQueue}>
              <Button
                color="primary"
                disabled={addingToQueue}
                onClick={() => {
                  if (displayMat) {
                    addToQueue({
                      materialId: displayMat.materialID,
                      queue: quarantineQueue,
                      queuePosition: 0,
                      operator: operator,
                    });
                  }
                  setMatToDisplay(null);
                }}
              >
                Quarantine Material
              </Button>
            </Tooltip>
          ) : undefined}
          {disallowCompleteReason ? (
            <Tooltip title={disallowCompleteReason} placement="top">
              <div>
                <Button color="primary" disabled>
                  Mark Wash Complete
                </Button>
              </div>
            </Tooltip>
          ) : (
            <Button color="primary" disabled={completingWash} onClick={markWashComplete}>
              Mark Wash Complete
            </Button>
          )}
          <Button color="primary" onClick={openAssignWorkorder}>
            {displayMat && displayMat.workorderId ? "Change Workorder" : "Assign Workorder"}
          </Button>
        </>
      }
    />
  );
});

export function Wash(): JSX.Element {
  const matSummary = useRecoilValue(last30MaterialSummary);
  const recentCompleted = React.useMemo(() => {
    const cutoff = addHours(new Date(), -36);
    const recent = matSummary.matsById
      .valuesToLazySeq()
      .filter(
        (e) =>
          e.completed_last_proc_machining === true && e.last_unload_time !== undefined && e.last_unload_time >= cutoff
      )
      .toMutableArray();
    // sort decending
    recent.sort((e1, e2) =>
      e1.last_unload_time && e2.last_unload_time ? e2.last_unload_time.getTime() - e1.last_unload_time.getTime() : 0
    );
    return recent;
  }, [matSummary]);

  const unwashed = LazySeq.ofIterable(recentCompleted).filter((m) => m.wash_completed === undefined);
  const washed = LazySeq.ofIterable(recentCompleted).filter((m) => m.wash_completed !== undefined);

  return (
    <div data-testid="stationmonitor-wash" style={{ padding: "8px" }}>
      <Grid container spacing={2}>
        <Grid item xs={12} md={6}>
          <WhiteboardRegion label="Recently completed parts not yet washed" borderRight borderBottom>
            {unwashed.map((m, idx) => (
              <MatSummary key={idx} mat={m} />
            ))}
          </WhiteboardRegion>
        </Grid>
        <Grid item xs={12} md={6}>
          <WhiteboardRegion label="Recently Washed Parts" borderLeft borderBottom>
            {washed.map((m, idx) => (
              <MatSummary key={idx} mat={m} />
            ))}
          </WhiteboardRegion>
        </Grid>
      </Grid>
      <SelectWorkorderDialog />
      <WashDialog />
    </div>
  );
}

export default function WashPage(): JSX.Element {
  React.useEffect(() => {
    document.title = "Wash - FMS Insight";
  }, []);

  return (
    <main>
      <Wash />
    </main>
  );
}
