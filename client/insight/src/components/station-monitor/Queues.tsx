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
import withStyles from '@material-ui/core/styles/withStyles';
import * as im from 'immutable';
import { createSelector } from 'reselect';
import DocumentTitle from 'react-document-title';
import Button from '@material-ui/core/Button';
import List from '@material-ui/core/List';
import ListItemText from '@material-ui/core/ListItemText';
import ListItemIcon from '@material-ui/core/ListItemIcon';
import MenuItem from '@material-ui/core/MenuItem';
import Dialog from '@material-ui/core/Dialog';
import DialogActions from '@material-ui/core/DialogActions';
import DialogContent from '@material-ui/core/DialogContent';
import DialogTitle from '@material-ui/core/DialogTitle';
import TextField from '@material-ui/core/TextField';

import { LoadStationAndQueueData, selectLoadStationAndQueueProps } from '../../data/load-station';
import { InProcMaterial, WhiteboardRegion, MaterialDetailTitle, MaterialDetailContent, PartIdenticon }
   from './Material';
import * as api from '../../data/api';
import * as routes from '../../data/routes';
import * as guiState from '../../data/gui-state';
import { Store, connect, mkAC, AppActionBeforeMiddleware, DispatchAction } from '../../store/store';
import * as matDetails from '../../data/material-details';
import { MaterialSummary } from '../../data/events';
import SerialScanner from './QRScan';

interface ExistingMatInQueueDialogBodyProps {
  readonly display_material: matDetails.MaterialDetail;
  readonly onClose: () => void;
  readonly removeFromQueue: (mat: matDetails.MaterialDetail) => void;
}

function ExistingMatInQueueDialogBody(props: ExistingMatInQueueDialogBodyProps) {
  return (
    <>
      <DialogTitle disableTypography>
        <MaterialDetailTitle partName={props.display_material.partName} serial={props.display_material.serial}/>
      </DialogTitle>
      <DialogContent>
        <MaterialDetailContent mat={props.display_material}/>
      </DialogContent>
      <DialogActions>
        <Button color="primary" onClick={() => props.removeFromQueue(props.display_material)}>
          Remove From Queue
        </Button>
        <Button onClick={props.onClose} color="primary">
          Close
        </Button>
      </DialogActions>
    </>
  );
}

interface AddSerialFoundProps {
  readonly queues: ReadonlyArray<string>;
  readonly queue_name?: string;
  readonly display_material: matDetails.MaterialDetail;
  readonly addMat: (d: matDetails.AddExistingMaterialToQueueData) => void;
  readonly onClose: () => void;
}

interface AddSerialFoundState {
  readonly selected_queue?: string;
}

class AddSerialFound extends React.PureComponent<AddSerialFoundProps, AddSerialFoundState> {
  state: AddSerialFoundState = {};

  render() {
    let queue = this.props.queue_name || this.state.selected_queue;
    if (queue === undefined && this.props.queues.length === 1) {
      queue = this.props.queues[0];
    }
    return (
      <>
        <DialogTitle disableTypography>
          <MaterialDetailTitle
            partName={this.props.display_material.partName}
            serial={this.props.display_material.serial}
          />
        </DialogTitle>
        <DialogContent>
          {this.props.queue_name === undefined && this.props.queues.length > 1 ?
            <div style={{marginBottom: '1em'}}>
              <p>Select a queue.</p>
              <List>
                {this.props.queues.map((q, idx) =>
                  <MenuItem
                    key={idx}
                    selected={q === this.state.selected_queue}
                    onClick={() => this.setState({selected_queue: q})}
                  >
                    {q}
                  </MenuItem>
                )}
              </List>
            </div>
            : undefined
          }
          <MaterialDetailContent mat={this.props.display_material}/>
        </DialogContent>
        <DialogActions>
          <Button
            color="primary"
            disabled={this.props.display_material.loading_events || queue === undefined}
            onClick={() => this.props.addMat({
              materialId: this.props.display_material.materialID,
              queue: queue || "",
              queuePosition: 0
            })}
          >
            Add To {queue}
          </Button>
          <Button onClick={this.props.onClose} color="primary">
            Cancel
          </Button>
        </DialogActions>
      </>
    );
  }
}

interface SelectJobProps {
  readonly jobs: {[key: string]: Readonly<api.IInProcessJob>};
  readonly selected_job?: Readonly<api.IInProcessJob>;
  readonly selected_last_process?: number;
  readonly onSelectJob: (j: Readonly<api.IInProcessJob>) => void;
  readonly onSelectProcess: (p?: number) => void;
}

class SelectJob extends React.PureComponent<SelectJobProps> {
  render() {
    const jobs = im.Map(this.props.jobs).valueSeq().sortBy(j => j.partName);
    return (
      <div style={{display: "flex"}}>
        <div>
          <p>Select a job.</p>
          <List>
            {jobs.map((j, idx) =>
              <MenuItem
                key={idx}
                selected={this.props.selected_job && j.unique === this.props.selected_job.unique}
                onClick={() => this.props.onSelectJob(j)}
              >
                <ListItemIcon>
                  <PartIdenticon part={j.partName}/>
                </ListItemIcon>
                <ListItemText
                  primary={j.partName}
                  secondary={j.unique}
                />
              </MenuItem>
            )}
          </List>
        </div>
        <div>
          { this.props.selected_job === undefined || this.props.selected_job.procsAndPaths.length === 1 ? undefined :
            <div style={{marginLeft: '1em'}}>
              <p style={{margin: "1em", maxWidth: "15em"}}>
                Select the last completed process, or "Raw Material" if this part has not yet completed any process.
              </p>
              <List>
                <MenuItem
                  key="rawmaterial"
                  selected={this.props.selected_last_process === undefined}
                  onClick={() => this.props.onSelectProcess()}
                >
                  Raw Material
                </MenuItem>
                {im.Range(1, this.props.selected_job.procsAndPaths.length).map(p =>
                  <MenuItem
                    key={p}
                    selected={this.props.selected_last_process === p}
                    onClick={() => this.props.onSelectProcess(p)}
                  >
                    Last completed process {p}
                  </MenuItem>
                )}
              </List>
            </div>
          }
        </div>
      </div>
    );
  }
}

const ConnectedSelectJob = connect(
  (s) => ({
    jobs: s.Current.current_status.jobs as {[key: string]: Readonly<api.IInProcessJob>}
  })
)(SelectJob);

interface AddNewMaterialProps {
  readonly queues: ReadonlyArray<string>;
  readonly queue_name?: string;
  readonly not_found_serial?: string;
  readonly onClose: () => void;
  readonly addMat: (d: matDetails.AddNewMaterialToQueueData) => void;
}

interface AddNewMaterialState {
  readonly selected_job?: Readonly<api.IInProcessJob>;
  readonly selected_last_proc?: number;
  readonly selected_queue?: string;
}

class AddNewMaterialBody extends React.PureComponent<AddNewMaterialProps, AddNewMaterialState> {
  state: AddNewMaterialState = {};

  onSelectJob = (j: Readonly<api.IInProcessJob>) => {
    this.setState({selected_job: j});
  }

  onSelectProcess = (p?: number) => {
    this.setState({selected_last_proc: p});
  }

  addMaterial = (queue?: string) => {
    if (this.state.selected_job === undefined) { return; }
    if (queue === undefined) { return; }

    this.props.addMat({
      jobUnique: this.state.selected_job.unique,
      lastCompletedProcess: this.state.selected_last_proc,
      queue: queue,
      queuePosition: 0,
      serial: this.props.not_found_serial,
    });

    this.setState({selected_job: undefined, selected_last_proc: undefined, selected_queue: undefined});
  }

  render() {
    let queue = this.props.queue_name || this.state.selected_queue;
    if (queue === undefined && this.props.queues.length === 1) {
      queue = this.props.queues[0];
    }
    return (
      <>
        <DialogTitle>
          {this.props.not_found_serial ? this.props.not_found_serial : "Add New Material"}
        </DialogTitle>
        <DialogContent>
          {this.props.not_found_serial ?
            <p>
              The serial {this.props.not_found_serial} was not found.
              Specify the job and process to add to the queue.
            </p>
            : undefined
          }
          <div style={{display: "flex"}}>
            {this.props.queue_name === undefined && this.props.queues.length > 1 ?
              <div style={{marginRight: '1em'}}>
                <p>Select a queue.</p>
                <List>
                  {this.props.queues.map((q, idx) =>
                    <MenuItem
                      key={idx}
                      selected={q === this.state.selected_queue}
                      onClick={() => this.setState({selected_queue: q})}
                    >
                      {q}
                    </MenuItem>
                  )}
                </List>
              </div>
              : undefined
            }
            <ConnectedSelectJob
              selected_job={this.state.selected_job}
              selected_last_process={this.state.selected_last_proc}
              onSelectJob={this.onSelectJob}
              onSelectProcess={this.onSelectProcess}
            />
          </div>
        </DialogContent>
        <DialogActions>
          <Button
            color="primary"
            onClick={() => this.addMaterial(queue)}
            disabled={this.state.selected_job === undefined || queue === undefined}
          >
            Add To {queue}
          </Button>
          <Button onClick={this.props.onClose} color="primary">
            Cancel
          </Button>
        </DialogActions>
      </>
    );
  }
}

interface ChooseSerialOrDirectJobProps {
  readonly queue_name: string;
  readonly setDialogSt: DispatchAction<guiState.ActionType.SetAddMatToQueueDialog>;
  readonly lookupSerial: (s: string, queue: string) => void;
  readonly onClose: () => void;
}

interface ChooseSerialOrDirectJobState {
  readonly serial?: string;
}

class ChooseSerialOrDirectJob extends React.PureComponent<ChooseSerialOrDirectJobProps, ChooseSerialOrDirectJobState> {
  state = { serial: undefined };

  render() {
    return (
      <>
        <DialogTitle>
          Lookup Material
        </DialogTitle>
        <DialogContent>
          <div style={{maxWidth: '25em'}}>
            <p>
              To find the details of the material to add,  you can either scan a part's serial,
              lookup a serial, or manually select a job.
            </p>
          </div>
          <div style={{display: "flex", alignItems: 'center'}}>
            <div
              style={{
                borderRight: '1px solid rgba(0,0,0,0.2)',
                paddingRight: '8px',
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
              }}
            >
              <div style={{marginBottom: '2em'}}>
                <TextField
                  label="Serial"
                  value={this.state.serial || ""}
                  onChange={e => this.setState({serial: e.target.value})}
                />
              </div>
              <div>
                <Button
                  variant="raised"
                  color="secondary"
                  onClick={() => this.state.serial
                    ? this.props.lookupSerial(this.state.serial || "", this.props.queue_name)
                    : undefined}
                >
                  Lookup Serial
                </Button>
              </div>
            </div>
            <div style={{paddingLeft: '8px'}}>
              <Button
                variant="raised"
                color="secondary"
                onClick={() => this.props.setDialogSt({
                  queue: this.props.queue_name,
                  st: guiState.AddMatToQueueDialogState.DialogOpenToAddMaterial
                })}
              >
                Manually Select Job
              </Button>
            </div>
          </div>
        </DialogContent>
        <DialogActions>
          <Button onClick={this.props.onClose} color="primary">
            Cancel
          </Button>
        </DialogActions>
      </>
    );
  }
}

export interface QueueMatDialogProps {
  readonly display_material: matDetails.MaterialDetail | null;
  readonly addMatState: guiState.AddMatToQueueDialogState;
  readonly addMatQueue?: string;
  readonly queueNames: ReadonlyArray<string>;

  readonly onClose: DispatchAction<matDetails.ActionType.CloseMaterialDialog>;
  readonly removeFromQueue: (mat: matDetails.MaterialDetail) => void;
  readonly addNewMat: (d: matDetails.AddNewMaterialToQueueData) => void;
  readonly addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => void;
  readonly setAddDialogSt: DispatchAction<guiState.ActionType.SetAddMatToQueueDialog>;
  readonly lookupSerial: (s: string) => void;
}

export function QueueMatDialog(props: QueueMatDialogProps) {
  let body: JSX.Element | undefined;

  switch (props.addMatState) {
    case guiState.AddMatToQueueDialogState.DialogClosed:
      // existing material
      if (props.display_material === null) {
        body = <p>None</p>;
      } else {
        body = (
          <ExistingMatInQueueDialogBody
            display_material={props.display_material}
            onClose={props.onClose}
            removeFromQueue={props.removeFromQueue}
          />
        );
      }
      break;

    case guiState.AddMatToQueueDialogState.DialogOpenChooseSerialOrJob:
      body = (
        <ChooseSerialOrDirectJob
          queue_name={props.addMatQueue || ""}
          setDialogSt={props.setAddDialogSt}
          lookupSerial={props.lookupSerial}
          onClose={props.onClose}
        />
      );
      break;

    case guiState.AddMatToQueueDialogState.DialogOpenToAddMaterial:
      if (props.display_material === null) {
        body = (
          <AddNewMaterialBody
            queues={props.queueNames}
            queue_name={props.addMatQueue}
            onClose={props.onClose}
            addMat={props.addNewMat}
          />
        );
      } else if (props.display_material.materialID >= 0 || props.display_material.loading_events) {
        body = (
          <AddSerialFound
            queues={props.queueNames}
            display_material={props.display_material}
            queue_name={props.addMatQueue}
            onClose={props.onClose}
            addMat={props.addExistingMat}
          />
        );
      } else {
        body = (
          <AddNewMaterialBody
            queues={props.queueNames}
            not_found_serial={props.display_material.serial}
            queue_name={props.addMatQueue}
            onClose={props.onClose}
            addMat={props.addNewMat}
          />
        );
      }
  }

  return (
    <Dialog
      open={props.display_material !== null || props.addMatState !== guiState.AddMatToQueueDialogState.DialogClosed}
      onClose={props.onClose}
      maxWidth="md"
    >
      {body}
    </Dialog>

  );
}

const ConnectedMaterialDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material,
    addMatState: st.Gui.add_mat_to_queue_st,
    addMatQueue: st.Gui.add_mat_to_queue,
    queueNames: st.Route.standalone_queues,
  }),
  {
    onClose: () => [
      {type: matDetails.ActionType.CloseMaterialDialog},
      {type: guiState.ActionType.SetAddMatToQueueDialog, st: guiState.AddMatToQueueDialogState.DialogClosed}
    ],
    removeFromQueue: (mat: matDetails.MaterialDetail) => [
      matDetails.removeFromQueue(mat),
      {type: matDetails.ActionType.CloseMaterialDialog},
    ] as AppActionBeforeMiddleware,
    addNewMat: (d: matDetails.AddNewMaterialToQueueData) => [
      matDetails.addNewMaterialToQueue(d),
      {type: matDetails.ActionType.CloseMaterialDialog},
      {type: guiState.ActionType.SetAddMatToQueueDialog, st: guiState.AddMatToQueueDialogState.DialogClosed}
    ],
    addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => [
      matDetails.addExistingMaterialToQueue(d),
      {type: matDetails.ActionType.CloseMaterialDialog},
      {type: guiState.ActionType.SetAddMatToQueueDialog, st: guiState.AddMatToQueueDialogState.DialogClosed}
    ],
    lookupSerial: (serial: string, queue: string) => [
      matDetails.openMaterialBySerial(serial),
      {
        type: guiState.ActionType.SetAddMatToQueueDialog,
        queue,
        st: guiState.AddMatToQueueDialogState.DialogOpenToAddMaterial
      }
    ],
    setAddDialogSt: mkAC(guiState.ActionType.SetAddMatToQueueDialog),
  }
)(QueueMatDialog);

const queueStyles = withStyles(() => ({
  mainScrollable: {
    'padding': '8px',
    'width': '100%',
  },
}));

export interface QueueProps {
  readonly data: LoadStationAndQueueData;
  openMat: (m: Readonly<MaterialSummary>) => void;
  setAddDialogSt: DispatchAction<guiState.ActionType.SetAddMatToQueueDialog>;
}

export const Queues = queueStyles<QueueProps>(props => {

  let queues = props.data.queues
    .toSeq()
    .sortBy((mats, q) => q)
    .map((mats, q) => ({
      label: q,
      free: false,
      material: mats,
    }))
    .valueSeq();

  let cells = queues;
  if (props.data.free) {
    cells = im.Seq([
      {
        label: "Raw Material",
        free: true,
        material: props.data.castings,
      },
      {
        label: "In Process Material",
        free: true,
        material: props.data.free,
      },
    ]).concat(queues);
  }

  return (
    <DocumentTitle title="Material Queues - FMS Insight">
      <main className={props.classes.mainScrollable}>
        {
          cells.map((region, idx) => (
            <WhiteboardRegion
              key={idx}
              label={region.label}
              borderBottom
              flexStart
              onAddMaterial={region.free ? undefined : () => props.setAddDialogSt({
                queue: region.label,
                st: guiState.AddMatToQueueDialogState.DialogOpenChooseSerialOrJob
              })}
            >
              {
                region.material.map((m, matIdx) =>
                  <InProcMaterial key={matIdx} mat={m} onOpen={props.openMat}/>
                )
              }
            </WhiteboardRegion>
          ))
        }
        <ConnectedMaterialDialog/>
        <SerialScanner/>
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
    setAddDialogSt: mkAC(guiState.ActionType.SetAddMatToQueueDialog),
  }
)(Queues);