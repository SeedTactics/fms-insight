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
import { WithStyles, createStyles, withStyles } from "@material-ui/core/styles";
import * as im from "immutable";
import { createSelector } from "reselect";
import Button from "@material-ui/core/Button";
import List from "@material-ui/core/List";
import ListItemText from "@material-ui/core/ListItemText";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import MenuItem from "@material-ui/core/MenuItem";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogTitle from "@material-ui/core/DialogTitle";
import TextField from "@material-ui/core/TextField";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58
import { SortEnd } from "react-sortable-hoc";

import { LoadStationAndQueueData, selectLoadStationAndQueueProps } from "../../data/load-station";
import {
  SortableInProcMaterial,
  SortableWhiteboardRegion,
  MaterialDetailTitle,
  MaterialDetailContent,
  PartIdenticon
} from "./Material";
import * as api from "../../data/api";
import * as routes from "../../data/routes";
import * as guiState from "../../data/gui-state";
import * as currentSt from "../../data/current-status";
import { Store, connect, AppActionBeforeMiddleware } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { MaterialSummary } from "../../data/events";
import SerialScanner from "./QRScan";
import ManualScan from "./ManualScan";

interface ExistingMatInQueueDialogBodyProps {
  readonly display_material: matDetails.MaterialDetail;
  readonly onClose: () => void;
  readonly removeFromQueue: (mat: matDetails.MaterialDetail) => void;
}

function ExistingMatInQueueDialogBody(props: ExistingMatInQueueDialogBodyProps) {
  return (
    <>
      <DialogTitle disableTypography>
        <MaterialDetailTitle partName={props.display_material.partName} serial={props.display_material.serial} />
      </DialogTitle>
      <DialogContent>
        <MaterialDetailContent mat={props.display_material} />
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
          {this.props.queue_name === undefined && this.props.queues.length > 1 ? (
            <div style={{ marginBottom: "1em" }}>
              <p>Select a queue.</p>
              <List>
                {this.props.queues.map((q, idx) => (
                  <MenuItem
                    key={idx}
                    selected={q === this.state.selected_queue}
                    onClick={() => this.setState({ selected_queue: q })}
                  >
                    {q}
                  </MenuItem>
                ))}
              </List>
            </div>
          ) : (
            undefined
          )}
          <MaterialDetailContent mat={this.props.display_material} />
        </DialogContent>
        <DialogActions>
          <Button
            color="primary"
            disabled={this.props.display_material.loading_events || queue === undefined}
            onClick={() =>
              this.props.addMat({
                materialId: this.props.display_material.materialID,
                queue: queue || "",
                queuePosition: 0
              })
            }
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
  readonly jobs: { [key: string]: Readonly<api.IInProcessJob> };
  readonly selected_job?: Readonly<api.IInProcessJob>;
  readonly selected_last_process?: number;
  readonly onSelectJob: (j: Readonly<api.IInProcessJob>) => void;
  readonly onSelectProcess: (p?: number) => void;
}

class SelectJob extends React.PureComponent<SelectJobProps> {
  render() {
    const jobs = im
      .Map(this.props.jobs)
      .valueSeq()
      .sortBy(j => j.partName);
    return (
      <div style={{ display: "flex" }}>
        <div>
          <p>Select a job.</p>
          <List>
            {jobs.map((j, idx) => (
              <MenuItem
                key={idx}
                selected={this.props.selected_job && j.unique === this.props.selected_job.unique}
                onClick={() => this.props.onSelectJob(j)}
              >
                <ListItemIcon>
                  <PartIdenticon part={j.partName} />
                </ListItemIcon>
                <ListItemText primary={j.partName} secondary={j.unique} />
              </MenuItem>
            ))}
          </List>
        </div>
        <div>
          {this.props.selected_job === undefined || this.props.selected_job.procsAndPaths.length === 1 ? (
            undefined
          ) : (
            <div style={{ marginLeft: "1em" }}>
              <p style={{ margin: "1em", maxWidth: "15em" }}>
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
                {im.Range(1, this.props.selected_job.procsAndPaths.length).map(p => (
                  <MenuItem
                    key={p}
                    selected={this.props.selected_last_process === p}
                    onClick={() => this.props.onSelectProcess(p)}
                  >
                    Last completed process {p}
                  </MenuItem>
                ))}
              </List>
            </div>
          )}
        </div>
      </div>
    );
  }
}

const ConnectedSelectJob = connect(s => ({
  jobs: s.Current.current_status.jobs as {
    [key: string]: Readonly<api.IInProcessJob>;
  }
}))(SelectJob);

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
    this.setState({ selected_job: j });
  };

  onSelectProcess = (p?: number) => {
    this.setState({ selected_last_proc: p });
  };

  addMaterial = (queue?: string) => {
    if (this.state.selected_job === undefined) {
      return;
    }
    if (queue === undefined) {
      return;
    }

    this.props.addMat({
      jobUnique: this.state.selected_job.unique,
      lastCompletedProcess: this.state.selected_last_proc,
      queue: queue,
      queuePosition: 0,
      serial: this.props.not_found_serial
    });

    this.setState({
      selected_job: undefined,
      selected_last_proc: undefined,
      selected_queue: undefined
    });
  };

  render() {
    let queue = this.props.queue_name || this.state.selected_queue;
    if (queue === undefined && this.props.queues.length === 1) {
      queue = this.props.queues[0];
    }
    return (
      <>
        <DialogTitle>{this.props.not_found_serial ? this.props.not_found_serial : "Add New Material"}</DialogTitle>
        <DialogContent>
          {this.props.not_found_serial ? (
            <p>
              The serial {this.props.not_found_serial} was not found. Specify the job and process to add to the queue.
            </p>
          ) : (
            undefined
          )}
          <div style={{ display: "flex" }}>
            {this.props.queue_name === undefined && this.props.queues.length > 1 ? (
              <div style={{ marginRight: "1em" }}>
                <p>Select a queue.</p>
                <List>
                  {this.props.queues.map((q, idx) => (
                    <MenuItem
                      key={idx}
                      selected={q === this.state.selected_queue}
                      onClick={() => this.setState({ selected_queue: q })}
                    >
                      {q}
                    </MenuItem>
                  ))}
                </List>
              </div>
            ) : (
              undefined
            )}
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

interface QueueMatDialogProps {
  readonly display_material: matDetails.MaterialDetail | null;
  readonly material_currently_in_queue: boolean;
  readonly addMatQueue?: string;
  readonly queueNames: ReadonlyArray<string>;

  readonly onClose: () => void;
  readonly removeFromQueue: (mat: matDetails.MaterialDetail) => void;
  readonly addNewMat: (d: matDetails.AddNewMaterialToQueueData) => void;
  readonly addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => void;
}

function QueueMatDialog(props: QueueMatDialogProps) {
  let body: JSX.Element | undefined;

  if (props.display_material === null) {
    body = <p>None</p>;
  } else {
    if (props.material_currently_in_queue) {
      body = (
        <ExistingMatInQueueDialogBody
          display_material={props.display_material}
          onClose={props.onClose}
          removeFromQueue={props.removeFromQueue}
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
    <Dialog open={props.display_material !== null} onClose={props.onClose} maxWidth="md">
      {body}
    </Dialog>
  );
}

const selectMatCurrentlyInQueue = createSelector(
  (st: Store) => st.MaterialDetails.material,
  (st: Store) => st.Current.current_status.material,
  function(mat: matDetails.MaterialDetail | null, allMats: ReadonlyArray<api.IInProcessMaterial>) {
    if (mat === null) {
      return false;
    }
    if (mat.materialID < 0) {
      return false;
    }
    for (let inProcMat of allMats) {
      if (inProcMat.materialID === mat.materialID) {
        return inProcMat.location.type === api.LocType.InQueue;
      }
    }
    return false;
  }
);

const ConnectedMaterialDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material,
    material_currently_in_queue: selectMatCurrentlyInQueue(st),
    addMatQueue: st.Gui.add_mat_to_queue,
    queueNames: st.Route.standalone_queues
  }),
  {
    onClose: () => [
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined }
    ],
    removeFromQueue: (mat: matDetails.MaterialDetail) =>
      [
        matDetails.removeFromQueue(mat),
        { type: matDetails.ActionType.CloseMaterialDialog },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined }
      ] as AppActionBeforeMiddleware,
    addNewMat: (d: matDetails.AddNewMaterialToQueueData) => [
      matDetails.addNewMaterialToQueue(d),
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined }
    ],
    addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => [
      matDetails.addExistingMaterialToQueue(d),
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined }
    ]
  }
)(QueueMatDialog);

interface ChooseSerialOrDirectJobProps {
  readonly dialog_open: boolean;
  readonly lookupSerial: (s: string) => void;
  readonly selectJobWithoutSerial: () => void;
  readonly onClose: () => void;
}

interface ChooseSerialOrDirectJobState {
  readonly serial?: string;
}

class ChooseSerialOrDirectJob extends React.PureComponent<ChooseSerialOrDirectJobProps, ChooseSerialOrDirectJobState> {
  state = { serial: undefined };

  render() {
    return (
      <Dialog open={this.props.dialog_open} onClose={this.props.onClose} maxWidth="md">
        <DialogTitle>Lookup Material</DialogTitle>
        <DialogContent>
          <div style={{ maxWidth: "25em" }}>
            <p>
              To find the details of the material to add, you can either scan a part's serial, lookup a serial, or
              manually select a job.
            </p>
          </div>
          <div style={{ display: "flex", alignItems: "center" }}>
            <div
              style={{
                borderRight: "1px solid rgba(0,0,0,0.2)",
                paddingRight: "8px",
                display: "flex",
                flexDirection: "column",
                alignItems: "center"
              }}
            >
              <div style={{ marginBottom: "2em" }}>
                <TextField
                  label="Serial"
                  value={this.state.serial || ""}
                  onChange={e => this.setState({ serial: e.target.value })}
                />
              </div>
              <div>
                <Button
                  variant="raised"
                  color="secondary"
                  onClick={() => (this.state.serial ? this.props.lookupSerial(this.state.serial || "") : undefined)}
                >
                  Lookup Serial
                </Button>
              </div>
            </div>
            <div style={{ paddingLeft: "8px" }}>
              <Button variant="contained" color="secondary" onClick={this.props.selectJobWithoutSerial}>
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
      </Dialog>
    );
  }
}

const ConnectedChooseSerialOrDirectJobDialog = connect(
  st => ({
    dialog_open: st.Gui.queue_dialog_mode_open
  }),
  {
    onClose: () =>
      [
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: false
        },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined }
      ] as AppActionBeforeMiddleware,
    lookupSerial: (serial: string) =>
      [
        ...matDetails.openMaterialBySerial(serial, false),
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: false
        }
      ] as AppActionBeforeMiddleware,
    selectJobWithoutSerial: () =>
      [
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: false
        },
        matDetails.openMaterialDialogWithEmptyMat()
      ] as AppActionBeforeMiddleware
  }
)(ChooseSerialOrDirectJob);

const queueStyles = createStyles({
  mainScrollable: {
    padding: "8px",
    width: "100%"
  }
});

interface QueueProps {
  readonly data: LoadStationAndQueueData;
  openMat: (m: Readonly<MaterialSummary>) => void;
  openAddToQueue: (queueName: string) => void;
  moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => void;
}

const Queues = withStyles(queueStyles)((props: QueueProps & WithStyles<typeof queueStyles>) => {
  let queues = props.data.queues
    .toVector()
    .sortOn(([q, mats]) => q)
    .map(([q, mats]) => ({
      label: q,
      free: false,
      material: mats
    }));

  let cells = queues;
  if (props.data.free) {
    cells = queues.prependAll([
      {
        label: "Raw Material",
        free: true,
        material: props.data.castings
      },
      {
        label: "In Process Material",
        free: true,
        material: props.data.free
      }
    ]);
  }

  return (
    <DocumentTitle title="Material Queues - FMS Insight">
      <main data-testid="stationmonitor-queues" className={props.classes.mainScrollable}>
        {cells.zipWithIndex().map(([region, idx]) => (
          <SortableWhiteboardRegion
            key={idx}
            axis="xy"
            label={region.label}
            borderBottom
            flexStart
            onAddMaterial={region.free ? undefined : () => props.openAddToQueue(region.label)}
            distance={5}
            shouldCancelStart={() => false}
            onSortEnd={(se: SortEnd) =>
              props.moveMaterialInQueue({
                materialId: region.material[se.oldIndex].materialID,
                queue: region.label,
                queuePosition: se.newIndex
              })
            }
          >
            {region.material.map((m, matIdx) => (
              <SortableInProcMaterial key={matIdx} index={matIdx} mat={m} onOpen={props.openMat} />
            ))}
          </SortableWhiteboardRegion>
        ))}
        <ConnectedMaterialDialog />
        <ConnectedChooseSerialOrDirectJobDialog />
        <SerialScanner />
        <ManualScan />
      </main>
    </DocumentTitle>
  );
});

const buildQueueData = createSelector(
  (st: Store) => st.Current.current_status,
  (st: Store) => st.Route,
  (curStatus: Readonly<api.ICurrentStatus>, route: routes.State): LoadStationAndQueueData => {
    return selectLoadStationAndQueueProps(-1, route.standalone_queues, route.standalone_free_material, curStatus);
  }
);

export default connect(
  (st: Store) => ({
    data: buildQueueData(st)
  }),
  {
    openAddToQueue: (queueName: string) =>
      [
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: true
        },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: queueName }
      ] as AppActionBeforeMiddleware,
    openMat: matDetails.openMaterialDialog,
    moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => [
      {
        type: currentSt.ActionType.ReorderQueuedMaterial,
        queue: d.queue,
        materialId: d.materialId,
        newIdx: d.queuePosition
      },
      matDetails.addExistingMaterialToQueue(d)
    ]
  }
)(Queues);
