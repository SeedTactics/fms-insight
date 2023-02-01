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
import { addDays } from "date-fns";
import { Grid } from "@mui/material";
import { Button } from "@mui/material";
import { DialogActions } from "@mui/material";

import { MaterialDialog, MatSummary } from "./Material.js";
import { WhiteboardRegion } from "./Whiteboard.js";
import * as matDetails from "../../cell-status/material-details.js";
import { MaterialSummaryAndCompletedData, MaterialSummary } from "../../cell-status/material-summary.js";
import { currentOperator } from "../../data/operators.js";
import { useRecoilValue } from "recoil";
import { last30MaterialSummary } from "../../cell-status/material-summary.js";
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";
import { instructionUrl } from "../../network/backend.js";
import { LogType } from "../../network/api.js";
import { QuarantineMatButton } from "./QuarantineButton.js";

interface InspButtonsProps {
  readonly inspection_type: string;
}

function InspButtons(props: InspButtonsProps) {
  const operator = useRecoilValue(currentOperator);
  const material = useRecoilValue(matDetails.materialInDialogInfo);
  const matEvents = useRecoilValue(matDetails.materialInDialogEvents);
  const [completeInsp, completeInspUpdating] = matDetails.useCompleteInspection();
  const closeMatDialog = matDetails.useCloseMaterialDialog();

  if (material === null) return null;

  function markInspComplete(success: boolean) {
    if (!material) {
      return;
    }

    completeInsp({
      mat: material,
      inspType: props.inspection_type,
      success,
      operator: operator,
    });

    closeMatDialog();
  }

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
  const url = instructionUrl(
    material.partName,
    props.inspection_type,
    material.materialID,
    null,
    maxProc,
    operator
  );

  return (
    <>
      {material.partName !== "" ? (
        <Button href={url} target="bms-instructions" color="primary">
          Instructions
        </Button>
      ) : undefined}
      <QuarantineMatButton />
      <Button color="primary" disabled={completeInspUpdating} onClick={() => markInspComplete(true)}>
        Mark {props.inspection_type} Success
      </Button>
      <Button color="primary" disabled={completeInspUpdating} onClick={() => markInspComplete(false)}>
        Mark {props.inspection_type} Failed
      </Button>
    </>
  );
}

interface InspDialogProps {
  readonly focusInspectionType: string | null;
}

function DialogBodyInspButtons({ focusInspectionType }: InspDialogProps) {
  const material = useRecoilValue(matDetails.materialInDialogInspections);
  if (material === null || focusInspectionType || material.signaledInspections.length === 1) return null;

  return (
    <>
      {material.signaledInspections.map((i) => (
        <DialogActions key={i}>
          <InspButtons inspection_type={i} />
        </DialogActions>
      ))}
    </>
  );
}

function DialogActionInspButtons({ focusInspectionType }: InspDialogProps) {
  const material = useRecoilValue(matDetails.materialInDialogInspections);
  let singleInspectionType: string;
  if (focusInspectionType) {
    singleInspectionType = focusInspectionType;
  } else if (material && material.signaledInspections.length === 1) {
    singleInspectionType = material.signaledInspections[0];
  } else {
    return null;
  }

  return <InspButtons inspection_type={singleInspectionType} />;
}

const InspMaterialDialog = React.memo(function InspMaterialDialog(props: InspDialogProps) {
  return (
    <MaterialDialog
      allowNote
      extraDialogElements={<DialogBodyInspButtons focusInspectionType={props.focusInspectionType} />}
      buttons={<DialogActionInspButtons focusInspectionType={props.focusInspectionType} />}
    />
  );
});

interface PartsForInspection {
  readonly waiting_to_inspect: ReadonlyArray<MaterialSummary>;
  readonly inspect_completed: ReadonlyArray<MaterialSummary>;
}

interface InspectionProps {
  readonly focusInspectionType: string | null;
}

export function Inspection(props: InspectionProps): JSX.Element {
  const matSummary = useRecoilValue(last30MaterialSummary);
  const recent_inspections = React.useMemo(
    () => extractRecentInspections(matSummary.matsById, props.focusInspectionType),
    [matSummary, props.focusInspectionType]
  );

  return (
    <div data-testid="stationmonitor-inspection" style={{ padding: "8px" }}>
      <Grid container spacing={2}>
        <Grid item xs={12} md={6}>
          <WhiteboardRegion label="Parts to Inspect" borderTop borderLeft borderRight borderBottom>
            {recent_inspections.waiting_to_inspect.map((m, idx) => (
              <MatSummary
                key={idx}
                mat={m}
                focusInspectionType={props.focusInspectionType}
                hideInspectionIcon
              />
            ))}
          </WhiteboardRegion>
        </Grid>
        <Grid item xs={12} md={6}>
          <WhiteboardRegion label="Recently Inspected" borderTop borderLeft borderRight borderBottom>
            {recent_inspections.inspect_completed.map((m, idx) => (
              <MatSummary
                key={idx}
                mat={m}
                focusInspectionType={props.focusInspectionType}
                hideInspectionIcon
              />
            ))}
          </WhiteboardRegion>
        </Grid>
      </Grid>
      <InspMaterialDialog focusInspectionType={props.focusInspectionType} />
    </div>
  );
}

function extractRecentInspections(
  mats: HashMap<number, MaterialSummaryAndCompletedData>,
  inspType: string | null
): PartsForInspection {
  const uninspectedCutoff = addDays(new Date(), -7);
  const inspectedCutoff = addDays(new Date(), -1);

  function checkAllCompleted(m: MaterialSummaryAndCompletedData): boolean {
    const comp = m.completedInspections;
    if (comp === undefined) {
      return m.signaledInspections.length === 0;
    } else {
      return LazySeq.of(m.signaledInspections).allMatch((s) => s in comp);
    }
  }

  const uninspected = Array.from(
    inspType === null
      ? mats
          .valuesToLazySeq()
          .filter(
            (m) =>
              m.last_unload_time !== undefined &&
              m.last_unload_time >= uninspectedCutoff &&
              m.signaledInspections.length > 0 &&
              !checkAllCompleted(m)
          )
      : mats
          .valuesToLazySeq()
          .filter(
            (m) =>
              m.last_unload_time !== undefined &&
              m.last_unload_time >= uninspectedCutoff &&
              m.signaledInspections.includes(inspType) &&
              (m.completedInspections || {})[inspType] === undefined
          )
  );
  // sort descending
  uninspected.sort((e1, e2) =>
    e1.last_unload_time && e2.last_unload_time
      ? e2.last_unload_time.getTime() - e1.last_unload_time.getTime()
      : 0
  );

  const inspected = Array.from(
    inspType === null
      ? mats
          .valuesToLazySeq()
          .filter(
            (m) =>
              m.completed_inspect_time !== undefined &&
              m.completed_inspect_time >= inspectedCutoff &&
              m.signaledInspections.length > 0 &&
              checkAllCompleted(m)
          )
      : mats
          .valuesToLazySeq()
          .filter(
            (m) =>
              m.completed_inspect_time !== undefined &&
              m.completed_inspect_time >= inspectedCutoff &&
              m.signaledInspections.includes(inspType) &&
              (m.completedInspections || {})[inspType] !== undefined
          )
  );
  // sort descending
  inspected.sort((e1, e2) =>
    e1.completed_inspect_time && e2.completed_inspect_time
      ? e2.completed_inspect_time.getTime() - e1.completed_inspect_time.getTime()
      : 0
  );

  return {
    waiting_to_inspect: uninspected,
    inspect_completed: inspected,
  };
}

export default function InspectionPage(props: InspectionProps): JSX.Element {
  React.useEffect(() => {
    let title = "Inspection - FMS Insight";
    if (props.focusInspectionType != null && props.focusInspectionType !== "") {
      title = "Inspection " + props.focusInspectionType + " - FMS Insight";
    }
    document.title = title;
  }, [props.focusInspectionType]);

  return (
    <main style={{ backgroundColor: "#F8F8F8", minHeight: "calc(100vh - 64px)" }}>
      <Inspection {...props} />
    </main>
  );
}
