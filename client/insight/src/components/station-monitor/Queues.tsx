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
import { SortEnd } from "react-sortable-hoc";
import { HashSet } from "prelude-ts";
import Table from "@material-ui/core/Table";
import TableHead from "@material-ui/core/TableHead";
import TableCell from "@material-ui/core/TableCell";
import TableRow from "@material-ui/core/TableRow";
import TableBody from "@material-ui/core/TableBody";
import TableFooter from "@material-ui/core/TableFooter";
import Button from "@material-ui/core/Button";
import Tooltip from "@material-ui/core/Tooltip";

import { LoadStationAndQueueData, selectLoadStationAndQueueProps } from "../../data/load-station";
import { SortableInProcMaterial, SortableWhiteboardRegion } from "./Material";
import * as api from "../../data/api";
import * as routes from "../../data/routes";
import * as guiState from "../../data/gui-state";
import * as currentSt from "../../data/current-status";
import { Store, connect, AppActionBeforeMiddleware, useSelector } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { MaterialSummary } from "../../data/events.matsummary";
import {
  ConnectedMaterialDialog,
  ConnectedChooseSerialOrDirectJobDialog,
  ConnectedAddCastingDialog,
} from "./QueuesAddMaterial";
import { LazySeq } from "../../data/lazyseq";

interface RawMaterialJobTableProps {
  readonly queue: string;
  readonly addCastings: () => void;
}

interface RawMatJobPath {
  readonly job: Readonly<api.IInProcessJob>;
  readonly proc1Path: number;
  readonly path: Readonly<api.IProcPathInfo>;
  readonly plannedQty: number;
  readonly startedQty: number;
  readonly assignedRaw: number;
}

function RawMaterialJobTable(props: RawMaterialJobTableProps) {
  const currentJobs = useSelector((s) => s.Current.current_status.jobs);
  const mats = useSelector((s) => s.Current.current_status.material);

  const jobs: ReadonlyArray<RawMatJobPath> = React.useMemo(
    () =>
      LazySeq.ofObject(currentJobs)
        .flatMap(([, j]) =>
          j.procsAndPaths[0].paths
            .filter((p) => p.casting && p.casting !== "" && p.inputQueue == props.queue)
            .map((path, idx) => ({
              job: j,
              path: path,
              proc1Path: idx,
              plannedQty: j.cyclesOnFirstProcess[idx],
              startedQty:
                (j.completed?.[0]?.[idx] || 0) +
                LazySeq.ofIterable(mats)
                  .filter(
                    (m) =>
                      (m.location.type !== api.LocType.InQueue ||
                        (m.location.type === api.LocType.InQueue && m.location.currentQueue !== props.queue)) &&
                      m.jobUnique === j.unique &&
                      m.process === 1 &&
                      m.path === idx + 1
                  )
                  .length(),
              assignedRaw: LazySeq.ofIterable(mats)
                .filter(
                  (m) =>
                    m.location.type === api.LocType.InQueue &&
                    m.location.currentQueue === props.queue &&
                    m.jobUnique === j.unique &&
                    m.process === 1 &&
                    m.path === idx + 1
                )
                .length(),
            }))
        )
        .toArray(),
    [currentJobs, mats]
  );

  return (
    <Table style={{ margin: "1em 5em 0 5em" }}>
      <TableHead>
        <TableCell>Job</TableCell>
        <TableCell>Starting Time</TableCell>
        <TableCell>Material</TableCell>
        <TableCell>Note</TableCell>
        <TableCell align="right">Planned Quantity</TableCell>
        <TableCell align="right">Started Quantity</TableCell>
        <TableCell align="right">Assigned Raw Material</TableCell>
        <TableCell align="right">Required</TableCell>
        <TableCell align="right">Available Unassigned</TableCell>
      </TableHead>
      <TableBody>
        {jobs.map((j, idx) => (
          <TableRow key={idx}>
            <TableCell>{j.job.unique}</TableCell>
            <TableCell>{j.path.simulatedStartingUTC.toLocaleString()}</TableCell>
            <TableCell>{j.path.casting}</TableCell>
            <TableCell>{j.job.comment}</TableCell>
            <TableCell align="right">{j.plannedQty}</TableCell>
            <TableCell align="right">{j.startedQty}</TableCell>
            <TableCell align="right">{j.assignedRaw}</TableCell>
            <TableCell align="right">
              <Tooltip
                title={
                  j.startedQty > 0 || j.assignedRaw > 0 ? `${j.plannedQty} - ${j.startedQty} - ${j.assignedRaw}` : ""
                }
              >
                <span>{j.plannedQty - j.startedQty - j.assignedRaw}</span>
              </Tooltip>
            </TableCell>
            <TableCell align="right">100</TableCell>
          </TableRow>
        ))}
      </TableBody>
      <TableFooter>
        <TableCell colSpan={8} />
        <TableCell align="right">
          <Button color="primary" variant="outlined" onClick={props.addCastings}>
            Add Raw Material
          </Button>
        </TableCell>
      </TableFooter>
    </Table>
  );
}

const queueStyles = createStyles({
  mainScrollable: {
    padding: "8px",
    width: "100%",
  },
});

interface QueueProps {
  readonly data: LoadStationAndQueueData;
  readonly rawMaterialQueues: HashSet<string>;
  openMat: (m: Readonly<MaterialSummary>) => void;
  openAddToQueue: (queueName: string) => void;
  moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => void;
}

const Queues = withStyles(queueStyles)((props: QueueProps & WithStyles<typeof queueStyles>) => {
  React.useEffect(() => {
    document.title = "Material Queues - FMS Insight";
  }, []);
  const [addCastingQueue, setAddCastingQueue] = React.useState<string | null>(null);
  const closeAddCastingDialog = React.useCallback(() => setAddCastingQueue(null), []);
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
    <main data-testid="stationmonitor-queues" className={props.classes.mainScrollable}>
      {cells.zipWithIndex().map(([region, idx]) => (
        <div style={{ borderBottom: "1px solid rgba(0,0,0,0.12)" }} key={idx}>
          <SortableWhiteboardRegion
            axis="xy"
            label={region.label}
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
            {props.rawMaterialQueues.contains(region.label) ? (
              <RawMaterialJobTable queue={region.label} addCastings={() => setAddCastingQueue(region.label)} />
            ) : undefined}
          </SortableWhiteboardRegion>
        </div>
      ))}
      <ConnectedMaterialDialog />
      <ConnectedChooseSerialOrDirectJobDialog />
      <ConnectedAddCastingDialog queue={addCastingQueue} closeDialog={closeAddCastingDialog} />
    </main>
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
    rawMaterialQueues: st.Events.last30.sim_use.rawMaterialQueues,
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
