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
import { Box, useMediaQuery, Button, Typography } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";

import { FolderOpen as FolderOpenIcon } from "@mui/icons-material";

import { LoadStationData, selectLoadStationAndQueueProps, MaterialList } from "../../data/load-station.js";
import {
  MaterialDialog,
  InProcMaterial,
  SortableInProcMaterial,
  DragOverlayInProcMaterial,
} from "./Material.js";
import { SortableRegion } from "./Whiteboard.js";
import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import { SelectWorkorderDialog, selectWorkorderDialogOpen } from "./SelectWorkorder.js";
import { SelectInspTypeDialog, SignalInspectionButton } from "./SelectInspType.js";
import { MoveMaterialArrowContainer, MoveMaterialArrowNode } from "./MoveMaterialArrows.js";
import { MoveMaterialNodeKindType } from "../../data/move-arrows.js";
import { currentOperator } from "../../data/operators.js";
import { instructionUrl } from "../../network/backend.js";
import { Tooltip } from "@mui/material";
import { Fab } from "@mui/material";
import { useRecoilValue, useSetRecoilState } from "recoil";
import { fmsInformation } from "../../network/server-settings.js";
import { currentStatus, secondsSinceEpochAtom } from "../../cell-status/current-status.js";
import { useIsDemo } from "../routes.js";
import { PrintOnClientButton } from "./QueuesMatDialog.js";
import { QuarantineMatButton } from "./QuarantineButton.js";
import { durationToSeconds } from "../../util/parseISODuration.js";
import { formatSeconds } from "./SystemOverview.js";

function MultiInstructionButton({ loadData }: { loadData: LoadStationData }) {
  const isDemo = useIsDemo();
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
  const currentStTime = useRecoilValue(currentStatus).timeOfCurrentStatusUTC;
  const secondsSinceEpoch = useRecoilValue(secondsSinceEpochAtom);

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
          <Typography variant="h4">Pallet {data.pallet.pallet}</Typography>
          {data.pallet.numFaces > 1 ? (
            <Typography variant="h6" justifySelf="center">
              Face 1
            </Typography>
          ) : undefined}
          <Box justifySelf="flex-end">
            <ElapsedLoadTime elapsedLoadTime={data.elapsedLoadingTime} />
          </Box>
        </Box>
      ) : data.pallet.numFaces > 1 ? (
        <Box display="flex" justifyContent="center">
          <Typography variant="h6">Face {faceNum}</Typography>
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
                <InProcMaterial mat={m} />
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
            material: data.pallet && m.action.loadOntoPallet === data.pallet.pallet ? m : null,
          }}
        >
          {mat.isFree ? (
            <InProcMaterial
              mat={m}
              displaySinglePallet={data.pallet ? data.pallet.pallet : ""}
              shake={m.action.type === api.ActionType.Loading}
            />
          ) : (
            <SortableInProcMaterial
              mat={m}
              displaySinglePallet={data.pallet ? data.pallet.pallet : ""}
              shake={
                m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === data.pallet?.pallet
              }
            />
          )}
        </MoveMaterialArrowNode>
      ))}
    </div>
  );
}

function MaterialColumn({
  data,
  mat,
}: {
  readonly data: LoadStationData;
  mat: { readonly label: string; readonly material: MaterialList; readonly isFree: boolean };
}) {
  return (
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
  );
}

const CompletedCol = React.memo(function CompletedCol({ fillViewPort }: { fillViewPort: boolean }) {
  return (
    <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.CompletedMaterialZone }}>
      <Box display="flex" justifyContent={fillViewPort ? "flex-end" : undefined} padding="8px">
        <Typography
          variant="h4"
          margin="4px"
          sx={fillViewPort ? { textOrientation: "mixed", writingMode: "vertical-rl" } : undefined}
        >
          Completed
        </Typography>
      </Box>
    </MoveMaterialArrowNode>
  );
});

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

function useGridLayout({
  numMatCols,
  maxNumFaces,
  horizontal,
}: {
  numMatCols: number;
  maxNumFaces: number;
  horizontal: boolean;
}): string {
  let rows = "";
  let cols = "";

  if (horizontal) {
    let colNames = "";
    for (let i = 0; i < numMatCols; i++) {
      cols += "minmax(16em, max-content) ";
      colNames += `mat${i} `;
    }
    cols += "1fr ";
    cols += " 4.5em";

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
  const currentSt = useRecoilValue(currentStatus);
  const data = React.useMemo(
    () => selectLoadStationAndQueueProps(props.loadNum, props.queues, currentSt),
    [currentSt, props.loadNum, props.queues]
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

  const fillViewPort = useMediaQuery(
    matCols.length <= 1
      ? "(min-width: 600px)"
      : matCols.length === 2
      ? "(min-width: 950px)"
      : "(min-width: 1250px)"
  );

  const grid = useGridLayout({ numMatCols: matCols.length, maxNumFaces: 2, horizontal: fillViewPort });

  return (
    <MoveMaterialArrowContainer hideArrows={!fillViewPort}>
      <Box
        component="main"
        width="100%"
        bgcolor="#F8F8F8"
        display="grid"
        sx={
          fillViewPort
            ? {
                minHeight: "calc(100vh - 64px)",
                gridTemplate: grid,
              }
            : {
                padding: "8px",
                gridTemplate: grid,
              }
        }
      >
        {matCols.map((col, idx) => (
          <Box
            key={idx}
            gridArea={`mat${idx}`}
            padding="8px"
            borderRight={fillViewPort ? "1px solid black" : undefined}
            borderBottom={!fillViewPort ? "1px solid black" : undefined}
          >
            <MaterialColumn data={data} mat={col} />
          </Box>
        ))}
        {[0, 1].map((i) => (
          <Box
            key={i}
            marginLeft="15px"
            gridArea={`palface${i}`}
            padding="8px"
            borderTop={data.pallet && i !== 0 ? "1px solid black" : undefined}
          >
            <PalletFace data={data} faceNum={i + 1} />
          </Box>
        ))}
        <Box
          borderLeft={fillViewPort ? "1px solid black" : undefined}
          borderTop={!fillViewPort ? "1px solid black" : undefined}
          gridArea="completed"
        >
          <CompletedCol fillViewPort={fillViewPort} />
        </Box>
        <MultiInstructionButton loadData={data} />
        <SelectWorkorderDialog />
        <SelectInspTypeDialog />
        <LoadMatDialog loadNum={props.loadNum} pallet={data.pallet?.pallet ?? null} />
      </Box>
    </MoveMaterialArrowContainer>
  );
}

function LoadStationCheckWidth(props: LoadStationProps): JSX.Element {
  React.useEffect(() => {
    document.title = "Load " + props.loadNum.toString() + " - FMS Insight";
  }, [props.loadNum]);
  return <LoadStation {...props} />;
}

export default LoadStationCheckWidth;
