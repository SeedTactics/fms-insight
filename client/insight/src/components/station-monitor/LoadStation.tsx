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
import { Box, Divider, useMediaQuery, Button } from "@mui/material";
import TimeAgo from "react-timeago";
import { addSeconds } from "date-fns";
import { durationToSeconds } from "../../util/parseISODuration.js";
import { LazySeq } from "@seedtactics/immutable-collections";

import { FolderOpen as FolderOpenIcon } from "@mui/icons-material";

import {
  LoadStationData,
  selectLoadStationAndQueueProps,
  PalletData,
  MaterialList,
} from "../../data/load-station.js";
import {
  MaterialDialog,
  InProcMaterial,
  SortableInProcMaterial,
  DragOverlayInProcMaterial,
} from "./Material.js";
import { WhiteboardRegion, SortableRegion } from "./Whiteboard.js";
import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "./SelectWorkorder.js";
import { SelectInspTypeDialog, selectInspTypeDialogOpen } from "./SelectInspType.js";
import { MoveMaterialArrowContainer, MoveMaterialArrowNode } from "./MoveMaterialArrows.js";
import { MoveMaterialNodeKindType } from "../../data/move-arrows.js";
import { currentOperator } from "../../data/operators.js";
import { instructionUrl } from "../../network/backend.js";
import { Tooltip } from "@mui/material";
import { Fab } from "@mui/material";
import { useRecoilValue, useSetRecoilState } from "recoil";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { useIsDemo } from "../routes.js";
import { PrintOnClientButton } from "./QueuesMatDialog.js";
import { QuarantineMatButton } from "./QuarantineButton.js";

function stationPalMaterialStatus(
  mat: Readonly<api.IInProcessMaterial>,
  dateOfCurrentStatus: Date
): JSX.Element {
  const name = mat.partName + "-" + mat.process.toString();

  let matStatus = "";
  let matTime: JSX.Element | undefined;
  switch (mat.action.type) {
    case api.ActionType.Loading:
      if (
        (mat.action.loadOntoPallet !== undefined && mat.action.loadOntoPallet !== mat.location.pallet) ||
        (mat.action.loadOntoFace !== undefined && mat.action.loadOntoFace !== mat.location.face)
      ) {
        matStatus = " (loading)";
      } else if (mat.action.loadOntoPallet !== undefined && mat.action.loadOntoFace !== undefined) {
        matStatus = " (reclamp)";
      }
      break;
    case api.ActionType.UnloadToCompletedMaterial:
    case api.ActionType.UnloadToInProcess:
      matStatus = " (unloading)";
      break;
    case api.ActionType.Machining:
      matStatus = " (machining)";
      if (mat.action.expectedRemainingMachiningTime) {
        matStatus += " completing ";
        const seconds = durationToSeconds(mat.action.expectedRemainingMachiningTime);
        matTime = <TimeAgo date={addSeconds(dateOfCurrentStatus, seconds)} />;
      }
      break;
  }

  return (
    <>
      <span>{name + matStatus}</span>
      {matTime}
    </>
  );
}

interface StationStatusProps {
  byStation: ReadonlyMap<string, { pal?: PalletData; queue?: PalletData }>;
  dateOfCurrentStatus: Date;
}

function StationStatus(props: StationStatusProps) {
  if (props.byStation.size === 0) {
    return <div />;
  }
  return (
    <dl style={{ color: "rgba(0,0,0,0.6" }}>
      {LazySeq.of(props.byStation)
        .sortBy(([s, _]) => s)
        .map(([stat, pals]) => (
          <React.Fragment key={stat}>
            {pals.pal ? (
              <>
                <dt style={{ marginTop: "1em" }}>
                  {stat} - Pallet {pals.pal.pallet.pallet} - worktable
                </dt>
                {pals.pal.material.map((mat, idx) => (
                  <dd key={idx}>{stationPalMaterialStatus(mat, props.dateOfCurrentStatus)}</dd>
                ))}
              </>
            ) : undefined}
            {pals.queue ? (
              <>
                <dt style={{ marginTop: "1em" }}>
                  {stat} - Pallet {pals.queue.pallet.pallet} - queue
                </dt>
                {pals.queue.material.map((mat, idx) => (
                  <dd key={idx}>{stationPalMaterialStatus(mat, props.dateOfCurrentStatus)}</dd>
                ))}
              </>
            ) : undefined}
          </React.Fragment>
        ))}
    </dl>
  );
}

function MultiInstructionButton({ loadData }: { readonly loadData: LoadStationData }) {
  const operator = useRecoilValue(currentOperator);
  const urls = React.useMemo(() => {
    const pal = loadData.pallet;
    if (pal) {
      return LazySeq.of(loadData.face.values())
        .append(loadData.freeLoadingMaterial)
        .concat(loadData.queues.values())
        .flatMap((x) => x)
        .collect((mat) => {
          if (
            mat.action.type === api.ActionType.Loading &&
            mat.action.loadOntoPallet === pal.pallet &&
            mat.location.type === api.LocType.OnPallet &&
            mat.location.pallet === pal.pallet
          ) {
            // transfer, but use unload type
            return instructionUrl(mat.partName, "unload", mat.materialID, pal.pallet, mat.process, operator);
          } else if (mat.action.type === api.ActionType.Loading && mat.action.loadOntoPallet === pal.pallet) {
            return instructionUrl(
              mat.partName,
              "load",
              mat.materialID,
              pal.pallet,
              mat.action.processAfterLoad ?? mat.process,
              operator
            );
          } else if (
            mat.location.type === api.LocType.OnPallet &&
            mat.location.pallet === pal.pallet &&
            (mat.action.type === api.ActionType.UnloadToCompletedMaterial ||
              mat.action.type === api.ActionType.UnloadToInProcess)
          ) {
            return instructionUrl(mat.partName, "unload", mat.materialID, pal.pallet, mat.process, operator);
          } else {
            return null;
          }
        })
        .toRArray();
    } else {
      return [];
    }
  }, [loadData]);

  if (urls.length === 0) {
    return <div />;
  }

  function open() {
    for (const url of urls) {
      window.open(url, "_blank");
    }
  }

  return (
    <Tooltip title="Open All Instructions">
      <Fab onClick={open} color="secondary">
        <FolderOpenIcon />
      </Fab>
    </Tooltip>
  );
}

function PalletZones({ data }: { readonly data: LoadStationData }) {
  const maxFace = LazySeq.of(data.face.keys()).maxBy((x) => x) ?? 1;
  const firstMats = LazySeq.of(data.face).head()?.[1];

  return (
    <>
      <Box sx={{ position: "absolute", top: "4px", left: "4px" }}>
        <Box sx={{ color: "rgba(0,0,0,0.5)", fontSize: "small" }}>Pallet</Box>
        {data.pallet ? (
          <Box sx={{ color: "rgba(0,0,0,0.5)", fontSize: "xx-large" }}>{data.pallet.pallet}</Box>
        ) : undefined}
      </Box>
      {data.face.size === 1 && firstMats ? (
        <Box sx={{ ml: "4em", mr: "4em" }}>
          <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.PalletFaceZone, face: maxFace }}>
            <WhiteboardRegion label={""} spaceAround>
              {firstMats.map((m, idx) => (
                <MoveMaterialArrowNode
                  key={idx}
                  kind={{
                    type: MoveMaterialNodeKindType.Material,
                    material: m,
                  }}
                >
                  <InProcMaterial mat={m} />
                </MoveMaterialArrowNode>
              ))}
            </WhiteboardRegion>
          </MoveMaterialArrowNode>
        </Box>
      ) : (
        <Box sx={{ ml: "4em", mr: "4em" }}>
          {LazySeq.of(data.face)
            .sortBy(([face, _]) => face)
            .map(([face, data]) => (
              <div key={face}>
                <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.PalletFaceZone, face: face }}>
                  <WhiteboardRegion label={"Face " + face.toString()} spaceAround>
                    {data.map((m, idx) => (
                      <MoveMaterialArrowNode
                        key={idx}
                        kind={{ type: MoveMaterialNodeKindType.Material, material: m }}
                      >
                        <InProcMaterial mat={m} />
                      </MoveMaterialArrowNode>
                    ))}
                  </WhiteboardRegion>
                </MoveMaterialArrowNode>
                {face === maxFace ? undefined : <Divider key={1} />}
              </div>
            ))}
        </Box>
      )}
    </>
  );
}

function PalletColumn(props: {
  readonly dateOfCurrentStatus: Date;
  readonly data: LoadStationData;
  readonly fillViewPort: boolean;
}) {
  return (
    <>
      {props.data.stationStatus ? ( // stationStatus is defined only when no pallet
        <Box
          sx={
            props.fillViewPort
              ? {
                  width: "100%",
                  flexGrow: 1,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                }
              : {
                  width: "100%",
                  minHeight: "12em",
                }
          }
        >
          <StationStatus
            byStation={props.data.stationStatus}
            dateOfCurrentStatus={props.dateOfCurrentStatus}
          />
        </Box>
      ) : (
        <Box
          sx={
            props.fillViewPort
              ? { position: "relative", width: "100%", flexGrow: 1 }
              : {
                  position: "relative",
                  width: "100%",
                  minHeight: "12em",
                }
          }
        >
          <PalletZones data={props.data} />
        </Box>
      )}
      <Divider />
      <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.CompletedMaterialZone }}>
        <WhiteboardRegion label="Completed Material" />
      </MoveMaterialArrowNode>
    </>
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
    <WhiteboardRegion column label={mat.label}>
      {mat.material.map((m, matIdx) => (
        <MoveMaterialArrowNode
          key={matIdx}
          kind={{
            type: MoveMaterialNodeKindType.Material,
            material: data.pallet && m.action.loadOntoPallet === data.pallet.pallet ? m : null,
          }}
        >
          {mat.isFree ? (
            <InProcMaterial mat={m} displaySinglePallet={data.pallet ? data.pallet.pallet : ""} />
          ) : (
            <SortableInProcMaterial mat={m} displaySinglePallet={data.pallet ? data.pallet.pallet : ""} />
          )}
        </MoveMaterialArrowNode>
      ))}
    </WhiteboardRegion>
  );
}

function MaterialColumn({
  data,
  mat,
  fillViewPort,
}: {
  readonly data: LoadStationData;
  mat: { readonly label: string; readonly material: MaterialList; readonly isFree: boolean };
  fillViewPort: boolean;
}) {
  return (
    <Box
      sx={{
        minWidth: "16em",
        padding: "8px",
        borderLeft: fillViewPort ? "1px solid rgba(0, 0, 0, 0.12)" : undefined,
        borderTop: !fillViewPort ? "1px solid rgba(0, 0, 0, 0.12)" : undefined,
      }}
    >
      <MoveMaterialArrowNode
        kind={
          mat.isFree
            ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
            : {
                type: MoveMaterialNodeKindType.QueueZone,
                queue: mat.label,
              }
        }
      >
        {mat.isFree ? (
          <MaterialRegion data={data} mat={mat} />
        ) : (
          <SortableRegion
            matIds={mat.material.map((m) => m.materialID)}
            direction="vertical"
            queueName={mat.label}
            renderDragOverlay={(mat) => (
              <DragOverlayInProcMaterial
                mat={mat}
                displaySinglePallet={data.pallet ? data.pallet.pallet : ""}
              />
            )}
          >
            <MaterialRegion data={data} mat={mat} />
          </SortableRegion>
        )}
      </MoveMaterialArrowNode>
    </Box>
  );
}

interface LoadMatDialogProps {
  readonly loadNum: number;
  readonly pallet: string | null;
}

function InstructionButton({ pallet }: { pallet: string | null }) {
  const material = useRecoilValue(matDetails.inProcessMaterialInDialog);
  const operator = useRecoilValue(currentOperator);

  if (material === null) return null;

  const type = material.action.type === api.ActionType.Loading ? "load" : "unload";

  const url = instructionUrl(
    material.partName,
    type,
    material.materialID,
    pallet,
    material.process,
    operator
  );
  return (
    <Button href={url} target="bms-instructions" color="primary">
      Instructions
    </Button>
  );
}

function PrintSerialButton({ loadNum }: { loadNum: number }) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const curMat = useRecoilValue(matDetails.inProcessMaterialInDialog);
  const [printLabel, printingLabel] = matDetails.usePrintLabel();

  if (curMat === null || !fmsInfo.usingLabelPrinterForSerials) return null;

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
            loadStation: loadNum,
            queue: null,
          })
        }
      >
        Print Label
      </Button>
    );
  }
}

function SignalInspectionButton() {
  const setForceInspOpen = useSetRecoilState(selectInspTypeDialogOpen);
  const curMat = useRecoilValue(matDetails.materialInDialogInfo);
  if (curMat === null) return null;
  return (
    <Button color="primary" onClick={() => setForceInspOpen(true)}>
      Signal Inspection
    </Button>
  );
}

const LoadMatDialog = React.memo(function LoadMatDialog(props: LoadMatDialogProps) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const setWorkorderDialogOpen = useSetRecoilState(selectWorkorderDialogOpen);

  return (
    <MaterialDialog
      allowNote
      buttons={
        <>
          <InstructionButton pallet={props.pallet} />
          <PrintSerialButton loadNum={props.loadNum} />
          <QuarantineMatButton />
          <SignalInspectionButton />
          {fmsInfo.allowChangeWorkorderAtLoadStation ? (
            <Button color="primary" onClick={() => setWorkorderDialogOpen(true)}>
              Assign Workorder
            </Button>
          ) : undefined}
        </>
      }
    />
  );
});

interface LoadStationProps {
  readonly loadNum: number;
  readonly queues: ReadonlyArray<string>;
}

export function LoadStation(props: LoadStationProps) {
  const currentSt = useRecoilValue(currentStatus);
  const data = React.useMemo(
    () => selectLoadStationAndQueueProps(props.loadNum, props.queues, currentSt),
    [currentSt, props.loadNum, props.queues]
  );
  const isDemo = useIsDemo();

  let matColsSeq = LazySeq.of(data.queues)
    .sortBy(([q, _]) => q)
    .map(([q, mats]) => ({
      label: q,
      material: mats,
      isFree: false,
    }));

  if (data.queues.size === 0 || data.freeLoadingMaterial.length > 0) {
    matColsSeq = matColsSeq.prepend({
      label: "Material",
      material: data.freeLoadingMaterial,
      isFree: true,
    });
  }
  const matCols = matColsSeq.toRArray();

  const fillViewPort = useMediaQuery(
    matCols.length <= 1
      ? "(min-width: 600px)"
      : matCols.length === 2
      ? "(min-width: 950px)"
      : "(min-width: 1250px)"
  );

  return (
    <MoveMaterialArrowContainer hideArrows={!fillViewPort}>
      <Box
        component="main"
        sx={
          fillViewPort
            ? {
                height: "calc(100vh - 64px - 1em)",
                display: "flex",
                padding: "8px",
                width: "100%",
              }
            : {
                padding: "8px",
                width: "100%",
              }
        }
      >
        <Box sx={{ flexGrow: 1, display: "flex", flexDirection: "column", position: "relative" }}>
          <PalletColumn
            fillViewPort={fillViewPort}
            data={data}
            dateOfCurrentStatus={currentSt.timeOfCurrentStatusUTC}
          />
          {!isDemo ? (
            <Box
              sx={
                fillViewPort
                  ? {
                      position: "absolute",
                      bottom: "0px",
                      right: "5px",
                    }
                  : {
                      position: "fixed",
                      bottom: "5px",
                      right: "5px",
                    }
              }
            >
              <MultiInstructionButton loadData={data} />
            </Box>
          ) : undefined}
        </Box>
        {matCols.map((col, idx) => (
          <MaterialColumn key={idx} data={data} mat={col} fillViewPort={fillViewPort} />
        ))}
        <SelectWorkorderDialog />
        <SelectInspTypeDialog />
        <LoadMatDialog loadNum={data.loadNum} pallet={data.pallet?.pallet ?? null} />
      </Box>
    </MoveMaterialArrowContainer>
  );
}

function LoadStationCheckWidth(props: LoadStationProps): JSX.Element {
  React.useEffect(() => {
    document.title = "Load " + props.loadNum.toString() + " - FMS Insight";
  }, [props.loadNum]);
  return (
    <div>
      <LoadStation {...props} />
    </div>
  );
}

export default LoadStationCheckWidth;
