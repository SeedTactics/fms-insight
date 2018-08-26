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
import * as im from "immutable";
import { addHours } from "date-fns";
import Grid from "@material-ui/core/Grid";
import Button from "@material-ui/core/Button";
import { DialogActions } from "@material-ui/core";
import { createSelector } from "reselect";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

import { MaterialSummary } from "../../data/events";
import { Store, connect, mkAC, AppActionBeforeMiddleware } from "../../store/store";
import { MaterialDialogProps, MaterialDialog, MatSummary, WhiteboardRegion, InstructionButton } from "./Material";
import * as matDetails from "../../data/material-details";
import { MaterialSummaryAndCompletedData } from "../../data/events.matsummary";
import SerialScanner from "./QRScan";

interface InspButtonsProps {
  readonly display_material: matDetails.MaterialDetail;
  readonly operator?: string;
  readonly inspection_type: string;
  readonly completeInspection: (comp: matDetails.CompleteInspectionData) => void;
}

function InspButtons(props: InspButtonsProps) {
  function markInspComplete(success: boolean) {
    if (!props.display_material) {
      return;
    }

    props.completeInspection({
      mat: props.display_material,
      inspType: props.inspection_type,
      success,
      operator: props.operator
    });
  }

  return (
    <>
      {props.display_material && props.display_material.partName !== "" ? (
        <InstructionButton material={props.display_material} type={props.inspection_type} />
      ) : (
        undefined
      )}
      <Button color="primary" onClick={() => markInspComplete(true)}>
        Mark {props.inspection_type} Success
      </Button>
      <Button color="primary" onClick={() => markInspComplete(false)}>
        Mark {props.inspection_type} Failed
      </Button>
    </>
  );
}

interface InspDialogProps extends MaterialDialogProps {
  readonly operator?: string;
  readonly focusInspectionType: string;
  readonly completeInspection: (comp: matDetails.CompleteInspectionData) => void;
}

function InspDialog(props: InspDialogProps) {
  const displayMat = props.display_material;
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
      display_material={props.display_material}
      onClose={props.onClose}
      extraDialogElements={
        !displayMat || !multipleInspTypes ? (
          undefined
        ) : (
          <>
            {multipleInspTypes.map(i => (
              <DialogActions key={i}>
                <InspButtons
                  display_material={displayMat}
                  operator={props.operator}
                  inspection_type={i}
                  completeInspection={props.completeInspection}
                />
              </DialogActions>
            ))}
          </>
        )
      }
      buttons={
        !singleInspectionType || !displayMat ? (
          undefined
        ) : (
          <InspButtons
            display_material={displayMat}
            operator={props.operator}
            inspection_type={singleInspectionType}
            completeInspection={props.completeInspection}
          />
        )
      }
    />
  );
}

const ConnectedInspDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material,
    focusInspectionType: st.Route.selected_insp_type || "",
    operator: st.Operators.current
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
    completeInspection: (data: matDetails.CompleteInspectionData) =>
      [
        matDetails.completeInspection(data),
        { type: matDetails.ActionType.CloseMaterialDialog }
      ] as AppActionBeforeMiddleware
  }
)(InspDialog);

interface PartsForInspection {
  readonly waiting_to_inspect: ReadonlyArray<MaterialSummary>;
  readonly inspect_completed: ReadonlyArray<MaterialSummary>;
}

interface InspectionProps {
  readonly recent_inspections: PartsForInspection;
  readonly focusInspectionType: string;
  readonly openMat: (mat: MaterialSummary) => void;
}

function Inspection(props: InspectionProps) {
  let title = "Inspection - FMS Insight";
  if (props.focusInspectionType !== "") {
    title = "Inspection " + props.focusInspectionType + " - FMS Insight";
  }

  return (
    <DocumentTitle title={title}>
      <main data-testid="stationmonitor-inspection" style={{ padding: "8px" }}>
        <Grid container spacing={16}>
          <Grid item xs={12} md={6}>
            <WhiteboardRegion label="Parts to Inspect" borderRight borderBottom>
              {props.recent_inspections.waiting_to_inspect.map((m, idx) => (
                <MatSummary key={idx} mat={m} onOpen={props.openMat} hideInspectionIcon />
              ))}
            </WhiteboardRegion>
          </Grid>
          <Grid item xs={12} md={6}>
            <WhiteboardRegion label="Recently Inspected" borderLeft borderBottom>
              {props.recent_inspections.inspect_completed.map((m, idx) => (
                <MatSummary
                  key={idx}
                  mat={m}
                  onOpen={props.openMat}
                  focusInspectionType={props.focusInspectionType}
                  hideInspectionIcon
                />
              ))}
            </WhiteboardRegion>
          </Grid>
        </Grid>
        <ConnectedInspDialog />
        <SerialScanner />
      </main>
    </DocumentTitle>
  );
}

const extractRecentInspections = createSelector(
  (st: Store) => st.Events.last30.mat_summary.matsById,
  (st: Store) => st.Route.selected_insp_type,
  (mats: im.Map<number, MaterialSummaryAndCompletedData>, inspType: string | undefined): PartsForInspection => {
    const cutoff = addHours(new Date(), -36);
    const allDetails = mats.valueSeq().filter(e => e.completed_time !== undefined && e.completed_time >= cutoff);

    function checkAllCompleted(m: MaterialSummaryAndCompletedData): boolean {
      return im
        .Set(m.signaledInspections)
        .subtract(im.Seq(m.completedInspections || {}).keySeq())
        .isEmpty();
    }

    const uninspected =
      inspType === undefined
        ? allDetails.filter(m => m.signaledInspections.length > 0 && !checkAllCompleted(m))
        : allDetails.filter(
            m => m.signaledInspections.indexOf(inspType) >= 0 && (m.completedInspections || {})[inspType] === undefined
          );

    const inspected =
      inspType === undefined
        ? allDetails.filter(m => m.signaledInspections.length > 0 && checkAllCompleted(m))
        : allDetails.filter(
            m => m.signaledInspections.indexOf(inspType) >= 0 && (m.completedInspections || {})[inspType] !== undefined
          );

    return {
      waiting_to_inspect: uninspected
        .sortBy(e => e.completed_time)
        .reverse()
        .toArray(),
      inspect_completed: inspected
        .sortBy(e => e.completed_time)
        .reverse()
        .toArray()
    };
  }
);

export default connect(
  (st: Store) => ({
    recent_inspections: extractRecentInspections(st),
    focusInspectionType: st.Route.selected_insp_type || ""
  }),
  {
    openMat: matDetails.openMaterialDialog
  }
)(Inspection);
