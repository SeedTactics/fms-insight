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

import { LoadStationAndQueueData, selectLoadStationAndQueueProps } from "../../data/load-station";
import { SortableInProcMaterial, SortableWhiteboardRegion } from "./Material";
import * as api from "../../data/api";
import * as routes from "../../data/routes";
import * as guiState from "../../data/gui-state";
import * as currentSt from "../../data/current-status";
import { Store, connect, AppActionBeforeMiddleware } from "../../store/store";
import * as matDetails from "../../data/material-details";
import { MaterialSummary } from "../../data/events.matsummary";
import { ConnectedMaterialDialog, ConnectedChooseSerialOrDirectJobDialog } from "./QueuesAddMaterial";

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
  React.useEffect(() => {
    document.title = "Material Queues - FMS Insight";
  }, []);
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
