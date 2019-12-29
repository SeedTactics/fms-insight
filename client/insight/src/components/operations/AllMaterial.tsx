/* Copyright (c) 2019, John Lenz

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
import { DragDropContext, Droppable, Draggable, DropResult, ResponderProvided } from "react-beautiful-dnd";
import { selectAllMaterialIntoBins, MaterialBins } from "../../data/all-material-bins";
import { MaterialSummary } from "../../data/events.matsummary";
import { connect, Store } from "../../store/store";
import * as matDetails from "../../data/material-details";
import * as currentSt from "../../data/current-status";
import { createSelector } from "reselect";
import { Paper, Typography } from "@material-ui/core";
import { LazySeq } from "../../data/lazyseq";
import { InProcMaterial } from "../station-monitor/Material";
import { IInProcessMaterial } from "../../data/api";
import { HashMap, Ordering } from "prelude-ts";
// eslint-disable-next-line @typescript-eslint/no-var-requires
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

function getQueueStyle(isDraggingOver: boolean, draggingFromThisWith: string | undefined): React.CSSProperties {
  return {
    display: "flex",
    flexDirection: "column",
    margin: "0.75em",
    flexWrap: "nowrap",
    width: "18em",
    minHeight: "20em",
    backgroundColor: isDraggingOver ? "#BDBDBD" : draggingFromThisWith ? "#EEEEEE" : undefined
  };
}

interface MaterialQueueProps {
  readonly queue: string;
  readonly material: ReadonlyArray<Readonly<IInProcessMaterial>>;
  readonly openMat: (mat: MaterialSummary) => void;
}

const MaterialQueue = React.memo(function DraggableMaterialQueueF(props: MaterialQueueProps) {
  return (
    <Droppable droppableId={props.queue}>
      {(provided, snapshot) => (
        <Paper ref={provided.innerRef} style={getQueueStyle(snapshot.isDraggingOver, snapshot.draggingFromThisWith)}>
          <Typography variant="h4">{props.queue}</Typography>
          {props.material.map((mat, idx) => (
            <Draggable key={mat.materialID} draggableId={mat.materialID.toString()} index={idx}>
              {(provided, snapshot) => (
                <InProcMaterial
                  mat={mat}
                  onOpen={props.openMat}
                  draggableProvided={provided}
                  hideAvatar
                  isDragging={snapshot.isDragging}
                />
              )}
            </Draggable>
          ))}
          {provided.placeholder}
        </Paper>
      )}
    </Droppable>
  );
});

interface SystemMaterialProps<T> {
  readonly name: string;
  readonly material: HashMap<T, ReadonlyArray<Readonly<IInProcessMaterial>>>;
  readonly renderLabel: (label: T) => string;
  readonly compareLabel: (l1: T, l2: T) => Ordering;
  readonly openMat: (mat: MaterialSummary) => void;
}

function renderLul(lul: number) {
  return "L/U " + lul.toString();
}

function compareLul(l1: number, l2: number) {
  return l1 - l2;
}

function renderPal(pal: string) {
  return "Pallet " + pal;
}

function comparePal(p1: string, p2: string) {
  const n1 = parseInt(p1);
  const n2 = parseInt(p2);
  if (isNaN(n1) || isNaN(n2)) {
    return p1.localeCompare(p2);
  } else {
    return n1 - n2;
  }
}

class SystemMaterial<T extends string | number> extends React.PureComponent<SystemMaterialProps<T>> {
  render() {
    return (
      <Paper style={getQueueStyle(false, undefined)}>
        <Typography variant="h4">{this.props.name}</Typography>
        {LazySeq.ofIterable(this.props.material)
          .sortBy(([l1, _m1], [l2, _m2]) => this.props.compareLabel(l1, l2))
          .map(([label, material], idx) => (
            <div key={idx}>
              <Typography variant="caption">{this.props.renderLabel(label)}</Typography>
              {material.map((mat, idx) => (
                <InProcMaterial key={idx} mat={mat} onOpen={this.props.openMat} hideAvatar />
              ))}
            </div>
          ))}
      </Paper>
    );
  }
}

interface AllMaterialProps {
  readonly allMat: MaterialBins;
  readonly openMat: (mat: MaterialSummary) => void;
  readonly moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => void;
}

function AllMaterial(props: AllMaterialProps) {
  const onDragEnd = React.useCallback(
    (result: DropResult, provided: ResponderProvided): void => {
      if (!result.destination) return;
      const queue = result.destination.droppableId;
      const materialId = parseInt(result.draggableId);
      const queuePosition = result.destination.index;
      props.moveMaterialInQueue({ materialId, queue, queuePosition });
    },
    [props.moveMaterialInQueue]
  );

  return (
    <DocumentTitle title="All Material - FMS Insight">
      <DragDropContext onDragEnd={onDragEnd}>
        <div style={{ display: "flex", flexWrap: "nowrap" }}>
          <SystemMaterial
            name="Load Stations"
            renderLabel={renderLul}
            compareLabel={compareLul}
            material={props.allMat.loadStations}
            openMat={props.openMat}
          />
          <SystemMaterial
            name="Pallets"
            renderLabel={renderPal}
            compareLabel={comparePal}
            material={props.allMat.pallets}
            openMat={props.openMat}
          />
          {LazySeq.ofIterable(props.allMat.queues).map(([queueName, material], idx) => (
            <MaterialQueue key={idx} queue={queueName} material={material} openMat={props.openMat} />
          ))}
        </div>
      </DragDropContext>
    </DocumentTitle>
  );
}

const extractMaterialRegions = createSelector((st: Store) => st.Current.current_status, selectAllMaterialIntoBins);

export default connect(
  st => ({
    allMat: extractMaterialRegions(st)
  }),
  {
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
)(AllMaterial);
