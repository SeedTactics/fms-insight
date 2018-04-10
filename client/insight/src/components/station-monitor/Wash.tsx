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
import ListIcon from '@material-ui/icons/List';
import ToysIcon from '@material-ui/icons/Toys';

import { MaterialSummary } from '../../data/events';
import { Store, connect, AppActionBeforeMiddleware } from '../../data/store';
import { MatSummary, MaterialDetailTitle, MaterialDetailContent } from './Material';
import * as matDetails from '../../data/material-details';
import * as guiState from '../../data/gui-state';
import { StationMonitorType } from '../../data/routes';
import SelectWorkorderDialog from './SelectWorkorder';

export interface WashListProps {
  readonly recent_completed: ReadonlyArray<MaterialSummary>;
  readonly openMat: (mat: MaterialSummary) => void;
}

export class WashList extends React.PureComponent<WashListProps> {
  render () {
    return (
      <ul style={{listStyle: 'none'}}>
        {
          this.props.recent_completed.map((mat, i) =>
            <li key={i} style={{paddingTop: 6, paddingBottom: 6}}>
              <div style={{display: 'inline-block'}}>
                <MatSummary
                  mat={mat}
                  checkWashCompleted
                  onOpen={this.props.openMat}
                />
              </div>
            </li>
          )
        }
      </ul>
    );
  }
}

export class SelectedMaterial extends React.PureComponent<{mat: matDetails.MaterialDetail}> {
  render() {
    const mat = this.props.mat;
    return (
      <>
        <MaterialDetailTitle partName={mat.partName} serial={mat.serial}/>
        <MaterialDetailContent mat={mat}/>
      </>
    );
  }
}

export interface WashProps extends WashListProps {
  readonly fillViewPort: boolean;
  readonly display_material: matDetails.MaterialDetail | null;
  readonly completeWash: (mat: matDetails.MaterialDetail) => void;
  readonly openSelectWorkorder: (mat: matDetails.MaterialDetail) => void;
}

const inspStyles = withStyles(() => ({
  mainFillViewport: {
    'height': 'calc(100vh - 64px - 2.5em)',
    'padding': '8px',
    'width': '100%',
    'display': 'flex',
    'flex-direction': 'column' as 'column',
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
  stretchCardContentContainer: {
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

  return (
    <main className={props.fillViewPort ? props.classes.mainFillViewport : props.classes.mainScrollable}>
      <Grid container style={{flexGrow: 1}} spacing={16}>
        <Grid item xs={12} md={6}>
          <Card className={props.fillViewPort ? props.classes.stretchCard : undefined}>
            <CardHeader
              title={
                <div style={{display: 'flex', alignItems: 'center'}}>
                  <ListIcon style={{marginRight: '0.75em'}}/>
                  <span>Recently Completed Parts</span>
                </div>}
            />
            <CardContent className={props.fillViewPort ? props.classes.stretchCardContent : undefined}>
              <div className={props.fillViewPort ? props.classes.stretchCardContentContainer : undefined}>
                <WashList
                  recent_completed={props.recent_completed}
                  openMat={props.openMat}
                />
              </div>
            </CardContent>
          </Card>
        </Grid>
        <Grid item xs={12} md={6}>
          <Card className={props.fillViewPort ? props.classes.stretchCard : undefined}>
            <CardHeader
              title={
                <div style={{display: 'flex', alignItems: 'center'}}>
                  <ToysIcon style={{marginRight: '0.75em'}}/>
                  <span>Selected Material</span>
                </div>}
            />
            <CardContent className={props.fillViewPort ? props.classes.stretchCardContent : undefined}>
              <div className={props.fillViewPort ? props.classes.stretchCardContentContainer : undefined}>
                {props.display_material ? <SelectedMaterial mat={props.display_material}/> : undefined}
              </div>
            </CardContent>
            {
              props.display_material ?
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
                </CardActions>
                : undefined
            }
          </Card>
        </Grid>
      </Grid>
      <SelectWorkorderDialog station={StationMonitorType.Wash}/>
    </main>
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
    ] as AppActionBeforeMiddleware
  }
)(Wash);