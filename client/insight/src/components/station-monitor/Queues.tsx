/* Copyright (c) 2020, John Lenz

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
import { createSelector } from "reselect";
import Button from "@material-ui/core/Button";
import List from "@material-ui/core/List";
import ListItem from "@material-ui/core/ListItem";
import ListItemText from "@material-ui/core/ListItemText";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import MenuItem from "@material-ui/core/MenuItem";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogTitle from "@material-ui/core/DialogTitle";
import Collapse from "@material-ui/core/Collapse";
import TextField from "@material-ui/core/TextField";
import { makeStyles } from "@material-ui/core/styles";
// eslint-disable-next-line @typescript-eslint/no-var-requires
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58
import { SortEnd } from "react-sortable-hoc";
import ExpandMoreIcon from "@material-ui/icons/ExpandMore";
import clsx from "clsx";

import { LoadStationAndQueueData, selectLoadStationAndQueueProps } from "../../data/load-station";
import {
  SortableInProcMaterial,
  SortableWhiteboardRegion,
  MaterialDetailTitle,
  MaterialDetailContent,
  PartIdenticon,
} from "./Material";
import * as api from "../../data/api";
import * as routes from "../../data/routes";
import * as guiState from "../../data/gui-state";
import * as currentSt from "../../data/current-status";
import { Store, connect, AppActionBeforeMiddleware } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { LazySeq } from "../../data/lazyseq";
import { MaterialSummary } from "../../data/events.matsummary";
import { Tooltip } from "@material-ui/core";
import { JobAndGroups, extractJobGroups } from "../../data/build-job-groups";

interface ExistingMatInQueueDialogBodyProps {
  readonly display_material: matDetails.MaterialDetail;
  readonly quarantineQueue: string | null;
  readonly onClose: () => void;
  readonly removeFromQueue: (mat: matDetails.MaterialDetail) => void;
  readonly addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => void;
}

function ExistingMatInQueueDialogBody(props: ExistingMatInQueueDialogBodyProps) {
  const quarantineQueue = props.quarantineQueue;
  return (
    <>
      <DialogTitle disableTypography>
        <MaterialDetailTitle partName={props.display_material.partName} serial={props.display_material.serial} />
      </DialogTitle>
      <DialogContent>
        <MaterialDetailContent mat={props.display_material} />
      </DialogContent>
      <DialogActions>
        {quarantineQueue === null ? (
          <Button color="primary" onClick={() => props.removeFromQueue(props.display_material)}>
            Remove From System
          </Button>
        ) : (
          <Tooltip title={"Move to " + quarantineQueue}>
            <Button
              color="primary"
              onClick={() =>
                props.addExistingMat({
                  materialId: props.display_material.materialID,
                  queue: quarantineQueue,
                  queuePosition: 0,
                })
              }
            >
              Quarantine Material
            </Button>
          </Tooltip>
        )}
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
          ) : undefined}
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
                queuePosition: -1,
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

const useSelectJobStyles = makeStyles((theme) =>
  createStyles({
    expand: {
      transform: "rotate(-90deg)",
      transition: theme.transitions.create("transform", {
        duration: theme.transitions.duration.shortest,
      }),
    },
    expandOpen: {
      transform: "rotate(0deg)",
    },
    nested: {
      paddingLeft: theme.spacing(4),
    },
  })
);

interface SelectJobProps {
  readonly jobs: { [key: string]: Readonly<api.IInProcessJob> };
  readonly selected_casting?: AddNewCastingState;
  readonly selected_job?: AddNewJobProcessState;
  readonly onSelectJob: (j: AddNewJobProcessState | undefined) => void;
  readonly onSelectCasting: (c: AddNewCastingState | undefined) => void;
}

function SelectCastingOrJob(props: SelectJobProps) {
  const [selectedJob, setSelectedJob] = React.useState<string | null>(null);
  const jobs: ReadonlyArray<JobAndGroups> = React.useMemo(
    () =>
      LazySeq.ofObject(props.jobs)
        .map(([_uniq, j]) => extractJobGroups(j))
        .toVector()
        .sortOn((j) => j.job.priority)
        .toArray(),
    [props.jobs]
  );
  const classes = useSelectJobStyles();

  return (
    <List>
      {jobs.map((j, idx) => (
        <React.Fragment key={idx}>
          <ListItem
            button
            onClick={() => {
              if (props.selected_casting) props.onSelectCasting(undefined);
              if (props.selected_job) props.onSelectJob(undefined);
              setSelectedJob(selectedJob === j.job.unique ? null : j.job.unique);
            }}
          >
            <ListItemIcon>
              <ExpandMoreIcon
                className={clsx(classes.expand, {
                  [classes.expandOpen]: selectedJob === j.job.unique,
                })}
              />
            </ListItemIcon>
            <ListItemIcon>
              <PartIdenticon part={j.job.partName} />
            </ListItemIcon>
            <ListItemText
              primary={j.job.partName + " (" + j.job.unique + ")"}
              secondary={j.job.routeStartUTC.toLocaleString()}
            />
          </ListItem>
          <Collapse in={selectedJob === j.job.unique} timeout="auto">
            {j.castings.map((c, idx) => (
              <ListItem
                button
                key={idx}
                className={classes.nested}
                selected={props.selected_casting?.casting === c.casting}
                onClick={() => props.onSelectCasting({ casting: c.casting })}
              >
                <ListItemText
                  primary={"Casting " + c.casting}
                  secondary={j.castings.length !== 1 ? c.details : undefined}
                />
              </ListItem>
            ))}
            {j.machinedProcs.map((p, idx) => (
              <ListItem
                button
                key={idx}
                className={classes.nested}
                selected={
                  props.selected_job?.job.unique === j.job.unique &&
                  props.selected_job?.last_proc === p.lastProc &&
                  props.selected_job?.path_group === p.pathGroup
                }
                onClick={() => props.onSelectJob({ job: j.job, last_proc: p.lastProc, path_group: p.pathGroup })}
              >
                <ListItemText
                  primary={p.lastProc === 0 ? "Raw Material" : "Last machined process " + p.lastProc}
                  secondary={p.details}
                />
              </ListItem>
            ))}
          </Collapse>
        </React.Fragment>
      ))}
    </List>
  );
}

const ConnectedSelectJob = connect((s) => ({
  jobs: s.Current.current_status.jobs as {
    [key: string]: Readonly<api.IInProcessJob>;
  },
}))(SelectCastingOrJob);

interface AddNewMaterialProps {
  readonly queues: ReadonlyArray<string>;
  readonly queue_name?: string;
  readonly not_found_serial?: string;
  readonly onClose: () => void;
  readonly addCasting: (d: matDetails.AddNewCastingToQueueData) => void;
  readonly addAssigned: (d: matDetails.AddNewMaterialToQueueData) => void;
}

interface AddNewJobProcessState {
  readonly job: Readonly<api.IInProcessJob>;
  readonly last_proc: number;
  readonly path_group: number;
}

interface AddNewCastingState {
  readonly casting?: string;
  readonly partName?: string;
}

interface AddNewMaterialState {
  readonly selected_job?: AddNewJobProcessState;
  readonly selected_casting?: AddNewCastingState;
  readonly selected_queue?: string;
}

class AddNewMaterialBody extends React.PureComponent<AddNewMaterialProps, AddNewMaterialState> {
  state: AddNewMaterialState = {};

  onSelectJob = (j: AddNewJobProcessState | undefined) => {
    if (j) {
      this.setState({ selected_job: j, selected_casting: undefined });
    } else {
      this.setState({ selected_job: undefined });
    }
  };

  onSelectCasting = (casting: AddNewCastingState | undefined) => {
    if (casting) {
      this.setState({ selected_casting: casting, selected_job: undefined });
    } else {
      this.setState({ selected_casting: undefined });
    }
  };

  addMaterial = (queue?: string) => {
    if (queue === undefined) {
      return;
    }

    if (this.state.selected_casting !== undefined) {
      this.props.addCasting({
        casting: this.state.selected_casting.casting,
        partName: this.state.selected_casting.partName,
        queue: queue,
        queuePosition: -1,
        serial: this.props.not_found_serial,
      });
      this.setState({
        selected_job: undefined,
        selected_casting: undefined,
        selected_queue: undefined,
      });
    } else if (this.state.selected_job !== undefined) {
      this.props.addAssigned({
        jobUnique: this.state.selected_job.job.unique,
        lastCompletedProcess: this.state.selected_job.last_proc,
        pathGroup: this.state.selected_job.path_group,
        queue: queue,
        queuePosition: -1,
        serial: this.props.not_found_serial,
      });
      this.setState({
        selected_job: undefined,
        selected_casting: undefined,
        selected_queue: undefined,
      });
    }
  };

  render() {
    let queue = this.props.queue_name || this.state.selected_queue;
    if (queue === undefined && this.props.queues.length === 1) {
      queue = this.props.queues[0];
    }

    let allowAdd = this.state.selected_casting !== undefined || this.state.selected_job !== undefined;

    return (
      <>
        <DialogTitle>{this.props.not_found_serial ? this.props.not_found_serial : "Add New Material"}</DialogTitle>
        <DialogContent>
          {this.props.not_found_serial ? (
            <p>
              The serial {this.props.not_found_serial} was not found. Specify the job and process to add to the queue.
            </p>
          ) : undefined}
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
            ) : undefined}
            <ConnectedSelectJob
              selected_job={this.state.selected_job}
              selected_casting={this.state.selected_casting}
              onSelectJob={this.onSelectJob}
              onSelectCasting={this.onSelectCasting}
            />
          </div>
        </DialogContent>
        <DialogActions>
          <Button color="primary" onClick={() => this.addMaterial(queue)} disabled={!allowAdd}>
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
  readonly quarantineQueue: string | null;

  readonly onClose: () => void;
  readonly removeFromQueue: (mat: matDetails.MaterialDetail) => void;
  readonly addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => void;
  readonly addNewCasting: (d: matDetails.AddNewCastingToQueueData) => void;
  readonly addNewAssigned: (d: matDetails.AddNewMaterialToQueueData) => void;
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
          quarantineQueue={props.quarantineQueue}
          removeFromQueue={props.removeFromQueue}
          addExistingMat={props.addExistingMat}
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
          addAssigned={props.addNewAssigned}
          addCasting={props.addNewCasting}
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
  function (mat: matDetails.MaterialDetail | null, allMats: ReadonlyArray<api.IInProcessMaterial>) {
    if (mat === null) {
      return false;
    }
    if (mat.materialID < 0) {
      return false;
    }
    for (const inProcMat of allMats) {
      if (inProcMat.materialID === mat.materialID) {
        return inProcMat.location.type === api.LocType.InQueue;
      }
    }
    return false;
  }
);

const ConnectedMaterialDialog = connect(
  (st) => ({
    display_material: st.MaterialDetails.material,
    material_currently_in_queue: selectMatCurrentlyInQueue(st),
    addMatQueue: st.Gui.add_mat_to_queue,
    queueNames: st.Route.standalone_queues,
    quarantineQueue: st.ServerSettings.fmsInfo?.quarantineQueue || null,
  }),
  {
    onClose: () => [
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined },
    ],
    removeFromQueue: (mat: matDetails.MaterialDetail) =>
      [
        matDetails.removeFromQueue(mat),
        { type: matDetails.ActionType.CloseMaterialDialog },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined },
      ] as AppActionBeforeMiddleware,
    addNewAssigned: (d: matDetails.AddNewMaterialToQueueData) => [
      matDetails.addNewMaterialToQueue(d),
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined },
    ],
    addNewCasting: (d: matDetails.AddNewCastingToQueueData) => [
      matDetails.addNewCastingToQueue(d),
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined },
    ],
    addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => [
      matDetails.addExistingMaterialToQueue(d),
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined },
    ],
  }
)(QueueMatDialog);

interface ChooseSerialOrDirectJobProps {
  readonly dialog_open: boolean;
  readonly lookupSerial: (s: string) => void;
  readonly selectJobWithoutSerial: () => void;
  readonly onClose: () => void;
}

const ChooseSerialOrDirectJob = React.memo(function ChooseSerialOrJob(props: ChooseSerialOrDirectJobProps) {
  const [serial, setSerial] = React.useState<string | undefined>(undefined);
  function lookup() {
    if (serial && serial !== "") {
      props.lookupSerial(serial);
      setSerial(undefined);
    }
  }
  function close() {
    props.onClose();
    setSerial(undefined);
  }
  function manualSelect() {
    props.selectJobWithoutSerial();
    setSerial(undefined);
  }
  return (
    <Dialog open={props.dialog_open} onClose={close} maxWidth="md">
      <DialogTitle>Lookup Material</DialogTitle>
      <DialogContent>
        <div style={{ maxWidth: "25em" }}>
          <p>
            To find the details of the material to add, you can either scan a part&apos;s serial, lookup a serial, or
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
              alignItems: "center",
            }}
          >
            <div style={{ marginBottom: "2em" }}>
              <TextField label="Serial" value={serial || ""} onChange={(e) => setSerial(e.target.value)} />
            </div>
            <div>
              <Button variant="contained" color="secondary" onClick={lookup}>
                Lookup Serial
              </Button>
            </div>
          </div>
          <div style={{ paddingLeft: "8px" }}>
            <Button variant="contained" color="secondary" onClick={manualSelect}>
              Manually Select Job
            </Button>
          </div>
        </div>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} color="primary">
          Cancel
        </Button>
      </DialogActions>
    </Dialog>
  );
});

const ConnectedChooseSerialOrDirectJobDialog = connect(
  (st) => ({
    dialog_open: st.Gui.queue_dialog_mode_open,
  }),
  {
    onClose: () =>
      [
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: false,
        },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined },
      ] as AppActionBeforeMiddleware,
    lookupSerial: (serial: string) =>
      [
        ...matDetails.openMaterialBySerial(serial, false),
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: false,
        },
      ] as AppActionBeforeMiddleware,
    selectJobWithoutSerial: () =>
      [
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: false,
        },
        matDetails.openMaterialDialogWithEmptyMat(),
      ] as AppActionBeforeMiddleware,
  }
)(ChooseSerialOrDirectJob);

const queueStyles = createStyles({
  mainScrollable: {
    padding: "8px",
    width: "100%",
  },
});

interface QueueProps {
  readonly data: LoadStationAndQueueData;
  openMat: (m: Readonly<MaterialSummary>) => void;
  openAddToQueue: (queueName: string) => void;
  moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => void;
}

const Queues = withStyles(queueStyles)((props: QueueProps & WithStyles<typeof queueStyles>) => {
  const queues = props.data.queues
    .toVector()
    .sortOn(([q, _]) => q)
    .map(([q, mats]) => ({
      label: q,
      free: false,
      material: mats,
    }));

  let cells = queues;
  if (props.data.free) {
    cells = queues.prependAll([
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
                queuePosition: se.newIndex,
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
    data: buildQueueData(st),
  }),
  {
    openAddToQueue: (queueName: string) =>
      [
        {
          type: guiState.ActionType.SetAddMatToQueueModeDialogOpen,
          open: true,
        },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: queueName },
      ] as AppActionBeforeMiddleware,
    openMat: matDetails.openMaterialDialog,
    moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => [
      {
        type: currentSt.ActionType.ReorderQueuedMaterial,
        queue: d.queue,
        materialId: d.materialId,
        newIdx: d.queuePosition,
      },
      matDetails.addExistingMaterialToQueue(d),
    ],
  }
)(Queues);
