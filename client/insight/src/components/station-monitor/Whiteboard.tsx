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

import { ReactNode, memo, useState } from "react";
import {
  closestCenter,
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  rectSortingStrategy,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { IInProcessMaterial } from "../../network/api.js";
import { SortableMatData } from "./Material.js";
import { useAddExistingMaterialToQueue } from "../../cell-status/material-details.js";
import { reorderQueuedMatInCurrentStatus } from "../../cell-status/current-status.js";
import { currentOperator } from "../../data/operators.js";
import { useAtomValue, useSetAtom } from "jotai";

export interface SortableRegionProps {
  readonly matIds: ReadonlyArray<number>;
  readonly direction: "vertical" | "rect";
  readonly queueName: string;
  readonly renderDragOverlay: (mat: Readonly<IInProcessMaterial>) => ReactNode;
  readonly children?: ReactNode;
}

export const SortableRegion = memo(function SortableRegion(props: SortableRegionProps) {
  const [activeMat, setActiveMat] = useState<Readonly<IInProcessMaterial> | undefined>(undefined);
  const [addExistingMatToQueue] = useAddExistingMaterialToQueue();
  const reorderQueuedMat = useSetAtom(reorderQueuedMatInCurrentStatus);
  const operator = useAtomValue(currentOperator);
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragStart={({ active }) => setActiveMat((active.data.current as SortableMatData).mat)}
      onDragCancel={() => setActiveMat(undefined)}
      onDragEnd={({ active, over }) => {
        if (over && active.id !== over.id) {
          const activeMatId = active.id as number;
          const overIdx = props.matIds.indexOf(over.id as number);
          addExistingMatToQueue({
            materialId: active.id as number,
            queue: props.queueName,
            queuePosition: overIdx,
            operator: operator,
          });
          reorderQueuedMat({
            queue: props.queueName,
            matId: activeMatId,
            newIdx: overIdx,
          });
        }
        setActiveMat(undefined);
      }}
    >
      <SortableContext
        items={props.matIds as number[]}
        strategy={props.direction === "vertical" ? verticalListSortingStrategy : rectSortingStrategy}
      >
        {props.children}
        <DragOverlay>{activeMat !== undefined ? props.renderDragOverlay(activeMat) : undefined}</DragOverlay>
      </SortableContext>
    </DndContext>
  );
});
