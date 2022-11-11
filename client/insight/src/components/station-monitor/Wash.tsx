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
import { Grid } from "@mui/material";
import { Button } from "@mui/material";

import { MaterialDialog, MatSummary } from "./Material.js";
import { WhiteboardRegion } from "./Whiteboard.js";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "./SelectWorkorder.js";
import { Tooltip } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentOperator } from "../../data/operators.js";
import { fmsInformation } from "../../network/server-settings.js";
import { useRecoilValue, useSetRecoilState } from "recoil";
import {
  materialDialogOpen,
  materialInDialogEvents,
  materialInDialogInfo,
  useAddExistingMaterialToQueue,
  useCloseMaterialDialog,
  useCompleteWash,
} from "../../cell-status/material-details.js";
import { last30MaterialSummary } from "../../cell-status/material-summary.js";
import { LogType } from "../../network/api.js";
import { instructionUrl } from "../../network/backend.js";

function CompleteWashButton() {
  const fmsInfo = useRecoilValue(fmsInformation);
  const mat = useRecoilValue(materialInDialogInfo);
  const [completeWash, completingWash] = useCompleteWash();
  const operator = useRecoilValue(currentOperator);
  const closeMatDialog = useCloseMaterialDialog();
  const toShow = useRecoilValue(materialDialogOpen);

  if (mat === null) return null;

  const requireScan = fmsInfo.requireScanAtWash;
  const requireWork = fmsInfo.requireWorkorderBeforeAllowWashComplete;
  let disallowCompleteReason: string | undefined;

  if (requireScan) {
    const usedScan =
      toShow !== null && (toShow.type === "Barcode" || toShow.type === "ManuallyEnteredSerial");
    if (!usedScan) {
      disallowCompleteReason = "Scan required at wash";
    }
  } else if (requireWork) {
    if (mat.workorderId === undefined || mat.workorderId === "") {
      disallowCompleteReason = "No workorder assigned";
    }
  }

  function markWashComplete() {
    if (mat === null) return;
    completeWash({
      mat,
      operator: operator,
    });
    closeMatDialog();
  }

  if (disallowCompleteReason) {
    return (
      <Tooltip title={disallowCompleteReason} placement="top">
        <div>
          <Button color="primary" disabled>
            Mark Wash Complete
          </Button>
        </div>
      </Tooltip>
    );
  } else {
    return (
      <Button color="primary" disabled={completingWash} onClick={markWashComplete}>
        Mark Wash Complete
      </Button>
    );
  }
}

function QuarantineMatButton() {
  const fmsInfo = useRecoilValue(fmsInformation);
  const [addToQueue, addingToQueue] = useAddExistingMaterialToQueue();
  const curMat = useRecoilValue(materialInDialogInfo);
  const operator = useRecoilValue(currentOperator);
  const closeMatDialog = useCloseMaterialDialog();

  const quarantineQueue = fmsInfo.quarantineQueue ?? null;

  if (!curMat || !quarantineQueue || quarantineQueue === "") return null;

  return (
    <Button
      color="primary"
      disabled={addingToQueue}
      onClick={() => {
        if (curMat) {
          addToQueue({
            materialId: curMat.materialID,
            queue: quarantineQueue,
            queuePosition: 0,
            operator: operator,
          });
        }
        closeMatDialog();
      }}
    >
      Quarantine
    </Button>
  );
}

function InstrButton() {
  const material = useRecoilValue(materialInDialogInfo);
  const matEvents = useRecoilValue(materialInDialogEvents);
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
  const url = instructionUrl(material.partName, "wash", material.materialID, null, maxProc, operator);
  return (
    <Button href={url} target="bms-instructions" color="primary">
      Instructions
    </Button>
  );
}

function AssignWorkorderButton() {
  const setWorkorderDialogOpen = useSetRecoilState(selectWorkorderDialogOpen);
  const mat = useRecoilValue(materialInDialogInfo);
  if (mat === null) return null;

  return (
    <Button color="primary" onClick={() => setWorkorderDialogOpen(true)}>
      Assign Workorder
    </Button>
  );
}

const WashMaterialDialog = React.memo(function WashDialog() {
  return (
    <MaterialDialog
      allowNote
      buttons={
        <>
          <InstrButton />
          <QuarantineMatButton />
          <CompleteWashButton />
          <AssignWorkorderButton />
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
          e.completed_last_proc_machining === true &&
          e.last_unload_time !== undefined &&
          e.last_unload_time >= cutoff
      )
      .toMutableArray();
    // sort decending
    recent.sort((e1, e2) =>
      e1.last_unload_time && e2.last_unload_time
        ? e2.last_unload_time.getTime() - e1.last_unload_time.getTime()
        : 0
    );
    return recent;
  }, [matSummary]);

  const unwashed = LazySeq.of(recentCompleted).filter((m) => m.wash_completed === undefined);
  const washed = LazySeq.of(recentCompleted).filter((m) => m.wash_completed !== undefined);

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
      <WashMaterialDialog />
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
