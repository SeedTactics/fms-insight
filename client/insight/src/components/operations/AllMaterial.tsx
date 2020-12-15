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
import { DragDropContext, Droppable, Draggable, DropResult } from "react-beautiful-dnd";
import {
  selectAllMaterialIntoBins,
  MaterialBin,
  MaterialBinType,
  moveMaterialBin,
  MaterialBinId,
} from "../../data/all-material-bins";
import { MaterialSummary } from "../../data/events.matsummary";
import { connect, Store, AppActionBeforeMiddleware, mkAC } from "../../store/store";
import * as matDetails from "../../data/material-details";
import * as currentSt from "../../data/current-status";
import * as guiState from "../../data/gui-state";
import { createSelector } from "reselect";
import Paper from "@material-ui/core/Paper";
import Typography from "@material-ui/core/Typography";
import Button from "@material-ui/core/Button";
import { LazySeq } from "../../data/lazyseq";
import { InProcMaterial, MaterialDialog } from "../station-monitor/Material";
import { IInProcessMaterial, LocType } from "../../data/api";
import { HashMap, Ordering } from "prelude-ts";
import {
  InvalidateCycleDialogButtons,
  InvalidateCycleDialogContent,
  InvalidateCycleState,
  SwapMaterialButtons,
  SwapMaterialDialogContent,
  SwapMaterialState,
} from "../station-monitor/InvalidateCycle";

enum DragType {
  Material = "DRAG_MATERIAL",
  Queue = "DRAG_QUEUE",
}

function getQueueStyle(isDraggingOver: boolean, draggingFromThisWith: string | undefined): React.CSSProperties {
  return {
    display: "flex",
    flexDirection: "column",
    flexWrap: "nowrap",
    width: "18em",
    minHeight: "20em",
    backgroundColor: isDraggingOver ? "#BDBDBD" : draggingFromThisWith ? "#EEEEEE" : undefined,
  };
}

interface MaterialQueueProps {
  readonly queue: string;
  readonly idx: number;
  readonly material: ReadonlyArray<Readonly<IInProcessMaterial>>;
  readonly openMat: (mat: MaterialSummary) => void;
}

const MaterialQueue = React.memo(function DraggableMaterialQueueF(props: MaterialQueueProps) {
  return (
    <Draggable draggableId={props.queue} index={props.idx}>
      {(provided, snapshot) => (
        <Paper
          ref={provided.innerRef}
          {...provided.draggableProps}
          style={{ ...provided.draggableProps.style, margin: "0.75em" }}
        >
          <div {...provided.dragHandleProps}>
            <Typography
              variant="h4"
              {...provided.dragHandleProps}
              color={snapshot.isDragging ? "primary" : "textPrimary"}
            >
              {props.queue}
            </Typography>
          </div>
          <Droppable droppableId={props.queue} type={DragType.Material}>
            {(provided, snapshot) => (
              <div
                ref={provided.innerRef}
                style={getQueueStyle(snapshot.isDraggingOver, snapshot.draggingFromThisWith)}
              >
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
              </div>
            )}
          </Droppable>
        </Paper>
      )}
    </Draggable>
  );
});

interface SystemMaterialProps<T> {
  readonly name: string;
  readonly draggableId: string;
  readonly idx: number;
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

function renderQueue(queue: string) {
  return queue;
}

function compareQueue(q1: string, q2: string) {
  return q1.localeCompare(q2);
}

class SystemMaterial<T extends string | number> extends React.PureComponent<SystemMaterialProps<T>> {
  render() {
    return (
      <Draggable draggableId={this.props.draggableId} index={this.props.idx}>
        {(provided, snapshot) => (
          <Paper
            ref={provided.innerRef}
            {...provided.draggableProps}
            style={{ ...provided.draggableProps.style, margin: "0.75em" }}
          >
            <div {...provided.dragHandleProps}>
              <Typography
                variant="h4"
                {...provided.dragHandleProps}
                color={snapshot.isDragging ? "primary" : "textPrimary"}
              >
                {this.props.name}
              </Typography>
            </div>
            <div style={getQueueStyle(false, undefined)}>
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
            </div>
          </Paper>
        )}
      </Draggable>
    );
  }
}

interface AllMatDialogProps {
  readonly display_material: matDetails.MaterialDetail | null;
  readonly current_material: ReadonlyArray<Readonly<IInProcessMaterial>>;
  readonly quarantineQueue: boolean;
  readonly removeFromQueue: (matId: number) => void;
  readonly onClose: () => void;
}

function AllMatDialog(props: AllMatDialogProps) {
  const [swapSt, setSwapSt] = React.useState<SwapMaterialState>(null);
  const [invalidateSt, setInvalidateSt] = React.useState<InvalidateCycleState>(null);

  const displayMat = props.display_material;
  const curMat =
    displayMat !== null ? props.current_material.find((m) => m.materialID === displayMat.materialID) ?? null : null;

  function close() {
    props.onClose();
    setSwapSt(null);
    setInvalidateSt(null);
  }

  return (
    <MaterialDialog
      display_material={props.display_material}
      onClose={close}
      allowNote={props.quarantineQueue}
      highlightProcess={invalidateSt?.process ?? undefined}
      extraDialogElements={
        <>
          <SwapMaterialDialogContent
            st={swapSt}
            setState={setSwapSt}
            curMat={curMat}
            current_material={props.current_material}
          />
          {displayMat && curMat && curMat.location.type === LocType.InQueue ? (
            <InvalidateCycleDialogContent st={invalidateSt} setState={setInvalidateSt} events={displayMat.events} />
          ) : undefined}
        </>
      }
      buttons={
        <>
          {displayMat && props.quarantineQueue ? (
            <Button color="primary" onClick={() => props.removeFromQueue(displayMat.materialID)}>
              Remove From System
            </Button>
          ) : undefined}
          <SwapMaterialButtons st={swapSt} setState={setSwapSt} curMat={curMat} close={close} operator={null} />
          {curMat && curMat.location.type === LocType.InQueue ? (
            <InvalidateCycleDialogButtons
              st={invalidateSt}
              setState={setInvalidateSt}
              curMat={curMat}
              operator={null}
            />
          ) : undefined}
        </>
      }
    />
  );
}

const ConnectedAllMatDialog = connect(
  (s) => ({
    current_material: s.Current.current_status.material,
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
    removeFromQueue: (matId: number) =>
      [
        matDetails.removeFromQueue(matId, null),
        { type: matDetails.ActionType.CloseMaterialDialog },
        { type: guiState.ActionType.SetAddMatToQueueName, queue: undefined },
      ] as AppActionBeforeMiddleware,
  }
)(AllMatDialog);

interface AllMaterialProps {
  readonly displaySystemBins: boolean;
  readonly allBins: ReadonlyArray<MaterialBin>;
  readonly display_material: matDetails.MaterialDetail | null;
  readonly openMat: (mat: MaterialSummary) => void;
  readonly moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => void;
  readonly moveMaterialBin: (curBinOrder: ReadonlyArray<MaterialBinId>, oldIdx: number, newIdx: number) => void;
}

function AllMaterial(props: AllMaterialProps) {
  React.useEffect(() => {
    document.title = "All Material - FMS Insight";
  }, []);

  const curBins = props.displaySystemBins
    ? props.allBins
    : props.allBins.filter((bin) => bin.type === MaterialBinType.QuarantineQueues);

  const onDragEnd = (result: DropResult): void => {
    if (!result.destination) return;
    if (result.reason === "CANCEL") return;

    if (result.type === DragType.Material) {
      const queue = result.destination.droppableId;
      const materialId = parseInt(result.draggableId);
      const queuePosition = result.destination.index;
      props.moveMaterialInQueue({ materialId, queue, queuePosition, operator: null });
    } else if (result.type === DragType.Queue) {
      props.moveMaterialBin(
        curBins.map((b) => b.binId),
        result.source.index,
        result.destination.index
      );
    }
  };

  const curDisplayQuarantine =
    props.display_material !== null &&
    curBins.findIndex(
      (bin) =>
        bin.type === MaterialBinType.QuarantineQueues &&
        bin.material.findIndex((mat) => mat.materialID === props.display_material?.materialID) >= 0
    ) >= 0;

  return (
    <DragDropContext onDragEnd={onDragEnd}>
      <Droppable droppableId="Board" type={DragType.Queue} direction="horizontal">
        {(provided) => (
          <div ref={provided.innerRef} style={{ display: "flex", flexWrap: "nowrap" }}>
            {curBins.map((matBin, idx) => {
              switch (matBin.type) {
                case MaterialBinType.LoadStations:
                  return (
                    <SystemMaterial
                      name="Load Stations"
                      draggableId={matBin.binId}
                      key={matBin.binId}
                      idx={idx}
                      renderLabel={renderLul}
                      compareLabel={compareLul}
                      material={matBin.byLul}
                      openMat={props.openMat}
                    />
                  );
                case MaterialBinType.Pallets:
                  return (
                    <SystemMaterial
                      name="Pallets"
                      draggableId={matBin.binId}
                      key={matBin.binId}
                      idx={idx}
                      renderLabel={renderPal}
                      compareLabel={comparePal}
                      material={matBin.byPallet}
                      openMat={props.openMat}
                    />
                  );
                case MaterialBinType.ActiveQueues:
                  return (
                    <SystemMaterial
                      name="Queues"
                      draggableId={matBin.binId}
                      key={matBin.binId}
                      idx={idx}
                      renderLabel={renderQueue}
                      compareLabel={compareQueue}
                      material={matBin.byQueue}
                      openMat={props.openMat}
                    />
                  );
                case MaterialBinType.QuarantineQueues:
                  return (
                    <MaterialQueue
                      key={matBin.binId}
                      idx={idx}
                      queue={matBin.queueName}
                      material={matBin.material}
                      openMat={props.openMat}
                    />
                  );
              }
            })}
            {provided.placeholder}
          </div>
        )}
      </Droppable>
      <ConnectedAllMatDialog display_material={props.display_material} quarantineQueue={curDisplayQuarantine} />
    </DragDropContext>
  );
}

const extractMaterialRegions = createSelector(
  (st: Store) => st.Current.current_status,
  (st: Store) => st.AllMatBins.curBinOrder,
  selectAllMaterialIntoBins
);

export default connect(
  (st) => ({
    allBins: extractMaterialRegions(st),
    display_material: st.MaterialDetails.material,
  }),
  {
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
    moveMaterialBin: moveMaterialBin,
  }
)(AllMaterial);
