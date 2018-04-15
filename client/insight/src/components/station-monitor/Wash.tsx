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
import { withStyles } from 'material-ui';
import { addHours } from 'date-fns';
import Grid from 'material-ui/Grid';
import Card, { CardContent, CardHeader, CardActions } from 'material-ui/Card';
import Button from 'material-ui/Button';
import { createSelector } from 'reselect';
import DocumentTitle from 'react-document-title';

import { MaterialSummary } from '../../data/events';
import { Store, connect, AppActionBeforeMiddleware } from '../../data/store';
import { MaterialDetailTitle, MaterialDetailContent } from './Material';
import * as matDetails from '../../data/material-details';
import * as guiState from '../../data/gui-state';
import { StationMonitorType } from '../../data/routes';
import SelectWorkorderDialog from './SelectWorkorder';
import { MaterialSummaryDisplay } from './Queues';

export interface WashProps {
  readonly recent_completed: ReadonlyArray<MaterialSummary>;
  readonly openMat: (mat: MaterialSummary) => void;
  readonly fillViewPort: boolean;
  readonly display_material: matDetails.MaterialDetail | null;
  readonly completeWash: (mat: matDetails.MaterialDetail) => void;
  readonly openSelectWorkorder: (mat: matDetails.MaterialDetail) => void;
  readonly clearSelected: () => void;
}

const inspStyles = withStyles(() => ({
  mainFillViewport: {
    'height': 'calc(100vh - 64px - 2.5em)',
    'padding': '8px',
    'width': '100%',
    'display': 'flex',
    'flex-direction': 'column' as 'column',
  },
  stretchList: {
    'height': '100%',
    'display': 'flex',
    'flex-direction': 'column' as 'column',
    'borderRight': '1px solid rgba(0,0,0,0.12)',
    'position': 'relative' as 'relative',
  },
  stretchCard: {
    'height': '100%',
    'display': 'flex',
    'flex-direction': 'column' as 'column',
  },
  stretchCardContent: {
    'flexGrow': 1,
    'position': 'relative' as 'relative',
  },
  stretchContentContainer: {
    'position': 'absolute' as 'absolute',
    'top': 0,
    'left': 0,
    'right': 0,
    'bottom': 0,
    'overflow-y': 'auto',
  },
  mainScrollable: {
    'padding': '8px',
    'width': '100%',
  },
}));

export const Wash = inspStyles<WashProps>(props => {

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

  let selectedMat: JSX.Element | undefined;

  if (props.display_material) {
    selectedMat = (
      <Card className={props.fillViewPort ? props.classes.stretchCard : undefined}>
        <CardHeader
          title={
            <MaterialDetailTitle
              partName={props.display_material.partName}
              serial={props.display_material.serial}
            />}
        />
        <CardContent className={props.fillViewPort ? props.classes.stretchCardContent : undefined}>
          <div className={props.fillViewPort ? props.classes.stretchContentContainer : undefined}>
            <MaterialDetailContent mat={props.display_material}/>
          </div>
        </CardContent>
          <CardActions>
            <Button color="primary" onClick={markWashComplete}>
              Mark Wash Complete
            </Button>
            <Button color="primary" onClick={openAssignWorkorder}>
              {
                props.display_material.workorderId ?
                  "Change Workorder"
                  : "Assign Workorder"
              }
            </Button>
            <Button color="secondary" onClick={props.clearSelected}>
              Clear
            </Button>
          </CardActions>
      </Card>
    );
  }

  return (
    <DocumentTitle title="Wash - FMS Insight">
    <main className={props.fillViewPort ? props.classes.mainFillViewport : props.classes.mainScrollable}>
      <Grid container style={{flexGrow: 1}} spacing={16}>
        <Grid item xs={12} md={6}>
          <div className={props.fillViewPort ? props.classes.stretchList : undefined}>
            <div className={props.fillViewPort ? props.classes.stretchContentContainer : undefined}>
              <MaterialSummaryDisplay
                label="Recently Completed Parts"
                checkWashCompleted
                material={props.recent_completed}
                openMat={props.openMat}
              />
            </div>
          </div>
        </Grid>
        <Grid item xs={12} md={6}>
          {selectedMat}
        </Grid>
      </Grid>
      <SelectWorkorderDialog station={StationMonitorType.Wash}/>
    </main>
    </DocumentTitle>
  );
});

export const extractRecentCompleted = createSelector(
  (st: Store) => st.Events.last30.mat_summary.matsById,
  (mats: im.Map<number, MaterialSummary>): ReadonlyArray<MaterialSummary> => {
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
    display_material: st.MaterialDetails.material[StationMonitorType.Wash],
  }),
  {
    openMat: matDetails.openWashMaterial,
    completeWash: matDetails.completeWash,
    openSelectWorkorder: (mat: matDetails.MaterialDetail) => [
      {
        type: guiState.ActionType.SetWorkorderDialogOpen,
        open: true
      },
      matDetails.loadWorkorders(mat, StationMonitorType.Wash),
    ] as AppActionBeforeMiddleware,
    clearSelected: () => ({
      type: matDetails.ActionType.CloseMaterialDialog,
      station: StationMonitorType.Wash,
    }) as AppActionBeforeMiddleware,
  }
)(Wash);