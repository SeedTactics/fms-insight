/* Copyright (c) 2025, John Lenz

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

import { useMemo, memo, useState, useCallback, ReactNode } from "react";
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
import { PromptForQueue } from "./QueuesAddMaterial.js";
import { useAtomValue, useSetAtom } from "jotai";
import { PrintLabelButton } from "./PrintedLabel.js";
import { hideNonLoadingMaterialOnLoadStation } from "../../data/queue-material.js";
import { basketDisplayName, loadStationDisplayName } from "../../cell-status/station-cycles.js";
import {
  BasketLoadStationWorkflow,
  SubmitBasketLoadStationCommand,
} from "./BasketLoadStationWork.js";

type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

type LoadStationData = {
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly activeBasket?: Readonly<api.IBasketStatus>;
  readonly allMaterial: MaterialList;
  readonly face: OrderedMap<number, MaterialList>;
  readonly freeLoadingMaterial: MaterialList;
  readonly queues: ReadonlyMap<string, { readonly mats: MaterialList; readonly hiddenCnt: number }>;
  readonly baskets: ReadonlyMap<
    number,
    { readonly mats: MaterialList; readonly hiddenCnt: number }
  >;
  readonly elapsedLoadingTime: string | null;
  readonly fsize: MatCardFontSize;
};

type RegionMaterial = {
  readonly mats: MaterialList;
  readonly hiddenCnt: number;
};

type QueueMaterialRegion = {
  readonly kind: "queue";
  readonly label: string;
  readonly material: RegionMaterial;
};

type BasketMaterialRegion = {
  readonly kind: "baskets";
  readonly label: string;
  readonly baskets: ReadonlyArray<{
    readonly basketId: number;
    readonly label: string;
    readonly material: RegionMaterial;
  }>;
};

type FreeMaterialRegion = {
  readonly kind: "free";
  readonly label: string;
  readonly material: RegionMaterial;
};

type MaterialColumnRegion = QueueMaterialRegion | BasketMaterialRegion | FreeMaterialRegion;

function encodeLoadStationRegionIdPart(value: string | number): string {
  return encodeURIComponent(value.toString());
}

function materialColumnTestId(region: MaterialColumnRegion): string {
  switch (region.kind) {
    case "queue":
      return `load-station-queue-${encodeLoadStationRegionIdPart(region.label)}`;
    case "baskets":
      return "load-station-baskets";
    case "free":
      return "load-station-material";
  }
}

function isLoadingToDisplayedTarget(
  mat: Readonly<api.IInProcessMaterial>,
  pallet: Readonly<api.IPalletStatus> | undefined,
  activeBasket: Readonly<api.IBasketStatus> | undefined,
): boolean {
  return (
    (mat.action.type === api.ActionType.Loading ||
      mat.action.type === api.ActionType.LoadingToBasket) &&
    ((pallet !== undefined && mat.action.loadOntoPalletNum === pallet.palletNum) ||
      (activeBasket !== undefined && mat.action.loadToBasketId === activeBasket.basketId))
  );
}

function selectLoadStationAndQueueProps(
  loadNum: number,
  queues: ReadonlyArray<string>,
  curSt: Readonly<api.ICurrentStatus>,
  hideNonLoading: boolean,
): LoadStationData {
  // search for pallet
  let pal: Readonly<api.IPalletStatus> | undefined;
  let activeBasket: Readonly<api.IBasketStatus> | undefined;
  const basketsToShow = new Set<number>();
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

    for (const basket of Object.values(curSt.baskets ?? {})) {
      if (basket.position.locationNum !== loadNum) continue;

      if (basket.position.location === api.BasketLocationEnum.LoadUnload) {
        activeBasket = basket;
      } else if (basket.position.location === api.BasketLocationEnum.LoadStationStaging) {
        basketsToShow.add(basket.basketId);
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

      // Loading from basket
      if (
        m.action.type === api.ActionType.Loading &&
        m.action.loadOntoPalletNum === pal.palletNum &&
        m.action.loadFromBasketId
      ) {
        basketsToShow.add(m.action.loadFromBasketId);
      }

      // Unloading to basket
      if (
        (m.action.type === api.ActionType.UnloadToInProcess ||
          m.action.type === api.ActionType.UnloadToCompletedMaterial) &&
        m.action.unloadToBasketId &&
        m.location.type === api.LocType.OnPallet &&
        m.location.palletNum === pal.palletNum
      ) {
        basketsToShow.add(m.action.unloadToBasketId);
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
  } else if (activeBasket !== undefined) {
    for (const m of curSt.material) {
      if (
        m.location.type === api.LocType.InBasket &&
        m.location.basketId === activeBasket.basketId &&
        (m.action.type === api.ActionType.UnloadToInProcess ||
          m.action.type === api.ActionType.UnloadToCompletedMaterial) &&
        m.action.unloadIntoQueue
      ) {
        queuesToShow.add(m.action.unloadIntoQueue);
      }

      if (
        m.location.type === api.LocType.InQueue &&
        m.location.currentQueue &&
        m.action.type === api.ActionType.LoadingToBasket &&
        m.action.loadToBasketId === activeBasket.basketId
      ) {
        queuesToShow.add(m.location.currentQueue);
      }
    }
  }

  const queueMat = new Map<
    string,
    { readonly mats: Array<api.IInProcessMaterial>; hiddenCnt: number }
  >(LazySeq.of(queuesToShow).map((q) => [q, { mats: [], hiddenCnt: 0 }]));
  const basketMat = new Map<
    number,
    { readonly mats: Array<api.IInProcessMaterial>; hiddenCnt: number }
  >(LazySeq.of(basketsToShow).map((b) => [b, { mats: [], hiddenCnt: 0 }]));
  const freeLoading: Array<Readonly<api.IInProcessMaterial>> = [];
  let palFaces = OrderedMap.empty<number, Array<api.IInProcessMaterial>>();
  let elapsedLoadingTime: string | null = null;

  // ensure all faces
  if (pal) {
    for (let i = 1; i <= pal.numFaces; i++) {
      palFaces = palFaces.set(i, []);
    }
  } else if (activeBasket) {
    palFaces = palFaces.set(1, []);
  }

  for (const m of curSt.material) {
    if (pal) {
      // if loading onto pallet, set elapsed load time, ensure face exists, and set free loading
      if (
        m.action.type === api.ActionType.Loading &&
        m.action.loadOntoPalletNum === pal.palletNum
      ) {
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
    } else if (activeBasket) {
      if (isLoadingToDisplayedTarget(m, pal, activeBasket) && m.action.elapsedLoadUnloadTime) {
        elapsedLoadingTime = m.action.elapsedLoadUnloadTime;
      }

      if (
        m.action.type === api.ActionType.LoadingToBasket &&
        m.action.loadToBasketId === activeBasket.basketId &&
        m.location.type === api.LocType.Free
      ) {
        freeLoading.push(m);
      }

      if (
        m.location.type === api.LocType.InBasket &&
        m.location.basketId === activeBasket.basketId
      ) {
        if (
          (m.action.type === api.ActionType.UnloadToCompletedMaterial ||
            m.action.type === api.ActionType.UnloadToInProcess) &&
          m.action.elapsedLoadUnloadTime
        ) {
          elapsedLoadingTime = m.action.elapsedLoadUnloadTime;
        }

        palFaces = palFaces.alter(1, (oldMats) => {
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
      if (hideNonLoading && !isLoadingToDisplayedTarget(m, pal, activeBasket)) {
        if (old === undefined) {
          queueMat.set(m.location.currentQueue, { mats: [], hiddenCnt: 1 });
        } else {
          old.hiddenCnt += 1;
        }
      } else {
        if (old === undefined) {
          queueMat.set(m.location.currentQueue, { mats: [m], hiddenCnt: 0 });
        } else {
          old.mats.push(m);
        }
      }
    }

    // add all material in baskets
    if (
      m.location.type === api.LocType.InBasket &&
      m.location.basketId &&
      basketMat.has(m.location.basketId)
    ) {
      const old = basketMat.get(m.location.basketId);
      if (hideNonLoading && !isLoadingToDisplayedTarget(m, pal, activeBasket)) {
        if (old === undefined) {
          basketMat.set(m.location.basketId, { mats: [], hiddenCnt: 1 });
        } else {
          old.hiddenCnt += 1;
        }
      } else {
        if (old === undefined) {
          basketMat.set(m.location.basketId, { mats: [m], hiddenCnt: 0 });
        } else {
          old.mats.push(m);
        }
      }
    }
  }

  queueMat.forEach((queue) =>
    queue.mats.sort(mkCompareByProperties((mat) => mat.location.queuePosition ?? 0)),
  );
  basketMat.forEach((basket) =>
    basket.mats.sort(mkCompareByProperties((mat) => mat.location.basketSlot ?? 0)),
  );

  const matCount = freeLoading.length + palFaces.valuesToAscLazySeq().sumBy((x) => x.length);
  return {
    pallet: pal,
    activeBasket,
    allMaterial: curSt.material,
    face: palFaces,
    freeLoadingMaterial: freeLoading,
    queues: queueMat,
    baskets: basketMat,
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
        .concat(LazySeq.of(loadData.queues).collect(([_, v]) => v.mats))
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
    const elapsedSecs =
      elapsedSecsInCurSt + (secondsSinceEpoch - Math.floor(currentStTime.getTime() / 1000));
    return <span>{formatSeconds(elapsedSecs)}</span>;
  } else {
    return null;
  }
}

function PalletFace({
  data,
  faceNum,
  loadNum,
  submitBasketLoadStationCommand,
}: {
  data: LoadStationData;
  faceNum: number;
  loadNum: number;
  submitBasketLoadStationCommand: SubmitBasketLoadStationCommand | undefined;
}) {
  const face = data.face.get(faceNum);
  const fmsInfo = useAtomValue(fmsInformation);
  const basketName = basketDisplayName(fmsInfo.basketName);
  if (!face) return null;

  if (data.activeBasket && !data.pallet) {
    return (
      <div data-testid="load-station-active-basket">
        {faceNum === 1 ? (
          <Box
            sx={{
              display: "grid",
              gridTemplateColumns: "1fr auto",
            }}
          >
            <Typography variant="h4">
              {basketName} {data.activeBasket.basketId}
            </Typography>
            <Box
              sx={{
                justifySelf: "flex-end",
              }}
            >
              <ElapsedLoadTime elapsedLoadTime={data.elapsedLoadingTime} />
            </Box>
          </Box>
        ) : null}
        <Box sx={{ ml: "4em", mr: "4em" }}>
          <MoveMaterialArrowNode
            kind={{
              type: MoveMaterialNodeKindType.BasketZone,
              basketId: data.activeBasket.basketId,
            }}
          >
            <BasketLoadStationWorkflow
              stationNumber={loadNum}
              basket={data.activeBasket}
              material={data.allMaterial}
              fsize={data.fsize}
              submitCommand={submitBasketLoadStationCommand}
            />
          </MoveMaterialArrowNode>
        </Box>
      </div>
    );
  }

  if (!data.pallet) return null;

  return (
    <div data-testid={`load-station-pallet-face-${faceNum}`}>
      {faceNum === 1 ? (
        <Box
          sx={{
            display: "grid",
            gridTemplateColumns: data.pallet.numFaces > 1 ? "1fr 1fr 1fr" : "1fr 1fr",
          }}
        >
          <Typography variant="h4">Pallet {data.pallet.palletNum}</Typography>
          {data.pallet.numFaces > 1 ? (
            <Typography
              variant="h6"
              sx={{
                justifySelf: "center",
              }}
            >
              {data.pallet.faceNames?.[faceNum - 1] ?? "Face 1"}
            </Typography>
          ) : undefined}
          <Box
            sx={{
              justifySelf: "flex-end",
            }}
          >
            <ElapsedLoadTime elapsedLoadTime={data.elapsedLoadingTime} />
          </Box>
        </Box>
      ) : data.pallet.numFaces > 1 ? (
        <Box
          sx={{
            display: "flex",
            justifyContent: "center",
          }}
        >
          <Typography variant="h6">
            {data.pallet.faceNames?.[faceNum - 1] ?? `Face ${faceNum}`}
          </Typography>
        </Box>
      ) : undefined}
      <Box
        sx={{
          ml: "4em",
          mr: "4em",
        }}
      >
        <MoveMaterialArrowNode
          kind={{ type: MoveMaterialNodeKindType.PalletFaceZone, face: faceNum }}
        >
          <Box
            sx={{
              display: "flex",
              flexWrap: "wrap",
              justifyContent: "space-around",
            }}
          >
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
  mat: {
    readonly label: string;
    readonly material: { readonly mats: MaterialList; readonly hiddenCnt: number };
    readonly isFree: boolean;
    readonly sortable?: boolean;
  };
}) {
  return (
    <div>
      {mat.label !== "" ? <Typography variant="h4">{mat.label}</Typography> : null}
      {mat.material.mats.map((m, matIdx) => (
        <MoveMaterialArrowNode
          key={matIdx}
          kind={{
            type: MoveMaterialNodeKindType.Material,
            material: m,
            identifier:
              m.materialID < 0
                ? `raw-${mat.isFree ? "free" : "queue"}-${mat.label}-${matIdx}`
                : undefined,
          }}
        >
          {mat.isFree || mat.sortable === false ? (
            <InProcMaterial
              mat={m}
              displayActionForSinglePallet={data.pallet ? data.pallet.palletNum : 0}
              fsize={data.fsize}
              showRawMaterial
              showJobComment
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
              showJobComment
            />
          )}
        </MoveMaterialArrowNode>
      ))}
      {mat.material.hiddenCnt > 0 ? (
        <Typography
          variant="body2"
          color="textSecondary"
          sx={{
            textAlign: "center",
            mt: "0.5em",
          }}
        >
          +{mat.material.hiddenCnt} hidden
        </Typography>
      ) : null}
    </div>
  );
}

function BasketRegion({
  data,
  label,
  baskets,
}: {
  readonly data: LoadStationData;
  readonly label: string;
  readonly baskets: ReadonlyArray<{
    readonly basketId: number;
    readonly label: string;
    readonly material: RegionMaterial;
  }>;
}) {
  return (
    <div>
      <Typography variant="h4">{label}</Typography>
      {baskets.map((basket, idx) => (
        <Box
          key={basket.basketId}
          data-testid={`load-station-basket-${encodeLoadStationRegionIdPart(basket.basketId)}`}
          sx={{
            mt: idx === 0 ? "0.5em" : "1.5em",
            pt: idx === 0 ? undefined : "1em",
          }}
        >
          <Typography
            variant="h6"
            sx={{
              mb: "0.5em",
            }}
          >
            {basket.label}
          </Typography>
          <MoveMaterialArrowNode
            kind={{
              type: MoveMaterialNodeKindType.BasketZone,
              basketId: basket.basketId,
            }}
          >
            <MaterialRegion
              data={data}
              mat={{
                label: "",
                material: basket.material,
                isFree: false,
                sortable: false,
              }}
            />
          </MoveMaterialArrowNode>
        </Box>
      ))}
    </div>
  );
}

function MaterialColumn({
  data,
  region,
}: {
  readonly data: LoadStationData;
  readonly region: MaterialColumnRegion;
}) {
  switch (region.kind) {
    case "free":
      return (
        <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.FreeMaterialZone }}>
          <MaterialRegion
            data={data}
            mat={{
              label: region.label,
              material: region.material,
              isFree: true,
              sortable: false,
            }}
          />
        </MoveMaterialArrowNode>
      );
    case "queue":
      return (
        <MoveMaterialArrowNode
          kind={{ type: MoveMaterialNodeKindType.QueueZone, queue: region.label }}
        >
          <SortableRegion
            matIds={region.material.mats.map((m) => m.materialID)}
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
            <MaterialRegion
              data={data}
              mat={{
                label: region.label,
                material: region.material,
                isFree: false,
                sortable: true,
              }}
            />
          </SortableRegion>
        </MoveMaterialArrowNode>
      );
    case "baskets":
      return <BasketRegion data={data} label={region.label} baskets={region.baskets} />;
  }
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
      <Box
        ref={ref}
        sx={{
          padding: "8px",
          display: "flex",
          flexDirection: "column",
          height: "100%",
        }}
      >
        <Typography variant="h4">Completed</Typography>
        <Box
          sx={{
            position: "relative",
            flexGrow: 1,
          }}
        >
          <Box
            sx={{
              position: "absolute",
              top: "0",
              left: "0",
              right: "0",
              bottom: "0",
              overflow: "auto",
            }}
          >
            <RecentCompletedMaterial />
          </Box>
        </Box>
      </Box>
    );
  } else if (showMaterial) {
    return (
      <Box
        ref={ref}
        sx={{
          padding: "8px",
        }}
      >
        <Typography variant="h4">Completed</Typography>
        <RecentCompletedMaterial />
      </Box>
    );
  } else {
    return (
      <Box
        ref={ref}
        sx={{
          display: "flex",
          justifyContent: fillViewPort ? "flex-end" : undefined,
          padding: "8px",
        }}
      >
        <Typography
          variant="h4"
          sx={{
            margin: "4px",
            ...(fillViewPort ? { textOrientation: "mixed", writingMode: "vertical-rl" } : {}),
          }}
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
  if (
    material.action.type === api.ActionType.Loading &&
    material.action.loadOntoPalletNum === pallet
  ) {
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
      ? (material.action.processAfterLoad ?? material.process)
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

function AddMatButton({
  queues,
  onClose,
  toQueue,
  showAddToQueue,
  setShowAddToQueue,
}: {
  queues: ReadonlyArray<string>;
  onClose: () => void;
  toQueue: string | null;
  showAddToQueue: boolean;
  setShowAddToQueue: (show: boolean) => void;
}) {
  const setMatToShow = useSetAtom(matDetails.materialDialogOpen);
  const lastProcMat = useAtomValue(matDetails.materialInDialogLargestUsedProcess);
  const existingMat = useAtomValue(matDetails.materialInDialogInfo);
  const inProcMat = useAtomValue(matDetails.inProcessMaterialInDialog);
  const operator = useAtomValue(currentOperator);
  const [addExistingMat, addingExistingMat] = matDetails.useAddExistingMaterialToQueue();

  if (
    !existingMat ||
    inProcMat?.location.type === api.LocType.OnPallet ||
    inProcMat?.action.type === api.ActionType.Loading ||
    inProcMat?.action.type === api.ActionType.LoadingToBasket
  ) {
    return null;
  }
  if (queues.length === 0) return null;
  if (
    inProcMat?.location.type === api.LocType.InQueue &&
    inProcMat?.location.currentQueue &&
    queues.includes(inProcMat?.location.currentQueue)
  ) {
    return null;
  }

  if (!showAddToQueue) {
    return (
      <Button color="primary" onClick={() => setShowAddToQueue(true)}>
        Add To Queue
      </Button>
    );
  } else {
    return (
      <Button
        color="primary"
        disabled={toQueue === null || addingExistingMat}
        onClick={() => {
          addExistingMat({
            materialId: existingMat.materialID,
            queue: toQueue ?? "",
            queuePosition: -1,
            operator: operator,
          });
          setMatToShow(null);
          onClose();
        }}
      >
        Add To {toQueue ?? "Queue"}{" "}
        {lastProcMat ? ` To Run Process ${lastProcMat.process + 1}` : ""}
      </Button>
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

  const onClose = useCallback(
    function onClose() {
      setSwapSt(null);
      setInvalidateSt(null);
      setShowAddMaterial(false);
      setSelectedQueue(null);
    },
    [setSwapSt, setInvalidateSt],
  );

  return (
    <MaterialDialog
      onClose={onClose}
      allowNote
      highlightProcsGreaterOrEqualTo={invalidateSt?.process ?? undefined}
      extraDialogElements={
        <>
          <SwapMaterialDialogContent st={swapSt} setState={setSwapSt} />
          {invalidateSt !== null ? (
            <InvalidateCycleDialogContent st={invalidateSt} setState={setInvalidateSt} />
          ) : null}
          {showAddMaterial ? (
            <PromptForQueue
              selectedQueue={selectedQueue}
              setSelectedQueue={setSelectedQueue}
              queueNames={props.queues}
            />
          ) : undefined}
        </>
      }
      buttons={
        <>
          <InstructionButton pallet={props.pallet} />
          <PrintLabelButton />
          <QuarantineMatButton />
          <SignalInspectionButton />
          <AddMatButton
            toQueue={selectedQueue}
            queues={props.queues}
            onClose={onClose}
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
  readonly submitBasketLoadStationCommand?: SubmitBasketLoadStationCommand;
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

    const equalRows = `minmax(calc((100vh - 64px) / ${maxNumFaces}), max-content)`;
    for (let i = 0; i < maxNumFaces; i++) {
      const rowSize = i === maxNumFaces - 1 ? `1fr` : equalRows;
      rows += `"${colNames} palface${i} completed" ${rowSize}\n`;
    }
  } else {
    cols = "1fr";
    for (let i = 0; i < maxNumFaces; i++) {
      rows += `"palface${i}" minmax(134px, max-content)\n`;
    }
    for (let i = 0; i < numMatCols; i++) {
      rows += `"mat${i}" minmax(134px, max-content)\n`;
    }
    rows += '"completed" minmax(134px, max-content)\n';
  }

  return `${rows} / ${cols}`;
}

export function LoadStation(props: LoadStationProps) {
  const fmsInfo = useAtomValue(fmsInformation);
  const basketName = basketDisplayName(fmsInfo.basketName);
  const basketRegionLabel = basketName.endsWith("s") ? basketName : `${basketName}s`;
  const currentSt = useAtomValue(currentStatus);
  const hideNonLoading = useAtomValue(hideNonLoadingMaterialOnLoadStation);
  const data = useMemo(
    () => selectLoadStationAndQueueProps(props.loadNum, props.queues, currentSt, hideNonLoading),
    [currentSt, props.loadNum, props.queues, hideNonLoading],
  );

  const queueCols = LazySeq.of(data.queues)
    .sortBy(([q, _]) => q)
    .map(([q, mats]) => ({
      kind: "queue" as const,
      label: q,
      material: mats,
    }))
    .toRArray();

  const matCols: MaterialColumnRegion[] = [...queueCols];

  if (data.baskets.size > 0) {
    const baskets = LazySeq.of(data.baskets)
      .filter(([b, _]) => b !== data.activeBasket?.basketId)
      .sortBy(([b, _]) => b)
      .map(([b, mats]) => ({
        basketId: b,
        label: `${basketName} ${b}`,
        material: mats,
      }))
      .toRArray();

    if (baskets.length > 0) {
      matCols.push({
        kind: "baskets" as const,
        label: basketRegionLabel,
        baskets,
      });
    }
  }

  if ((data.queues.size === 0 && data.baskets.size === 0) || data.freeLoadingMaterial.length > 0) {
    matCols.push({
      kind: "free" as const,
      label: "Material",
      material: { mats: data.freeLoadingMaterial, hiddenCnt: 0 },
    });
  }
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
    <MoveMaterialArrowContainer hideArrows={!fillViewPort} whiteBackground={props.whiteBackground}>
      <Box
        component="main"
        sx={{
          width: "100%",
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
            data-testid={materialColumnTestId(col)}
            sx={{
              gridArea: `mat${idx}`,
              padding: "8px",
              borderRight: fillViewPort ? "1px solid black" : undefined,
              borderBottom: !fillViewPort ? "1px solid black" : undefined,
            }}
          >
            <MaterialColumn data={data} region={col} />
          </Box>
        ))}
        {data.face.keysToAscLazySeq().map((faceNum, idx) => (
          <Box
            key={faceNum}
            sx={{
              marginLeft: "15px",
              gridArea: `palface${idx}`,
              padding: "8px",
              borderTop: data.pallet && idx !== 0 ? "1px solid black" : undefined,
            }}
          >
            <PalletFace
              data={data}
              faceNum={faceNum}
              loadNum={props.loadNum}
              submitBasketLoadStationCommand={props.submitBasketLoadStationCommand}
            />
          </Box>
        ))}
        <Box
          data-testid="load-station-completed"
          sx={{
            borderLeft: fillViewPort ? "1px solid black" : undefined,
            borderTop: !fillViewPort ? "1px solid black" : undefined,
            gridArea: "completed",
          }}
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

function LoadStationCheckWidth(props: LoadStationProps): ReactNode {
  const fmsInfo = useAtomValue(fmsInformation);
  const title = loadStationDisplayName(props.loadNum, fmsInfo.loadStationNames);
  useSetTitle(title);
  return <LoadStation {...props} />;
}

export default LoadStationCheckWidth;
