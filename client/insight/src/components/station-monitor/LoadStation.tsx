/* Copyright (c) 2023, John Lenz

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

import { useMemo, memo, useState, useCallback } from "react";
import { Box, useMediaQuery, Button, Typography } from "@mui/material";
import { LazySeq, mkCompareByProperties, OrderedMap } from "@seedtactics/immutable-collections";

import { FolderOpen as FolderOpenIcon } from "@mui/icons-material";

import {
  MaterialDialog,
  InProcMaterial,
  SortableInProcMaterial,
  DragOverlayInProcMaterial,
  MatSummary,
  MatCardFontSize,
} from "./Material.js";
import { SortableRegion } from "./Whiteboard.js";
import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "./SelectWorkorder.js";
import { SelectInspTypeDialog, SignalInspectionButton } from "./SelectInspType.js";
import {
  MoveMaterialArrowContainer,
  MoveMaterialArrowNode,
  useMoveMaterialArrowRef,
} from "./MoveMaterialArrows.js";
import { MoveMaterialNodeKindType } from "../../data/move-arrows.js";
import { currentOperator } from "../../data/operators.js";
import { instructionUrl } from "../../network/backend.js";
import { Tooltip } from "@mui/material";
import { Fab } from "@mui/material";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus, secondsSinceEpochAtom } from "../../cell-status/current-status.js";
import { useIsDemo, useSetTitle } from "../routes.js";
import { PrintOnClientButton } from "./QueuesMatDialog.js";
import { QuarantineMatButton } from "./QuarantineButton.js";
import { durationToSeconds } from "../../util/parseISODuration.js";
import { formatSeconds } from "./SystemOverview.js";
import {
  InvalidateCycleDialogButton,
  InvalidateCycleDialogContent,
  InvalidateCycleState,
  SwapMaterialButtons,
  SwapMaterialDialogContent,
  SwapMaterialState,
} from "./InvalidateCycle.js";
import { last30MaterialSummary } from "../../cell-status/material-summary.js";
import { addHours } from "date-fns";
import {
  AddToQueueButton,
  AddToQueueMaterialDialogCt,
  NewMaterialToQueueType,
  useMaterialInDialogAddType,
} from "./QueuesAddMaterial.js";
import { useAtomValue, useSetAtom } from "jotai";

type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

type LoadStationData = {
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly face: OrderedMap<number, MaterialList>;
  readonly freeLoadingMaterial: MaterialList;
  readonly queues: ReadonlyMap<string, MaterialList>;
  readonly elapsedLoadingTime: string | null;
  readonly fsize: MatCardFontSize;
};

function selectLoadStationAndQueueProps(
  loadNum: number,
  queues: ReadonlyArray<string>,
  curSt: Readonly<api.ICurrentStatus>,
): LoadStationData {
  // search for pallet
  let pal: Readonly<api.IPalletStatus> | undefined;
  if (loadNum >= 0) {
    for (const p of Object.values(curSt.pallets)) {
      if (
        p.currentPalletLocation.loc === api.PalletLocationEnum.LoadUnload &&
        p.currentPalletLocation.num === loadNum
      ) {
        pal = p;
        break;
      }
    }
  }

  // calculate queues to show, which is all configured queues and any queues with
  // material loading onto the pallet
  const queuesToShow = new Set(queues);
  if (pal !== undefined) {
    for (const m of curSt.material) {
      if (
        m.action.type === api.ActionType.Loading &&
        m.action.loadOntoPalletNum === pal.palletNum &&
        m.location.type === api.LocType.InQueue &&
        m.location.currentQueue
      ) {
        queuesToShow.add(m.location.currentQueue);
      }

      if (
        (m.action.type === api.ActionType.UnloadToInProcess ||
          m.action.type === api.ActionType.UnloadToCompletedMaterial) &&
        m.action.unloadIntoQueue &&
        m.location.type === api.LocType.OnPallet &&
        m.location.palletNum === pal.palletNum
      ) {
        queuesToShow.add(m.action.unloadIntoQueue);
      }
    }
  }

  const queueMat = new Map<string, Array<api.IInProcessMaterial>>(
    LazySeq.of(queuesToShow).map((q) => [q, []]),
  );
  const freeLoading: Array<Readonly<api.IInProcessMaterial>> = [];
  let palFaces = OrderedMap.empty<number, Array<api.IInProcessMaterial>>();
  let elapsedLoadingTime: string | null = null;

  // ensure all faces
  if (pal) {
    for (let i = 1; i <= pal.numFaces; i++) {
      palFaces = palFaces.set(i, []);
    }
  }

  for (const m of curSt.material) {
    if (pal) {
      // if loading onto pallet, set elapsed load time, ensure face exists, and set free loading
      if (m.action.type === api.ActionType.Loading && m.action.loadOntoPalletNum === pal.palletNum) {
        if (m.action.elapsedLoadUnloadTime) {
          elapsedLoadingTime = m.action.elapsedLoadUnloadTime;
        }

        if (m.action.loadOntoFace && !palFaces.has(m.action.loadOntoFace)) {
          palFaces = palFaces.set(m.action.loadOntoFace, []);
        }

        if (m.location.type === api.LocType.Free) {
          freeLoading.push(m);
        }
      }

      // if currently on the pallet, set elapsed load time and add it to the pallet face
      if (m.location.type === api.LocType.OnPallet && m.location.palletNum === pal.palletNum) {
        if (
          (m.action.type === api.ActionType.UnloadToCompletedMaterial ||
            m.action.type === api.ActionType.UnloadToInProcess) &&
          m.action.elapsedLoadUnloadTime
        ) {
          elapsedLoadingTime = m.action.elapsedLoadUnloadTime;
        }

        palFaces = palFaces.alter(m.location.face ?? 1, (oldMats) => {
          if (oldMats) {
            oldMats.push(m);
            return oldMats;
          } else {
            return [m];
          }
        });
      }
    }

    // add all material in the configured queues
    if (
      m.location.type === api.LocType.InQueue &&
      m.location.currentQueue &&
      queueMat.has(m.location.currentQueue)
    ) {
      const old = queueMat.get(m.location.currentQueue);
      if (old === undefined) {
        queueMat.set(m.location.currentQueue, [m]);
      } else {
        old.push(m);
      }
    }
  }

  queueMat.forEach((mats) => mats.sort(mkCompareByProperties((m) => m.location.queuePosition ?? 0)));

  const matCount = freeLoading.length + palFaces.valuesToAscLazySeq().sumBy((x) => x.length);

  return {
    pallet: pal,
    face: palFaces,
    freeLoadingMaterial: freeLoading,
    queues: queueMat,
    elapsedLoadingTime,
    fsize: matCount <= 2 ? "x-large" : matCount <= 6 ? "large" : "normal",
  };
}

function MultiInstructionButton({ loadData }: { loadData: LoadStationData }) {
  const isDemo = useIsDemo();
  const operator = useAtomValue(currentOperator);
  const urls = useMemo(() => {
    const pal = loadData.pallet;
    if (pal) {
      return LazySeq.of(loadData.face.values())
        .append(loadData.freeLoadingMaterial)
        .concat(loadData.queues.values())
        .flatMap((x) => x)
        .collect((mat) => {
          if (
            mat.action.type === api.ActionType.Loading &&
            mat.action.loadOntoPalletNum === pal.palletNum &&
            mat.location.type === api.LocType.OnPallet &&
            mat.location.palletNum === pal.palletNum
          ) {
            // transfer, but use unload type
            return instructionUrl(
              mat.partName,
              "unload",
              mat.materialID,
              pal.palletNum,
              mat.process,
              operator,
            );
          } else if (
            mat.action.type === api.ActionType.Loading &&
            mat.action.loadOntoPalletNum === pal.palletNum
          ) {
            return instructionUrl(
              mat.partName,
              "load",
              mat.materialID,
              pal.palletNum,
              mat.action.processAfterLoad ?? mat.process,
              operator,
            );
          } else if (
            mat.location.type === api.LocType.OnPallet &&
            mat.location.palletNum === pal.palletNum &&
            (mat.action.type === api.ActionType.UnloadToCompletedMaterial ||
              mat.action.type === api.ActionType.UnloadToInProcess)
          ) {
            return instructionUrl(
              mat.partName,
              "unload",
              mat.materialID,
              pal.palletNum,
              mat.process,
              operator,
            );
          } else {
            return null;
          }
        })
        .toRArray();
    } else {
      return [];
    }
  }, [loadData, operator]);

  if (urls.length === 0 || isDemo) {
    return <div />;
  }

  function open() {
    for (const url of urls) {
      window.open(url, "_blank");
    }
  }

  return (
    <Box
      sx={{
        position: "fixed",
        bottom: "5px",
        right: "5px",
      }}
    >
      <Tooltip title="Open All Instructions">
        <Fab onClick={open} color="secondary">
          <FolderOpenIcon />
        </Fab>
      </Tooltip>
    </Box>
  );
}

function ElapsedLoadTime({ elapsedLoadTime }: { elapsedLoadTime: string | null }) {
  const currentStTime = useAtomValue(currentStatus).timeOfCurrentStatusUTC;
  const secondsSinceEpoch = useAtomValue(secondsSinceEpochAtom);

  if (elapsedLoadTime) {
    const elapsedSecsInCurSt = durationToSeconds(elapsedLoadTime);
    const elapsedSecs = elapsedSecsInCurSt + (secondsSinceEpoch - Math.floor(currentStTime.getTime() / 1000));
    return <span>{formatSeconds(elapsedSecs)}</span>;
  } else {
    return null;
  }
}

function PalletFace({ data, faceNum }: { data: LoadStationData; faceNum: number }) {
  const face = data.face.get(faceNum);
  if (!face || !data.pallet) return null;

  return (
    <div>
      {faceNum === 1 ? (
        <Box display="grid" gridTemplateColumns={data.pallet.numFaces > 1 ? "1fr 1fr 1fr" : "1fr 1fr"}>
          <Typography variant="h4">Pallet {data.pallet.palletNum}</Typography>
          {data.pallet.numFaces > 1 ? (
            <Typography variant="h6" justifySelf="center">
              {data.pallet.faceNames?.[faceNum - 1] ?? "Face 1"}
            </Typography>
          ) : undefined}
          <Box justifySelf="flex-end">
            <ElapsedLoadTime elapsedLoadTime={data.elapsedLoadingTime} />
          </Box>
        </Box>
      ) : data.pallet.numFaces > 1 ? (
        <Box display="flex" justifyContent="center">
          <Typography variant="h6">{data.pallet.faceNames?.[faceNum - 1] ?? `Face ${faceNum}`}</Typography>
        </Box>
      ) : undefined}
      <Box ml="4em" mr="4em">
        <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.PalletFaceZone, face: faceNum }}>
          <Box display="flex" flexWrap="wrap" justifyContent="space-around">
            {face.map((m, idx) => (
              <MoveMaterialArrowNode
                key={idx}
                kind={{ type: MoveMaterialNodeKindType.Material, material: m }}
              >
                <InProcMaterial mat={m} fsize={data.fsize} />
              </MoveMaterialArrowNode>
            ))}
          </Box>
        </MoveMaterialArrowNode>
      </Box>
    </div>
  );
}

function MaterialRegion({
  data,
  mat,
}: {
  readonly data: LoadStationData;
  mat: { readonly label: string; readonly material: MaterialList; readonly isFree: boolean };
}) {
  return (
    <div>
      <Typography variant="h4">{mat.label}</Typography>
      {mat.material.map((m, matIdx) => (
        <MoveMaterialArrowNode
          key={matIdx}
          kind={{
            type: MoveMaterialNodeKindType.Material,
            material: data.pallet && m.action.loadOntoPalletNum === data.pallet.palletNum ? m : null,
          }}
        >
          {mat.isFree ? (
            <InProcMaterial
              mat={m}
              displayActionForSinglePallet={data.pallet ? data.pallet.palletNum : 0}
              fsize={data.fsize}
              showRawMaterial
            />
          ) : (
            <SortableInProcMaterial
              mat={m}
              displayActionForSinglePallet={data.pallet ? data.pallet.palletNum : 0}
              shake={
                m.action.type === api.ActionType.Loading &&
                m.action.loadOntoPalletNum === data.pallet?.palletNum
              }
              fsize={
                m.action.type === api.ActionType.Loading &&
                m.action.loadOntoPalletNum === data.pallet?.palletNum
                  ? data.fsize
                  : undefined
              }
              showRawMaterial
            />
          )}
        </MoveMaterialArrowNode>
      ))}
    </div>
  );
}

function MaterialColumn({
  data,
  region,
}: {
  readonly data: LoadStationData;
  region: { readonly label: string; readonly material: MaterialList; readonly isFree: boolean };
}) {
  return (
    <MoveMaterialArrowNode
      kind={
        region.isFree
          ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
          : {
              type: MoveMaterialNodeKindType.QueueZone,
              queue: region.label,
            }
      }
    >
      {region.isFree ? (
        <MaterialRegion data={data} mat={region} />
      ) : (
        <SortableRegion
          matIds={region.material.map((m) => m.materialID)}
          direction="vertical"
          queueName={region.label}
          renderDragOverlay={(mat) => (
            <DragOverlayInProcMaterial
              mat={mat}
              displayActionForSinglePallet={data.pallet ? data.pallet.palletNum : 0}
              fsize={
                mat.action.type === api.ActionType.Loading &&
                mat.action.loadOntoPalletNum === data.pallet?.palletNum
                  ? data.fsize
                  : undefined
              }
            />
          )}
        >
          <MaterialRegion data={data} mat={region} />
        </SortableRegion>
      )}
    </MoveMaterialArrowNode>
  );
}

function RecentCompletedMaterial() {
  const matSummary = useAtomValue(last30MaterialSummary);
  const recentCompleted = useMemo(() => {
    const cutoff = addHours(new Date(), -5);
    return matSummary.matsById
      .valuesToLazySeq()
      .filter(
        (e) =>
          e.completed_last_proc_machining === true &&
          e.last_unload_time !== undefined &&
          e.last_unload_time >= cutoff,
      )
      .toSortedArray({ desc: (e) => e.last_unload_time?.getTime() ?? 0 });
  }, [matSummary]);

  return (
    <div>
      {recentCompleted.map((m, idx) => (
        <MatSummary key={idx} mat={m} />
      ))}
    </div>
  );
}

const CompletedCol = memo(function CompletedCol({
  fillViewPort,
  showMaterial,
}: {
  fillViewPort: boolean;
  showMaterial: boolean;
}) {
  const ref = useMoveMaterialArrowRef({
    type: showMaterial
      ? MoveMaterialNodeKindType.CompletedExpandedMaterialZone
      : MoveMaterialNodeKindType.CompletedCollapsedMaterialZone,
  });
  if (showMaterial && fillViewPort) {
    return (
      <Box padding="8px" display="flex" flexDirection="column" height="100%" ref={ref}>
        <Typography variant="h4">Completed</Typography>
        <Box position="relative" flexGrow={1}>
          <Box position="absolute" top="0" left="0" right="0" bottom="0" overflow="auto">
            <RecentCompletedMaterial />
          </Box>
        </Box>
      </Box>
    );
  } else if (showMaterial) {
    return (
      <Box padding="8px" ref={ref}>
        <Typography variant="h4">Completed</Typography>
        <RecentCompletedMaterial />
      </Box>
    );
  } else {
    return (
      <Box display="flex" justifyContent={fillViewPort ? "flex-end" : undefined} padding="8px" ref={ref}>
        <Typography
          variant="h4"
          margin="4px"
          sx={fillViewPort ? { textOrientation: "mixed", writingMode: "vertical-rl" } : undefined}
        >
          Completed
        </Typography>
      </Box>
    );
  }
});

interface LoadMatDialogProps {
  readonly pallet: number | null;
  readonly queues: ReadonlyArray<string>;
}

function InstructionButton({ pallet }: { pallet: number | null }) {
  const material = useAtomValue(matDetails.inProcessMaterialInDialog);
  const operator = useAtomValue(currentOperator);
  const demo = useIsDemo();

  if (material === null) return null;

  let type: string | undefined;
  if (material.action.type === api.ActionType.Loading && material.action.loadOntoPalletNum === pallet) {
    type = "load";
  } else if (
    (material.action.type === api.ActionType.UnloadToInProcess ||
      material.action.type === api.ActionType.UnloadToCompletedMaterial) &&
    material.location.type === api.LocType.OnPallet &&
    material.location.palletNum === pallet
  ) {
    type = "unload";
  }

  if (type === undefined) return null;

  const url = instructionUrl(
    material.partName,
    type,
    material.materialID,
    pallet,
    material.action.type === api.ActionType.Loading
      ? material.action.processAfterLoad ?? material.process
      : material.process,
    operator,
  );

  if (demo) {
    return <Button color="primary">Instructions</Button>;
  } else {
    return (
      <Button href={url} target="bms-instructions" color="primary">
        Instructions
      </Button>
    );
  }
}

function PrintSerialButton() {
  const fmsInfo = useAtomValue(fmsInformation);
  const curMat = useAtomValue(matDetails.inProcessMaterialInDialog);
  const [printLabel, printingLabel] = matDetails.usePrintLabel();

  if (curMat === null || !fmsInfo.usingLabelPrinterForSerials || curMat.materialID < 0) return null;

  if (fmsInfo.useClientPrinterForLabels) {
    return <PrintOnClientButton mat={curMat} />;
  } else {
    return (
      <Button
        color="primary"
        disabled={printingLabel}
        onClick={() =>
          printLabel({
            materialId: curMat.materialID,
            proc: curMat.process,
          })
        }
      >
        Print Label
      </Button>
    );
  }
}

function useAllowAddMaterial(queues: ReadonlyArray<string>): boolean {
  const kind = useMaterialInDialogAddType(queues);
  const mat = useAtomValue(matDetails.materialInDialogInfo);
  const barcode = useAtomValue(matDetails.barcodeMaterialDetail);
  switch (kind) {
    case "None":
    case "MatInQueue":
      return false;

    case "AddToQueue":
      // add an additional restriction that the material must already exist or
      // come from a barcode.  It can not be added manually (use the queues page for that).
      return mat !== null || !!barcode;
  }
}

function AddMatButton({
  queues,
  onClose,
  newMaterialTy,
  enteredOperator,
  showAddToQueue,
  toQueue,
  setShowAddToQueue,
}: {
  queues: ReadonlyArray<string>;
  onClose: () => void;
  newMaterialTy: NewMaterialToQueueType | null;
  enteredOperator: string | null;
  toQueue: string | null;
  showAddToQueue: boolean;
  setShowAddToQueue: (show: boolean) => void;
}) {
  const allow = useAllowAddMaterial(queues);
  if (!allow) return null;
  if (queues.length === 0) return null;

  if (!showAddToQueue) {
    return (
      <Button color="primary" onClick={() => setShowAddToQueue(true)}>
        Add To Queue
      </Button>
    );
  } else {
    return (
      <AddToQueueButton
        enteredOperator={enteredOperator}
        newMaterialTy={newMaterialTy}
        toQueue={toQueue}
        onClose={onClose}
      />
    );
  }
}

function AssignWorkorderButton() {
  const fmsInfo = useAtomValue(fmsInformation);
  const setWorkorderDialogOpen = useSetAtom(selectWorkorderDialogOpen);
  const mat = useAtomValue(matDetails.materialInDialogInfo);

  if (!fmsInfo.allowChangeWorkorderAtLoadStation) {
    return null;
  }

  if (mat === null || mat.materialID < 0) {
    return null;
  }

  return (
    <Button color="primary" onClick={() => setWorkorderDialogOpen(true)}>
      Assign Workorder
    </Button>
  );
}

const LoadMatDialog = memo(function LoadMatDialog(props: LoadMatDialogProps) {
  const [swapSt, setSwapSt] = useState<SwapMaterialState>(null);
  const [invalidateSt, setInvalidateSt] = useState<InvalidateCycleState | null>(null);

  // add material state
  const [showAddMaterial, setShowAddMaterial] = useState<boolean>(false);
  const [selectedQueue, setSelectedQueue] = useState<string | null>(null);
  const [enteredOperator, setEnteredOperator] = useState<string | null>(null);
  const [newMaterialTy, setNewMaterialTy] = useState<NewMaterialToQueueType | null>(null);

  const toQueue = props.queues.length === 1 ? props.queues[0] : selectedQueue;

  const onClose = useCallback(
    function onClose() {
      setSwapSt(null);
      setInvalidateSt(null);
    },
    [setSwapSt, setInvalidateSt],
  );

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
          {showAddMaterial ? (
            <AddToQueueMaterialDialogCt
              enteredOperator={enteredOperator}
              setEnteredOperator={setEnteredOperator}
              selectedQueue={selectedQueue}
              setSelectedQueue={setSelectedQueue}
              newMaterialTy={newMaterialTy}
              setNewMaterialTy={setNewMaterialTy}
              queueNames={props.queues}
              toQueue={toQueue}
            />
          ) : undefined}
        </>
      }
      buttons={
        <>
          <InstructionButton pallet={props.pallet} />
          <PrintSerialButton />
          <QuarantineMatButton />
          <SignalInspectionButton />
          <AddMatButton
            toQueue={toQueue}
            queues={props.queues}
            onClose={onClose}
            enteredOperator={enteredOperator}
            newMaterialTy={newMaterialTy}
            showAddToQueue={showAddMaterial}
            setShowAddToQueue={setShowAddMaterial}
          />
          <SwapMaterialButtons st={swapSt} setState={setSwapSt} onClose={onClose} />
          <InvalidateCycleDialogButton
            st={invalidateSt}
            setState={setInvalidateSt}
            onClose={onClose}
            loadStation
          />
          <AssignWorkorderButton />
        </>
      }
    />
  );
});

interface LoadStationProps {
  readonly loadNum: number;
  readonly queues: ReadonlyArray<string>;
  readonly completed: boolean;
  readonly whiteBackground?: boolean;
}

function useGridLayout({
  numMatCols,
  maxNumFaces,
  horizontal,
  showMatInCompleted,
}: {
  numMatCols: number;
  maxNumFaces: number;
  horizontal: boolean;
  showMatInCompleted: boolean;
}): string {
  let rows = "";
  let cols = "";
  if (maxNumFaces <= 0) maxNumFaces = 1;

  if (horizontal) {
    let colNames = "";
    for (let i = 0; i < numMatCols; i++) {
      cols += "minmax(16em, max-content) ";
      colNames += `mat${i} `;
    }
    cols += "1fr ";
    if (showMatInCompleted) {
      cols += "minmax(16em, max-content)";
    } else {
      cols += " 4.5em";
    }

    for (let i = 0; i < maxNumFaces; i++) {
      rows += `"${colNames} palface${i} completed" 1fr\n`;
    }
  } else {
    cols = "1fr";
    for (let i = 0; i < numMatCols; i++) {
      rows += `"mat${i}" minmax(134px, max-content)\n`;
    }
    for (let i = 0; i < maxNumFaces; i++) {
      rows += `"palface${i}" minmax(134px, max-content)\n`;
    }
    rows += '"completed" minmax(134px, max-content)\n';
  }

  return `${rows} / ${cols}`;
}

export function LoadStation(props: LoadStationProps) {
  const currentSt = useAtomValue(currentStatus);
  const data = useMemo(
    () => selectLoadStationAndQueueProps(props.loadNum, props.queues, currentSt),
    [currentSt, props.loadNum, props.queues],
  );

  let matColsSeq = LazySeq.of(data.queues)
    .sortBy(([q, _]) => q)
    .map(([q, mats]) => ({
      label: q,
      material: mats,
      isFree: false,
    }));

  if (data.queues.size === 0 || data.freeLoadingMaterial.length > 0) {
    matColsSeq = matColsSeq.append({
      label: "Material",
      material: data.freeLoadingMaterial,
      isFree: true,
    });
  }
  const matCols = matColsSeq.toRArray();
  const numNonPalCols = matCols.length + (props.completed ? 1 : 0);

  const fillViewPort = useMediaQuery(
    numNonPalCols <= 1
      ? "(min-width: 720px)"
      : numNonPalCols === 2
        ? "(min-width: 1030px)"
        : "(min-width: 1320px)",
  );

  const grid = useGridLayout({
    numMatCols: matCols.length,
    maxNumFaces: data.face.size,
    horizontal: fillViewPort,
    showMatInCompleted: props.completed,
  });

  return (
    <MoveMaterialArrowContainer hideArrows={!fillViewPort}>
      <Box
        component="main"
        sx={{
          width: "100%",
          bgcolor: props.whiteBackground ? "white" : "#F8F8F8",
          display: "grid",
          gridTemplate: grid,
          minHeight: {
            xs: "calc(100vh - 64px - 32px)",
            sm: "calc(100vh - 64px - 40px)",
            md: "calc(100vh - 64px)",
          },
          padding: fillViewPort ? undefined : "8px",
        }}
      >
        {matCols.map((col, idx) => (
          <Box
            key={idx}
            gridArea={`mat${idx}`}
            padding="8px"
            borderRight={fillViewPort ? "1px solid black" : undefined}
            borderBottom={!fillViewPort ? "1px solid black" : undefined}
          >
            <MaterialColumn data={data} region={col} />
          </Box>
        ))}
        {data.face.keysToAscLazySeq().map((faceNum, idx) => (
          <Box
            key={faceNum}
            marginLeft="15px"
            gridArea={`palface${idx}`}
            padding="8px"
            borderTop={data.pallet && idx !== 0 ? "1px solid black" : undefined}
          >
            <PalletFace data={data} faceNum={faceNum} />
          </Box>
        ))}
        <Box
          borderLeft={fillViewPort ? "1px solid black" : undefined}
          borderTop={!fillViewPort ? "1px solid black" : undefined}
          gridArea="completed"
        >
          <CompletedCol fillViewPort={fillViewPort} showMaterial={props.completed} />
        </Box>
        <MultiInstructionButton loadData={data} />
        <SelectWorkorderDialog />
        <SelectInspTypeDialog />
        <LoadMatDialog pallet={data.pallet?.palletNum ?? null} queues={props.queues} />
      </Box>
    </MoveMaterialArrowContainer>
  );
}

function LoadStationCheckWidth(props: LoadStationProps): JSX.Element {
  useSetTitle("Load " + props.loadNum.toString());
  return <LoadStation {...props} />;
}

export default LoadStationCheckWidth;
