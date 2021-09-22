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
import { addDays } from "date-fns";
import { Grid } from "@material-ui/core";
import { Button } from "@material-ui/core";
import { Tooltip } from "@material-ui/core";
import { DialogActions } from "@material-ui/core";

import { MaterialDialog, MatSummary, WhiteboardRegion, InstructionButton } from "./Material";
import * as matDetails from "../../cell-status/material-details";
import { MaterialSummaryAndCompletedData, MaterialSummary } from "../../cell-status/material-summary";
import { HashMap, HashSet } from "prelude-ts";
import { LazySeq } from "../../util/lazyseq";
import { currentOperator } from "../../data/operators";
import { useRecoilValue, useSetRecoilState } from "recoil";
import { fmsInformation } from "../../network/server-settings";
import { last30MaterialSummary } from "../../cell-status/material-summary";

interface InspButtonsProps {
  readonly display_material: matDetails.MaterialDetail;
  readonly inspection_type: string;
}

function InspButtons(props: InspButtonsProps) {
  const operator = useRecoilValue(currentOperator);
  const quarantineQueue = useRecoilValue(fmsInformation).quarantineQueue ?? null;
  const [completeInsp, completeInspUpdating] = matDetails.useCompleteInspection();
  const [addExistingToQueue, addExistingToQueueUpdating] = matDetails.useAddExistingMaterialToQueue();
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);

  function markInspComplete(success: boolean) {
    if (!props.display_material) {
      return;
    }

    completeInsp({
      mat: props.display_material,
      inspType: props.inspection_type,
      success,
      operator: operator,
    });

    setMatToShow(null);
  }

  return (
    <>
      {props.display_material && props.display_material.partName !== "" ? (
        <InstructionButton
          material={props.display_material}
          type={props.inspection_type}
          operator={operator}
          pallet={null}
        />
      ) : undefined}
      {props.display_material && quarantineQueue !== null ? (
        <Tooltip title={"Move to " + quarantineQueue}>
          <Button
            color="primary"
            disabled={addExistingToQueueUpdating}
            onClick={() => {
              if (props.display_material && quarantineQueue) {
                addExistingToQueue({
                  materialId: props.display_material.materialID,
                  queue: quarantineQueue,
                  queuePosition: 0,
                  operator: operator || null,
                });
              }
              setMatToShow(null);
            }}
          >
            Quarantine Material
          </Button>
        </Tooltip>
      ) : undefined}
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

function InspDialog(props: InspDialogProps) {
  const displayMat = useRecoilValue(matDetails.materialDetail);
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);

  let singleInspectionType: string | undefined;
  let multipleInspTypes: ReadonlyArray<string> | undefined;
  if (props.focusInspectionType) {
    singleInspectionType = props.focusInspectionType;
  } else if (displayMat) {
    if (displayMat.signaledInspections.length === 1) {
      singleInspectionType = displayMat.signaledInspections[0];
    } else if (displayMat.signaledInspections.length > 1) {
      multipleInspTypes = displayMat.signaledInspections;
    }
  }
  return (
    <MaterialDialog
      display_material={displayMat}
      allowNote
      onClose={() => setMatToShow(null)}
      extraDialogElements={
        !displayMat || !multipleInspTypes ? undefined : (
          <>
            {multipleInspTypes.map((i) => (
              <DialogActions key={i}>
                <InspButtons display_material={displayMat} inspection_type={i} />
              </DialogActions>
            ))}
          </>
        )
      }
      buttons={
        !singleInspectionType || !displayMat ? undefined : (
          <InspButtons display_material={displayMat} inspection_type={singleInspectionType} />
        )
      }
    />
  );
}

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
          <WhiteboardRegion label="Parts to Inspect" borderRight borderBottom>
            {recent_inspections.waiting_to_inspect.map((m, idx) => (
              <MatSummary key={idx} mat={m} focusInspectionType={props.focusInspectionType} hideInspectionIcon />
            ))}
          </WhiteboardRegion>
        </Grid>
        <Grid item xs={12} md={6}>
          <WhiteboardRegion label="Recently Inspected" borderLeft borderBottom>
            {recent_inspections.inspect_completed.map((m, idx) => (
              <MatSummary key={idx} mat={m} focusInspectionType={props.focusInspectionType} hideInspectionIcon />
            ))}
          </WhiteboardRegion>
        </Grid>
      </Grid>
      <InspDialog focusInspectionType={props.focusInspectionType} />
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
    return HashSet.ofIterable(m.signaledInspections)
      .removeAll(m.completedInspections ? Object.keys(m.completedInspections) : [])
      .isEmpty();
  }

  const uninspected = Array.from(
    inspType === null
      ? LazySeq.ofIterable(mats.valueIterable()).filter(
          (m) =>
            m.last_unload_time !== undefined &&
            m.last_unload_time >= uninspectedCutoff &&
            m.signaledInspections.length > 0 &&
            !checkAllCompleted(m)
        )
      : LazySeq.ofIterable(mats.valueIterable()).filter(
          (m) =>
            m.last_unload_time !== undefined &&
            m.last_unload_time >= uninspectedCutoff &&
            m.signaledInspections.includes(inspType) &&
            (m.completedInspections || {})[inspType] === undefined
        )
  );
  // sort descending
  uninspected.sort((e1, e2) =>
    e1.last_unload_time && e2.last_unload_time ? e2.last_unload_time.getTime() - e1.last_unload_time.getTime() : 0
  );

  const inspected = Array.from(
    inspType === null
      ? LazySeq.ofIterable(mats.valueIterable()).filter(
          (m) =>
            m.completed_inspect_time !== undefined &&
            m.completed_inspect_time >= inspectedCutoff &&
            m.signaledInspections.length > 0 &&
            checkAllCompleted(m)
        )
      : LazySeq.ofIterable(mats.valueIterable()).filter(
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
    <main>
      <Inspection {...props} />
    </main>
  );
}
