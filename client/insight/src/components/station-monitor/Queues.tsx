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
import Button from 'material-ui/Button';
import Dialog, {
  DialogActions,
  DialogContent,
  DialogTitle,
} from 'material-ui/Dialog';

import { MaterialList, LoadStationAndQueueData, selectLoadStationAndQueueProps } from '../../data/load-station';
import { InProcMaterial, MaterialDetailTitle, MaterialDetailContent } from './Material';
import * as api from '../../data/api';
import * as routes from '../../data/routes';
import { Store, connect, mkAC, DispatchAction } from '../../data/store';
import * as matDetails from '../../data/material-details';

const materialStyle = withStyles(() => ({
  container: {
    width: '100%',
    minHeight: '70px',
    position: 'relative' as 'relative',
  },
  labelContainer: {
    position: 'absolute' as 'absolute',
    top: '4px',
    left: '4px',
  },
  label: {
    color: 'rgba(0,0,0,0.5)',
    fontSize: 'small',
  },
  material: {
    marginTop: '8px',
    marginBottom: '8px',
    display: 'flex' as 'flex',
    flexWrap: 'wrap' as 'wrap',
    justifyContent: 'space-around' as 'space-around',
  }
}));

export interface MaterialDisplayProps {
  readonly material: MaterialList;
  readonly label: string;
  openMat: (m: Readonly<api.IInProcessMaterial>) => void;
}

const MaterialDisplayWithStyles = materialStyle<MaterialDisplayProps>(props => {
  return (
    <div className={props.classes.container}>
      <div className={props.classes.labelContainer}>
        <span className={props.classes.label}>
          {props.label}
        </span>
      </div>
      {
        props.material.length === 0 ? undefined :
          <div className={props.classes.material}>
            {
              props.material.map((m, idx) => (
                <InProcMaterial key={idx} mat={m} onOpen={props.openMat}/>
              ))
            }
          </div>
      }
    </div>
  );
});

// decorate doesn't work well with classes yet.
// https://github.com/Microsoft/TypeScript/issues/4881
export class MaterialDisplay extends React.PureComponent<MaterialDisplayProps> {
  render() {
    return <MaterialDisplayWithStyles {...this.props}/>;
  }
}

export interface MaterialDialogProps {
  display_material: matDetails.MaterialDetail | null;
  onClose: DispatchAction<matDetails.ActionType.CloseMaterialDialog>;
}

export function MaterialDialog(props: MaterialDialogProps) {
  const onClose = () => props.onClose({station: routes.StationMonitorType.LoadUnload});
  let body: JSX.Element | undefined;
  if (props.display_material === null) {
    body = <p>None</p>;
  } else {
    const mat = props.display_material;
    body = (
      <>
        <DialogTitle disableTypography>
          <MaterialDetailTitle partName={mat.partName} serial={mat.serial}/>
        </DialogTitle>
        <DialogContent>
          <MaterialDetailContent mat={mat}/>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose} color="primary">
            Close
          </Button>
        </DialogActions>
      </>
    );
  }
  return (
    <Dialog
      open={props.display_material !== null}
      onClose={onClose}
      maxWidth="md"
    >
      {body}
    </Dialog>

  );
}

export const ConnectedMaterialDialog = connect(
  (st: Store) => ({
    display_material: st.MaterialDetails.material[routes.StationMonitorType.LoadUnload]
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
  }
)(MaterialDialog);

const queueStyles = withStyles(() => ({
  mainFillViewport: {
    'height': 'calc(100vh - 64px - 2.5em)',
    'display': 'flex',
    'padding': '8px',
    'width': '100%',
  },
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
  readonly fillViewPort: boolean;
  readonly data: LoadStationAndQueueData;
  openMat: (m: Readonly<api.IInProcessMaterial>) => void;
}

export const Queues = queueStyles<QueueProps>(props => {

  let queues = props.data.queues
    .toSeq()
    .sortBy((mats, q) => q)
    .map((mats, q) => ({
      label: q,
      material: mats,
      openMat: props.openMat
    }))
    .valueSeq();

  let cells: im.Seq.Indexed<MaterialDisplayProps> = queues;
  if (props.data.free) {
    cells = im.Seq([{
      label: "In Process Material",
      material: props.data.free,
      openMat: props.openMat,
    }]).concat(queues);
  }

  return (
    <main className={props.fillViewPort ? props.classes.mainFillViewport : props.classes.mainScrollable}>
      {
        cells.map((mat, idx) => (
          <div key={idx} className={props.classes.queueCol}>
            <MaterialDisplay {...mat}/>
          </div>
        ))
      }
      <ConnectedMaterialDialog/>
    </main>
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
    openMat: matDetails.openLoadunloadMaterialDialog,
  }
)(Queues);