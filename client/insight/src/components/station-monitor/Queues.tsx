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
import { withStyles } from 'material-ui';
import * as im from 'immutable';
import { createSelector } from 'reselect';
import DocumentTitle from 'react-document-title';

import { LoadStationAndQueueData, selectLoadStationAndQueueProps } from '../../data/load-station';
import { MaterialDialog, InProcMaterial, WhiteboardRegion } from './Material';
import * as api from '../../data/api';
import * as routes from '../../data/routes';
import { Store, connect, mkAC } from '../../store/store';
import * as matDetails from '../../data/material-details';
import { MaterialSummary } from '../../data/events';

const ConnectedMaterialDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
  }
)(MaterialDialog);

const queueStyles = withStyles(() => ({
  mainScrollable: {
    'display': 'flex',
    'padding': '8px',
    'width': '100%',
  },
  queueCol: {
    'width': '16em',
    'padding': '8px',
    'display': 'flex',
    'flexDirection': 'column' as 'column',
    'borderLeft': '1px solid rgba(0, 0, 0, 0.12)',
  },
}));

export interface QueueProps {
  readonly data: LoadStationAndQueueData;
  openMat: (m: Readonly<MaterialSummary>) => void;
}

export const Queues = queueStyles<QueueProps>(props => {

  let queues = props.data.queues
    .toSeq()
    .sortBy((mats, q) => q)
    .map((mats, q) => ({
      label: q,
      material: mats,
    }))
    .valueSeq();

  let cells = queues;
  if (props.data.free) {
    cells = im.Seq([{
      label: "In Process Material",
      material: props.data.free,
    }]).concat(queues);
  }

  return (
    <DocumentTitle title="Material Queues - FMS Insight">
      <main className={props.classes.mainScrollable}>
        {
          cells.map((mat, idx) => (
            <div key={idx} className={props.classes.queueCol}>
              <WhiteboardRegion label={mat.label}>
                {
                  mat.material.map((m, matIdx) =>
                    <InProcMaterial key={matIdx} mat={m} onOpen={props.openMat}/>
                  )
                }
              </WhiteboardRegion>
            </div>
          ))
        }
        <ConnectedMaterialDialog/>
      </main>
    </DocumentTitle>
  );
});

const buildQueueData = createSelector(
  (st: Store) => st.Current.current_status,
  (st: Store) => st.Route,
  (curStatus: Readonly<api.ICurrentStatus>, route: routes.State): LoadStationAndQueueData => {
    return selectLoadStationAndQueueProps(
        -1,
        route.standalone_queues,
        route.standalone_free_material,
        curStatus);
  }
);

export default connect(
  (st: Store) => ({
    data: buildQueueData(st)
  }),
  {
    openMat: matDetails.openMaterialDialog,
  }
)(Queues);