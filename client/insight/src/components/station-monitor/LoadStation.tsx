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
import { Box, Divider } from "@mui/material";
import { Button } from "@mui/material";
import { Hidden } from "@mui/material";
import TimeAgo from "react-timeago";
import { addSeconds } from "date-fns";
import { durationToSeconds } from "../../util/parseISODuration.js";
import { LazySeq } from "@seedtactics/immutable-collections";

import { FolderOpen as FolderOpenIcon } from "@mui/icons-material";

import {
  LoadStationAndQueueData,
  selectLoadStationAndQueueProps,
  PalletData,
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

function MultiInstructionButton({
  loadData,
  operator,
}: {
  readonly loadData: LoadStationAndQueueData;
  readonly operator: string | null;
}) {
  const urls = React.useMemo(() => {
    const pal = loadData.pallet;
    if (pal) {
      return LazySeq.of(loadData.face.values())
        .append(loadData.freeLoadingMaterial)
        .append(loadData.free ?? [])
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

function showArrow(m: Readonly<api.IInProcessMaterial>): boolean {
  if (
    m.action.type === api.ActionType.Loading &&
    m.location.type === api.LocType.OnPallet &&
    (m.action.loadOntoPallet === undefined || m.action.loadOntoPallet === m.location.pallet) &&
    (m.action.loadOntoFace === undefined || m.action.loadOntoFace === m.location.face)
  ) {
    // an operation at the loadstation which is not moving the material
    return false;
  } else {
    return true;
  }
}

interface PalletColumnProps {
  readonly dateOfCurrentStatus: Date;
  readonly data: LoadStationAndQueueData;
  readonly fillViewPort: boolean;
}

function PalletColumn(props: PalletColumnProps) {
  const maxFace = LazySeq.of(props.data.face.keys()).maxBy((x) => x) ?? 1;

  let palDetails: JSX.Element;
  const firstMats = LazySeq.of(props.data.face).head()?.[1];
  if (props.data.face.size === 1 && firstMats) {
    palDetails = (
      <Box sx={{ ml: "4em", mr: "4em" }}>
        <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.PalletFaceZone, face: maxFace }}>
          <WhiteboardRegion label={""} spaceAround>
            {firstMats.map((m, idx) => (
              <MoveMaterialArrowNode
                key={idx}
                kind={{
                  type: MoveMaterialNodeKindType.Material,
                  material: showArrow(m) ? m : null,
                }}
              >
                <InProcMaterial mat={m} />
              </MoveMaterialArrowNode>
            ))}
          </WhiteboardRegion>
        </MoveMaterialArrowNode>
      </Box>
    );
  } else {
    palDetails = (
      <Box sx={{ ml: "4em", mr: "4em" }}>
        {LazySeq.of(props.data.face)
          .sortBy(([face, _]) => face)
          .map(([face, data]) => (
            <div key={face}>
              <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.PalletFaceZone, face: face }}>
                <WhiteboardRegion label={"Face " + face.toString()} spaceAround>
                  {data.map((m, idx) => (
                    <MoveMaterialArrowNode
                      key={idx}
                      kind={{ type: MoveMaterialNodeKindType.Material, material: showArrow(m) ? m : null }}
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
    );
  }

  return (
    <>
      {props.data.allJobsHaveRawMatQueue && props.data.freeLoadingMaterial.length === 0 ? undefined : (
        <>
          <WhiteboardRegion label="Raw Material" spaceAround>
            {props.data.freeLoadingMaterial.map((m, idx) => (
              <MoveMaterialArrowNode
                key={idx}
                kind={{ type: MoveMaterialNodeKindType.Material, material: m }}
              >
                <InProcMaterial mat={m} />
              </MoveMaterialArrowNode>
            ))}
          </WhiteboardRegion>
          <Divider />
        </>
      )}
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
          <Box sx={{ position: "absolute", top: "4px", left: "4px" }}>
            <Box sx={{ color: "rgba(0,0,0,0.5)", fontSize: "small" }}>Pallet</Box>
            {props.data.pallet ? (
              <Box sx={{ color: "rgba(0,0,0,0.5)", fontSize: "xx-large" }}>{props.data.pallet.pallet}</Box>
            ) : undefined}
          </Box>
          {palDetails}
        </Box>
      )}
      <Divider />
      <MoveMaterialArrowNode kind={{ type: MoveMaterialNodeKindType.CompletedMaterialZone }}>
        <WhiteboardRegion label="Completed Material" />
      </MoveMaterialArrowNode>
    </>
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

function QuarantineButton() {
  const fmsInfo = useRecoilValue(fmsInformation);
  const [signalQuarantine, signalingQuarantine] = matDetails.useSignalForQuarantine();
  const curMat = useRecoilValue(matDetails.materialInDialogInfo);
  const operator = useRecoilValue(currentOperator);
  const closeMatDialog = matDetails.useCloseMaterialDialog();

  const quarantineQueue = fmsInfo.allowQuarantineAtLoadStation ? fmsInfo.quarantineQueue ?? null : null;

  if (!curMat || !quarantineQueue || quarantineQueue === "") return null;

  return (
    <Button
      color="primary"
      disabled={signalingQuarantine}
      onClick={() => {
        signalQuarantine(curMat.materialID, quarantineQueue, operator);
        closeMatDialog();
      }}
    >
      Quarantine
    </Button>
  );
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
          <QuarantineButton />
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
  readonly showFree: boolean;
  readonly queues: ReadonlyArray<string>;
}

interface LoadStationDisplayProps extends LoadStationProps {
  readonly fillViewPort: boolean;
}

export function LoadStation(props: LoadStationDisplayProps) {
  const operator = useRecoilValue(currentOperator);
  const currentSt = useRecoilValue(currentStatus);
  const data = React.useMemo(
    () => selectLoadStationAndQueueProps(props.loadNum, props.queues, props.showFree, currentSt),
    [currentSt, props.loadNum, props.showFree, props.queues]
  );
  const isDemo = useIsDemo();

  const queues = LazySeq.of(data.queues)
    .sortBy(([q, _]) => q)
    .map(([q, mats]) => ({
      label: q,
      material: mats,
      isFree: false,
    }));

  let cells = queues;
  if (data.free) {
    cells = queues.prepend({
      label: "In Process Material",
      material: data.free,
      isFree: true,
    });
  }

  const col1 = cells.take(2);
  const col2 = cells.drop(2).take(2);

  return (
    <MoveMaterialArrowContainer>
      <Box
        component="main"
        sx={
          props.fillViewPort
            ? {
                height: "calc(100vh - 64px - 1em)",
                display: "flex",
                padding: "8px",
                width: "100%",
              }
            : {
                display: "flex",
                padding: "8px",
                width: "100%",
              }
        }
      >
        <Box sx={{ flexGrow: 1, display: "flex", flexDirection: "column", position: "relative" }}>
          <PalletColumn
            fillViewPort={props.fillViewPort}
            data={data}
            dateOfCurrentStatus={currentSt.timeOfCurrentStatusUTC}
          />
          {isDemo ? undefined : (
            <Box
              sx={
                props.fillViewPort
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
              <MultiInstructionButton loadData={data} operator={operator} />
            </Box>
          )}
        </Box>
        {col1.isEmpty() ? undefined : (
          <Box
            sx={{
              width: "16em",
              padding: "8px",
              display: "flex",
              flexDirection: "column",
              borderLeft: "1px solid rgba(0, 0, 0, 0.12)",
            }}
          >
            {col1.map((mat, idx) => (
              <MoveMaterialArrowNode
                key={idx}
                kind={
                  mat.isFree
                    ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
                    : {
                        type: MoveMaterialNodeKindType.QueueZone,
                        queue: mat.label,
                      }
                }
              >
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
                  <WhiteboardRegion label={mat.label}>
                    {mat.material.map((m, matIdx) => (
                      <MoveMaterialArrowNode
                        key={matIdx}
                        kind={{
                          type: MoveMaterialNodeKindType.Material,
                          material: data.pallet && m.action.loadOntoPallet === data.pallet.pallet ? m : null,
                        }}
                      >
                        <SortableInProcMaterial
                          mat={m}
                          displaySinglePallet={data.pallet ? data.pallet.pallet : ""}
                        />
                      </MoveMaterialArrowNode>
                    ))}
                  </WhiteboardRegion>
                </SortableRegion>
              </MoveMaterialArrowNode>
            ))}
          </Box>
        )}
        {col2.isEmpty() ? undefined : (
          <Box
            sx={{
              width: "16em",
              padding: "8px",
              display: "flex",
              flexDirection: "column",
              borderLeft: "1px solid rgba(0, 0, 0, 0.12)",
            }}
          >
            {col2.map((mat, idx) => (
              <MoveMaterialArrowNode
                key={idx}
                kind={
                  mat.isFree
                    ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
                    : {
                        type: MoveMaterialNodeKindType.QueueZone,
                        queue: mat.label,
                      }
                }
              >
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
                  <WhiteboardRegion label={mat.label}>
                    {mat.material.map((m, matIdx) => (
                      <MoveMaterialArrowNode
                        key={matIdx}
                        kind={{
                          type: MoveMaterialNodeKindType.Material,
                          material: data.pallet && m.action.loadOntoPallet === data.pallet.pallet ? m : null,
                        }}
                      >
                        <SortableInProcMaterial
                          mat={m}
                          displaySinglePallet={data.pallet ? data.pallet.pallet : ""}
                        />
                      </MoveMaterialArrowNode>
                    ))}
                  </WhiteboardRegion>
                </SortableRegion>
              </MoveMaterialArrowNode>
            ))}
          </Box>
        )}
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
      <Hidden lgDown>
        <LoadStation {...props} fillViewPort />
      </Hidden>
      <Hidden lgUp>
        <LoadStation {...props} fillViewPort={false} />
      </Hidden>
    </div>
  );
}

export default LoadStationCheckWidth;
