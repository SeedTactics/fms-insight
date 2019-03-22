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
import { addHours } from "date-fns";
import Grid from "@material-ui/core/Grid";
import Button from "@material-ui/core/Button";
import { createSelector } from "reselect";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

import { Store, connect, mkAC } from "../../store/store";
import { MaterialDialog, WhiteboardRegion, MatSummary, MaterialDialogProps, InstructionButton } from "./Material";
import * as matDetails from "../../data/material-details";
import * as guiState from "../../data/gui-state";
import * as api from "../../data/api";
import SelectWorkorderDialog from "./SelectWorkorder";
import { MaterialSummaryAndCompletedData, MaterialSummary } from "../../data/events.matsummary";
import Tooltip from "@material-ui/core/Tooltip";
import { HashMap } from "prelude-ts";
import { LazySeq } from "../../data/lazyseq";

interface WashDialogProps extends MaterialDialogProps {
  readonly operator?: string;
  readonly fmsInfo?: Readonly<api.IFMSInfo>;
  readonly completeWash: (mat: matDetails.CompleteWashData) => void;
  readonly openSelectWorkorder: (mat: matDetails.MaterialDetail) => void;
}

function WashDialog(props: WashDialogProps) {
  function markWashComplete() {
    if (!props.display_material) {
      return;
    }

    props.completeWash({
      mat: props.display_material,
      operator: props.operator
    });
  }
  function openAssignWorkorder() {
    if (!props.display_material) {
      return;
    }
    props.openSelectWorkorder(props.display_material);
  }

  const requireScan = props.fmsInfo ? props.fmsInfo.requireScanAtWash : false;
  const requireWork = props.fmsInfo ? props.fmsInfo.requireWorkorderBeforeAllowWashComplete : false;
  let disallowCompleteReason: string | undefined;

  if (requireScan && props.display_material && !props.display_material.openedViaBarcodeScanner) {
    disallowCompleteReason = "Scan required at wash";
  } else if (requireWork && props.display_material) {
    if (props.display_material.workorderId === undefined || props.display_material.workorderId === "") {
      disallowCompleteReason = "No workorder assigned";
    }
  }

  return (
    <MaterialDialog
      display_material={props.display_material}
      onClose={props.onClose}
      buttons={
        <>
          {props.display_material && props.display_material.partName !== "" ? (
            <InstructionButton material={props.display_material} type="wash" />
          ) : (
            undefined
          )}
          {disallowCompleteReason ? (
            <Tooltip title={disallowCompleteReason} placement="top">
              <div>
                <Button color="primary" disabled>
                  Mark Wash Complete
                </Button>
              </div>
            </Tooltip>
          ) : (
            <Button color="primary" onClick={markWashComplete}>
              Mark Wash Complete
            </Button>
          )}
          <Button color="primary" onClick={openAssignWorkorder}>
            {props.display_material && props.display_material.workorderId ? "Change Workorder" : "Assign Workorder"}
          </Button>
        </>
      }
    />
  );
}

const ConnectedWashDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material,
    operator: st.ServerSettings.user
      ? st.ServerSettings.user.profile.name || st.ServerSettings.user.profile.sub
      : st.Operators.current,
    fmsInfo: st.ServerSettings.fmsInfo
  }),
  {
    completeWash: (d: matDetails.CompleteWashData) => [
      matDetails.completeWash(d),
      { type: matDetails.ActionType.CloseMaterialDialog }
    ],
    openSelectWorkorder: (mat: matDetails.MaterialDetail) => [
      {
        type: guiState.ActionType.SetWorkorderDialogOpen,
        open: true
      },
      matDetails.loadWorkorders(mat)
    ],
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog)
  }
)(WashDialog);

interface WashProps {
  readonly recent_completed: ReadonlyArray<MaterialSummaryAndCompletedData>;
  readonly openMat: (mat: MaterialSummary) => void;
}

function Wash(props: WashProps) {
  const unwashed = LazySeq.ofIterable(props.recent_completed).filter(m => m.wash_completed === undefined);
  const washed = LazySeq.ofIterable(props.recent_completed).filter(m => m.wash_completed !== undefined);

  return (
    <DocumentTitle title="Wash - FMS Insight">
      <main data-testid="stationmonitor-wash" style={{ padding: "8px" }}>
        <Grid container spacing={16}>
          <Grid item xs={12} md={6}>
            <WhiteboardRegion label="Recently completed parts not yet washed" borderRight borderBottom>
              {unwashed.map((m, idx) => (
                <MatSummary key={idx} mat={m} onOpen={props.openMat} />
              ))}
            </WhiteboardRegion>
          </Grid>
          <Grid item xs={12} md={6}>
            <WhiteboardRegion label="Recently Washed Parts" borderLeft borderBottom>
              {washed.map((m, idx) => (
                <MatSummary key={idx} mat={m} onOpen={props.openMat} />
              ))}
            </WhiteboardRegion>
          </Grid>
        </Grid>
        <SelectWorkorderDialog />
        <ConnectedWashDialog />
      </main>
    </DocumentTitle>
  );
}

const extractRecentCompleted = createSelector(
  (st: Store) => st.Events.last30.mat_summary.matsById,
  (mats: HashMap<number, MaterialSummaryAndCompletedData>): ReadonlyArray<MaterialSummaryAndCompletedData> => {
    const cutoff = addHours(new Date(), -36);
    const recent = LazySeq.ofIterable(mats.valueIterable())
      .filter(e => e.completed_time !== undefined && e.completed_time >= cutoff)
      .toArray();
    // sort decending
    recent.sort((e1, e2) =>
      e1.completed_time && e2.completed_time ? e2.completed_time.getTime() - e1.completed_time.getTime() : 0
    );
    return recent;
  }
);

export default connect(
  (st: Store) => ({
    recent_completed: extractRecentCompleted(st)
  }),
  {
    openMat: matDetails.openMaterialDialog
  }
)(Wash);
