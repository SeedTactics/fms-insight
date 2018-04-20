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

import * as React from 'react';
import * as im from 'immutable';
import { addHours } from 'date-fns';
import Grid from 'material-ui/Grid';
import Button from 'material-ui/Button';
import { createSelector } from 'reselect';
import DocumentTitle from 'react-document-title';

import { MaterialSummary } from '../../data/events';
import { Store, connect, AppActionBeforeMiddleware, mkAC } from '../../data/store';
import { MaterialDialog, WhiteboardRegion, MatSummary, MaterialDialogProps } from './Material';
import * as matDetails from '../../data/material-details';
import * as guiState from '../../data/gui-state';
import { StationMonitorType } from '../../data/routes';
import SelectWorkorderDialog from './SelectWorkorder';
import { MaterialSummaryAndCompletedData } from '../../data/events.matsummary';

export interface WashDialogProps extends MaterialDialogProps {
  readonly completeWash: (mat: matDetails.MaterialDetail) => void;
  readonly openSelectWorkorder: (mat: matDetails.MaterialDetail) => void;
}

export function WashDialog(props: WashDialogProps) {
  function markWashComplete() {
    if (!props.display_material) {
      return;
    }

    props.completeWash(props.display_material);
  }
  function openAssignWorkorder() {
    if (!props.display_material) {
      return;
    }
    props.openSelectWorkorder(props.display_material);
  }
  return (
    <MaterialDialog
      display_material={props.display_material}
      onClose={props.onClose}
      buttons={
        <>
          <Button color="primary" onClick={markWashComplete}>
            Mark Wash Complete
          </Button>
          <Button color="primary" onClick={openAssignWorkorder}>
            {
              props.display_material && props.display_material.workorderId ?
                "Change Workorder"
                : "Assign Workorder"
            }
          </Button>
        </>
      }
    />
  );
}

const ConnectedWashDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
    completeWash: (mat: matDetails.MaterialDetail) => [
      matDetails.completeWash(mat),
      {type: matDetails.ActionType.CloseMaterialDialog},
    ],
    openSelectWorkorder: (mat: matDetails.MaterialDetail) => [
      {
        type: guiState.ActionType.SetWorkorderDialogOpen,
        open: true
      },
      matDetails.loadWorkorders(mat, StationMonitorType.Wash),
    ] as AppActionBeforeMiddleware,
  }
)(WashDialog);

export interface WashProps {
  readonly recent_completed: ReadonlyArray<MaterialSummaryAndCompletedData>;
  readonly openMat: (mat: MaterialSummary) => void;
}

export function Wash(props: WashProps) {
  const unwashed = im.Seq(props.recent_completed).filter(m => m.wash_completed === undefined);
  const washed = im.Seq(props.recent_completed).filter(m => m.wash_completed !== undefined);

  return (
    <DocumentTitle title="Wash - FMS Insight">
      <main style={{padding: '8px'}}>
        <Grid container spacing={16}>
          <Grid item xs={12} md={6}>
            <WhiteboardRegion label="Recently completed parts not yet washed" borderRight borderBottom>
              { unwashed.map((m, idx) =>
                <MatSummary key={idx} mat={m} onOpen={props.openMat}/>)
              }
            </WhiteboardRegion>
          </Grid>
          <Grid item xs={12} md={6}>
            <WhiteboardRegion label="Recently Washed Parts" borderLeft borderBottom>
              { washed.map((m, idx) =>
                <MatSummary key={idx} mat={m} onOpen={props.openMat}/>)
              }
            </WhiteboardRegion>
          </Grid>
        </Grid>
        <SelectWorkorderDialog/>
        <ConnectedWashDialog/>
      </main>
    </DocumentTitle>
  );
}

export const extractRecentCompleted = createSelector(
  (st: Store) => st.Events.last30.mat_summary.matsById,
  (mats: im.Map<number, MaterialSummaryAndCompletedData>): ReadonlyArray<MaterialSummaryAndCompletedData> => {
    const cutoff = addHours(new Date(), -36);
    return mats
      .valueSeq()
      .filter(e => e.completed_time !== undefined && e.completed_time >= cutoff)
      .sortBy(e => e.completed_time)
      .reverse()
      .toArray();
  }
);

export default connect(
  (st: Store) => ({
    recent_completed: extractRecentCompleted(st),
  }),
  {
    openMat: matDetails.openMaterialDialog,
  }
)(Wash);