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
import { createStyles } from "@material-ui/core/styles";
import { createSelector } from "reselect";
import Button from "@material-ui/core/Button";
import List from "@material-ui/core/List";
import ListItem from "@material-ui/core/ListItem";
import ListItemText from "@material-ui/core/ListItemText";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import ListSubheader from "@material-ui/core/ListSubheader";
import MenuItem from "@material-ui/core/MenuItem";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogTitle from "@material-ui/core/DialogTitle";
import Collapse from "@material-ui/core/Collapse";
import TextField from "@material-ui/core/TextField";
import { makeStyles } from "@material-ui/core/styles";
import ExpandMoreIcon from "@material-ui/icons/ExpandMore";
import clsx from "clsx";

import { MaterialDetailTitle, MaterialDetailContent, PartIdenticon } from "./Material";
import * as api from "../../data/api";
import * as guiState from "../../data/gui-state";
import { Store, connect, AppActionBeforeMiddleware } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { LazySeq } from "../../data/lazyseq";
import { Tooltip } from "@material-ui/core";
import { JobAndGroups, extractJobGroups } from "../../data/build-job-groups";
import { HashSet } from "prelude-ts";

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
  readonly queue: string | undefined;
  readonly jobs: { [key: string]: Readonly<api.IInProcessJob> };
  readonly selected_job?: AddNewJobProcessState;
  readonly onSelectJob: (j: AddNewJobProcessState | undefined) => void;
}

function SelectJob(props: SelectJobProps) {
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
            {j.machinedProcs.map((p, idx) => (
              <Tooltip
                title={props.queue !== undefined && !p.queues.contains(props.queue) ? "Not used in " + props.queue : ""}
                key={idx}
              >
                <div>
                  <ListItem
                    button
                    className={classes.nested}
                    disabled={props.queue !== undefined && !p.queues.contains(props.queue)}
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
                </div>
              </Tooltip>
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
}))(SelectJob);

interface AddNewMaterialProps {
  readonly queues: ReadonlyArray<string>;
  readonly queue_name?: string;
  readonly not_found_serial?: string;
  readonly onClose: () => void;
  readonly addAssigned: (d: matDetails.AddNewMaterialToQueueData) => void;
}

interface AddNewJobProcessState {
  readonly job: Readonly<api.IInProcessJob>;
  readonly last_proc: number;
  readonly path_group: number;
}

interface AddNewMaterialState {
  readonly selected_job?: AddNewJobProcessState;
  readonly selected_queue?: string;
}

class AddNewMaterialBody extends React.PureComponent<AddNewMaterialProps, AddNewMaterialState> {
  state: AddNewMaterialState = {};

  onSelectJob = (j: AddNewJobProcessState | undefined) => {
    if (j) {
      this.setState({ selected_job: j });
    } else {
      this.setState({ selected_job: undefined });
    }
  };

  addMaterial = (queue?: string) => {
    if (queue === undefined) {
      return;
    }

    if (this.state.selected_job !== undefined) {
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
        selected_queue: undefined,
      });
    }
  };

  render() {
    let queue = this.props.queue_name || this.state.selected_queue;
    if (queue === undefined && this.props.queues.length === 1) {
      queue = this.props.queues[0];
    }

    const allowAdd = this.state.selected_job !== undefined;

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
            <ConnectedSelectJob selected_job={this.state.selected_job} onSelectJob={this.onSelectJob} queue={queue} />
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

export interface QueueMatDialogProps {
  readonly display_material: matDetails.MaterialDetail | null;
  readonly material_currently_in_queue: boolean;
  readonly addMatQueue?: string;
  readonly queueNames: ReadonlyArray<string>;
  readonly quarantineQueue: string | null;

  readonly onClose: () => void;
  readonly removeFromQueue: (mat: matDetails.MaterialDetail) => void;
  readonly addExistingMat: (d: matDetails.AddExistingMaterialToQueueData) => void;
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
        />
      );
    }
  }

  return (
    <Dialog open={props.display_material !== null} onClose={props.onClose} maxWidth="lg">
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

export const ConnectedMaterialDialog = connect(
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

export const ConnectedChooseSerialOrDirectJobDialog = connect(
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

interface AddCastingProps {
  readonly queue: string | null;
  readonly jobs: { [key: string]: Readonly<api.IInProcessJob> };
  readonly castingNames: HashSet<string>;
  readonly addNewCasting: (c: matDetails.AddNewCastingToQueueData) => void;
  readonly closeDialog: () => void;
}

function AddCastingDialog(props: AddCastingProps) {
  const [selectedCasting, setSelectedCasting] = React.useState<string | null>();
  const castings: ReadonlyArray<[string, number]> = React.useMemo(
    () =>
      LazySeq.ofObject(props.jobs)
        .flatMap(([, j]) => j.procsAndPaths[0].paths)
        .filter((p) => p.casting !== undefined && p.casting !== "")
        .map((p) => ({ casting: p.casting as string, cnt: 1 }))
        .appendAll(LazySeq.ofIterable(props.castingNames).map((c) => ({ casting: c, cnt: 0 })))
        .toMap(
          (c) => [c.casting, c.cnt],
          (q1, q2) => q1 + q2
        )
        .toVector()
        .sortBy(([c1, q1], [c2, q2]) => {
          if (q1 === 0 && q2 != 0) {
            return 1; // put non-zero quantities first
          } else if (q1 !== 0 && q2 == 0) {
            return -1;
          } else {
            return c1.localeCompare(c2);
          }
        })
        .toArray(),
    [props.jobs, props.castingNames]
  );

  return (
    <List>
      <ListSubheader>Unassigned Casting</ListSubheader>
      {castings.map(([casting, jobCnt], idx) => (
        <ListItem button key={idx} selected={selectedCasting === casting} onClick={() => setSelectedCasting(casting)}>
          <ListItemIcon>
            <PartIdenticon part={casting} />
          </ListItemIcon>
          <ListItemText
            primary={casting}
            secondary={jobCnt === 0 ? "Not used by any current jobs" : `Used by ${jobCnt} current jobs`}
          />
        </ListItem>
      ))}
    </List>
  );
}

export const ConnectedAddCastingDialog = connect(
  (s) => ({
    jobs: s.Current.current_status.jobs as {
      [key: string]: Readonly<api.IInProcessJob>;
    },
    castingNames: s.Events.last30.sim_use.castingNames,
  }),
  {
    addNewCasting: matDetails.addNewCastingToQueue,
  }
)(AddCastingDialog);
