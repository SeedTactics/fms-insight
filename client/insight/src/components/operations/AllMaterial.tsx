/* Copyright (c) 2022, John Lenz

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
import {
  selectAllMaterialIntoBins,
  MaterialBinType,
  MaterialBin,
  moveMaterialBin,
  currentMaterialBinOrder,
  moveMaterialInBin,
  MaterialBinId,
  findMaterialInQuarantineQueues,
  findQueueInQuarantineQueues,
} from "../../data/all-material-bins.js";
import * as matDetails from "../../cell-status/material-details.js";
import * as currentSt from "../../cell-status/current-status.js";
import { Box } from "@mui/material";
import { Typography } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";
import {
  DragOverlayInProcMaterial,
  InProcMaterial,
  MaterialDialog,
  SortableInProcMaterial,
  SortableMatData,
} from "../station-monitor/Material.js";
import { IInProcessMaterial } from "../../network/api.js";
import {
  InvalidateCycleDialogButtons,
  InvalidateCycleDialogContent,
  InvalidateCycleState,
  SwapMaterialButtons,
  SwapMaterialDialogContent,
  SwapMaterialState,
} from "../station-monitor/InvalidateCycle.js";
import { useRecoilState, useRecoilValue } from "recoil";
import {
  horizontalListSortingStrategy,
  SortableContext,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import {
  closestCenter,
  CollisionDetection,
  DndContext,
  DragOverlay,
  getFirstCollision,
  MeasuringStrategy,
  pointerWithin,
  rectIntersection,
} from "@dnd-kit/core";
import { useRecoilConduit } from "../../util/recoil-util.js";
import { QuarantineMatButton } from "../station-monitor/QuarantineButton.js";

type ColWithTitleProps = {
  readonly label: string;
  readonly binId: MaterialBinId;
  readonly children?: React.ReactNode;
};

type ColWithTitleSortableProps = {
  readonly dragRootProps?: React.HTMLAttributes<HTMLDivElement>;
  readonly dragHandleProps?: React.HTMLAttributes<HTMLDivElement>;
  readonly setDragHandleRef?: React.RefCallback<HTMLDivElement>;
  readonly isDragOverlay?: boolean;
  readonly isActiveDrag?: boolean;
};

const ColumnWithTitle = React.forwardRef(function MaterialBin(
  props: ColWithTitleProps & ColWithTitleSortableProps,
  ref: React.ForwardedRef<HTMLDivElement>
) {
  return (
    <Box
      sx={{
        margin: props.isDragOverlay ? undefined : "0.75em",
        opacity: props.isActiveDrag ? 0.2 : 1,
        border: "1px solid black",
        padding: "4px",
      }}
      ref={ref}
      {...props.dragRootProps}
    >
      <Box
        role="button"
        tabIndex={0}
        sx={{ cursor: props.isDragOverlay ? "grabbing" : "grab" }}
        {...props.dragHandleProps}
      >
        <Typography variant="h4">{props.label}</Typography>
      </Box>
      <Box
        sx={{
          display: "flex",
          flexDirection: "column",
          flexWrap: "nowrap",
          width: "18em",
          minHeight: "20em",
        }}
      >
        {props.children}
      </Box>
    </Box>
  );
});

type SortableColumnData = {
  readonly type: "container";
  readonly bin: MaterialBin;
};

function SortableColumnWithTitle(props: ColWithTitleProps & { readonly bin: MaterialBin }) {
  const data: SortableColumnData = { type: "container", bin: props.bin };
  const {
    active,
    isDragging,
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
  } = useSortable({
    id: props.binId,
    data,
  });

  const handleProps: { [key: string]: unknown } = {
    ...listeners,
  };
  for (const [a, v] of Object.entries(attributes)) {
    if (a.startsWith("aria")) {
      handleProps[a] = v;
    }
  }
  const style = {
    transform: transform
      ? `translate3d(${Math.round(transform.x)}px, ${Math.round(transform.y)}px, 0)`
      : undefined,
    transition: active !== null ? transition : undefined,
  };

  return (
    <ColumnWithTitle
      ref={setNodeRef}
      dragRootProps={{ style }}
      dragHandleProps={handleProps}
      setDragHandleRef={setActivatorNodeRef}
      isActiveDrag={isDragging}
      {...props}
    />
  );
}

interface QuarantineQueueProps {
  readonly binId: MaterialBinId;
  readonly queue: string;
  readonly material: ReadonlyArray<Readonly<IInProcessMaterial>>;
  readonly bin: MaterialBin;
  readonly isDragOverlay?: boolean;
}

const QuarantineQueue = React.memo(function QuarantineQueue(props: QuarantineQueueProps) {
  if (props.isDragOverlay) {
    return (
      <ColumnWithTitle binId={props.binId} label={props.queue} isDragOverlay>
        {props.material.map((mat) => (
          <InProcMaterial key={mat.materialID} mat={mat} hideAvatar showHandle />
        ))}
      </ColumnWithTitle>
    );
  } else {
    return (
      <SortableColumnWithTitle binId={props.binId} label={props.queue} bin={props.bin}>
        <SortableContext
          items={props.material.map((m) => m.materialID)}
          strategy={verticalListSortingStrategy}
        >
          {props.material.map((mat) => (
            <SortableInProcMaterial key={mat.materialID} mat={mat} hideAvatar />
          ))}
        </SortableContext>
      </SortableColumnWithTitle>
    );
  }
});

interface SystemMaterialProps<T> {
  readonly name: string;
  readonly binId: string;
  readonly material: ReadonlyMap<T, ReadonlyArray<Readonly<IInProcessMaterial>>>;
  readonly bin: MaterialBin;
  readonly isDragOverlay?: boolean;
  readonly renderLabel: (label: T) => string;
  readonly compareLabel: (l1: T, l2: T) => number;
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
  override render() {
    const Col = this.props.isDragOverlay ? ColumnWithTitle : SortableColumnWithTitle;
    return (
      <Col
        label={this.props.name}
        binId={this.props.binId}
        bin={this.props.bin}
        isDragOverlay={this.props.isDragOverlay}
      >
        {LazySeq.of(this.props.material)
          .sortWith(([l1, _m1], [l2, _m2]) => this.props.compareLabel(l1, l2))
          .map(([label, material], idx) => (
            <div key={idx}>
              <Typography variant="caption">{this.props.renderLabel(label)}</Typography>
              {material.map((mat, idx) => (
                <InProcMaterial key={idx} mat={mat} hideAvatar />
              ))}
            </div>
          ))}
      </Col>
    );
  }
}

function MaterialBinColumn({
  matBin,
  isDragOverlay,
}: {
  readonly matBin: MaterialBin;
  readonly isDragOverlay?: boolean;
}) {
  switch (matBin.type) {
    case MaterialBinType.LoadStations:
      return (
        <SystemMaterial
          name="Load Stations"
          binId={matBin.binId}
          renderLabel={renderLul}
          compareLabel={compareLul}
          material={matBin.byLul}
          bin={matBin}
          isDragOverlay={isDragOverlay}
        />
      );
    case MaterialBinType.Pallets:
      return (
        <SystemMaterial
          name="Pallets"
          binId={matBin.binId}
          renderLabel={renderPal}
          compareLabel={comparePal}
          material={matBin.byPallet}
          bin={matBin}
          isDragOverlay={isDragOverlay}
        />
      );
    case MaterialBinType.ActiveQueues:
      return (
        <SystemMaterial
          name="Queues"
          binId={matBin.binId}
          renderLabel={renderQueue}
          compareLabel={compareQueue}
          material={matBin.byQueue}
          bin={matBin}
          isDragOverlay={isDragOverlay}
        />
      );
    case MaterialBinType.QuarantineQueues:
      return (
        <QuarantineQueue
          binId={matBin.binId}
          queue={matBin.queueName}
          material={matBin.material}
          bin={matBin}
          isDragOverlay={isDragOverlay}
        />
      );
  }
}

const AllMatDialog = React.memo(function AllMatDialog() {
  const [swapSt, setSwapSt] = React.useState<SwapMaterialState>(null);
  const [invalidateSt, setInvalidateSt] = React.useState<InvalidateCycleState | null>(null);

  function onClose() {
    setSwapSt(null);
    setInvalidateSt(null);
  }

  return (
    <MaterialDialog
      onClose={onClose}
      allowNote
      highlightProcess={invalidateSt?.process ?? undefined}
      extraDialogElements={
        <>
          <SwapMaterialDialogContent st={swapSt} setState={setSwapSt} />
          {invalidateSt !== null ? (
            <InvalidateCycleDialogContent st={invalidateSt} setState={setInvalidateSt} />
          ) : null}
        </>
      }
      buttons={
        <>
          <QuarantineMatButton onClose={onClose} ignoreOperator />
          <SwapMaterialButtons st={swapSt} setState={setSwapSt} onClose={onClose} ignoreOperator />
          <InvalidateCycleDialogButtons
            st={invalidateSt}
            setState={setInvalidateSt}
            onClose={onClose}
            ignoreOperator
          />
        </>
      }
    />
  );
});

function useCollisionDetection(allBins: ReadonlyArray<MaterialBin>): CollisionDetection {
  return React.useCallback(
    (args) => {
      if (typeof args.active.id === "string") {
        // columns only collide with other columns
        return closestCenter({
          ...args,
          droppableContainers: args.droppableContainers.filter(
            (container) => typeof container.id === "string"
          ),
        });
      }

      // Intersect the material with any droppable (either another material or a column)
      const pointerIntersections = pointerWithin(args);
      const intersections = pointerIntersections.length > 0 ? pointerIntersections : rectIntersection(args);
      let overId = getFirstCollision(intersections, "id");

      if (overId != null) {
        if (typeof overId === "string") {
          // If the over is a non-empty column, find the closest material inside that column
          const bin = findQueueInQuarantineQueues(overId, allBins);
          if (bin && bin.bin.material.length > 0) {
            overId = closestCenter({
              ...args,
              droppableContainers: args.droppableContainers.filter(
                (container) =>
                  container.id !== overId &&
                  bin.bin.material.findIndex((m) => m.materialID === container.id) >= 0
              ),
            })[0]?.id;
          }
        }

        return [{ id: overId }];
      }
      return [];
    },
    [allBins]
  );
}

interface AllMaterialProps {
  readonly displaySystemBins: boolean;
}

type CurActiveDrag =
  | {
      readonly type: "material";
      readonly mat: Readonly<IInProcessMaterial>;
      readonly curOverBinId: MaterialBinId | null;
      readonly initialIdx: number;
    }
  | { readonly type: "column"; readonly bin: MaterialBin };

export function AllMaterial(props: AllMaterialProps) {
  React.useEffect(() => {
    document.title = "All Material - FMS Insight";
  }, []);
  const st = useRecoilValue(currentSt.currentStatus);
  const [matBinOrder, setMatBinOrder] = useRecoilState(currentMaterialBinOrder);
  const [addExistingMatToQueue] = matDetails.useAddExistingMaterialToQueue();
  const reorderQueuedMat = useRecoilConduit(currentSt.reorderQueuedMatInCurrentStatus);
  const [activeDrag, setActiveDrag] = React.useState<CurActiveDrag | null>(null);

  const allBins = React.useMemo(() => {
    const allBins = selectAllMaterialIntoBins(st, matBinOrder);
    if (activeDrag && activeDrag.type === "material" && activeDrag.curOverBinId !== null) {
      return moveMaterialInBin(allBins, activeDrag.mat, activeDrag.curOverBinId, activeDrag.initialIdx);
    } else {
      return allBins;
    }
  }, [st, matBinOrder, activeDrag]);

  const curBins = props.displaySystemBins
    ? allBins
    : allBins.filter((bin) => bin.type === MaterialBinType.QuarantineQueues);

  const collisionDetectionStrategy = useCollisionDetection(allBins);

  return (
    <DndContext
      collisionDetection={collisionDetectionStrategy}
      measuring={{
        droppable: { strategy: MeasuringStrategy.Always },
      }}
      onDragStart={({ active }) => {
        if (typeof active.id === "string") {
          setActiveDrag({ type: "column", bin: (active.data.current as SortableColumnData).bin });
        } else {
          setActiveDrag({
            type: "material",
            mat: (active.data.current as SortableMatData).mat,
            curOverBinId: null,
            initialIdx: 0,
          });
        }
      }}
      onDragOver={({ over }) => {
        if (!over) return;
        if (activeDrag === null || activeDrag.type === "column") return;
        const overCol =
          typeof over.id === "string"
            ? findQueueInQuarantineQueues(over.id, allBins)
            : findMaterialInQuarantineQueues(over.id, allBins);
        if (!overCol) return;

        if (overCol.bin.binId !== activeDrag.curOverBinId) {
          setActiveDrag({
            ...activeDrag,
            curOverBinId: overCol.bin.binId,
            initialIdx: overCol.idx,
          });
        }
      }}
      onDragEnd={({ over }) => {
        if (activeDrag === null) return;
        if (!over) {
          setActiveDrag(null);
          return;
        }
        if (activeDrag.type === "column") {
          if (typeof over.id === "number") {
            console.log("Invalid, collision should never allow a column drag to be over a material");
          } else {
            setMatBinOrder(
              moveMaterialBin(
                curBins.map((b) => b.binId),
                curBins.findIndex((b) => b.binId === activeDrag.bin.binId),
                curBins.findIndex((b) => b.binId === over.id)
              )
            );
          }
        } else {
          const overCol =
            typeof over.id === "string"
              ? findQueueInQuarantineQueues(over.id, allBins)
              : findMaterialInQuarantineQueues(over.id, allBins);
          if (overCol) {
            addExistingMatToQueue({
              materialId: activeDrag.mat.materialID,
              queue: overCol.bin.queueName,
              queuePosition: overCol.idx,
              operator: null,
            });
            reorderQueuedMat({
              queue: overCol.bin.queueName,
              matId: activeDrag.mat.materialID,
              newIdx: overCol.idx,
            });
          }
        }

        setActiveDrag(null);
      }}
      onDragCancel={() => setActiveDrag(null)}
    >
      <SortableContext items={curBins.map((b) => b.binId)} strategy={horizontalListSortingStrategy}>
        <main
          style={{
            display: "flex",
            flexWrap: "nowrap",
            backgroundColor: "#F8F8F8",
            minHeight: "calc(100vh - 64px)",
          }}
        >
          {curBins.map((matBin) => (
            <MaterialBinColumn key={matBin.binId} matBin={matBin} />
          ))}
        </main>
      </SortableContext>
      <DragOverlay>
        {activeDrag?.type === "material" ? (
          <DragOverlayInProcMaterial mat={activeDrag.mat} hideAvatar />
        ) : activeDrag?.type === "column" ? (
          <MaterialBinColumn matBin={activeDrag.bin} isDragOverlay />
        ) : undefined}
      </DragOverlay>
      <AllMatDialog />
    </DndContext>
  );
}
