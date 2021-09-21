/* Copyright (c) 2021, John Lenz

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
import { Divider } from "@material-ui/core";
import { withStyles } from "@material-ui/core";
import { createStyles } from "@material-ui/core";
import { WithStyles } from "@material-ui/core";
import { Button } from "@material-ui/core";
import { Hidden } from "@material-ui/core";
import FolderOpenIcon from "@material-ui/icons/FolderOpen";
import TimeAgo from "react-timeago";
import { addSeconds } from "date-fns";
import { durationToSeconds } from "../../util/parseISODuration";

import { LoadStationAndQueueData, selectLoadStationAndQueueProps, PalletData } from "../../data/load-station";
import {
  MaterialDialog,
  InProcMaterial,
  SortableInProcMaterial,
  WhiteboardRegion,
  SortableWhiteboardRegion,
  InstructionButton,
} from "./Material";
import * as api from "../../network/api";
import * as matDetails from "../../cell-status/material-details";
import { SelectWorkorderDialog } from "./SelectWorkorder";
import SelectInspTypeDialog, { selectInspTypeDialogOpen } from "./SelectInspType";
import { MoveMaterialArrowContainer, MoveMaterialArrowNode } from "./MoveMaterialArrows";
import { MoveMaterialNodeKindType } from "../../data/move-arrows";
import { SortEnd } from "react-sortable-hoc";
import { HashMap, Option } from "prelude-ts";
import { LazySeq } from "../../util/lazyseq";
import { currentOperator } from "../../data/operators";
import { PrintedLabel } from "./PrintedLabel";
import ReactToPrint from "react-to-print";
import { instructionUrl } from "../../network/backend";
import { Tooltip } from "@material-ui/core";
import { Fab } from "@material-ui/core";
import { useRecoilState, useRecoilValue, useSetRecoilState } from "recoil";
import { fmsInformation } from "../../network/server-settings";
import { currentStatus, reorder_queued_mat } from "../../cell-status/current-status";
import { useIsDemo } from "../routes";

function stationPalMaterialStatus(mat: Readonly<api.IInProcessMaterial>, dateOfCurrentStatus: Date): JSX.Element {
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

const stationStatusStyles = createStyles({
  defList: {
    color: "rgba(0,0,0,0.6)",
  },
  defItem: {
    marginTop: "1em",
  },
});

interface StationStatusProps extends WithStyles<typeof stationStatusStyles> {
  byStation: HashMap<string, { pal?: PalletData; queue?: PalletData }>;
  dateOfCurrentStatus: Date;
}

const StationStatus = withStyles(stationStatusStyles)((props: StationStatusProps) => {
  if (props.byStation.length() === 0) {
    return <div />;
  }
  return (
    <dl className={props.classes.defList}>
      {props.byStation
        .toVector()
        .sortOn(([s, _]) => s)
        .map(([stat, pals]) => (
          <React.Fragment key={stat}>
            {pals.pal ? (
              <>
                <dt className={props.classes.defItem}>
                  {stat} - Pallet {pals.pal.pallet.pallet} - worktable
                </dt>
                {pals.pal.material.map((mat, idx) => (
                  <dd key={idx}>{stationPalMaterialStatus(mat, props.dateOfCurrentStatus)}</dd>
                ))}
              </>
            ) : undefined}
            {pals.queue ? (
              <>
                <dt className={props.classes.defList}>
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
});

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
      return LazySeq.ofIterable(loadData.face.valueIterable())
        .append(loadData.freeLoadingMaterial)
        .append(loadData.free ?? [])
        .appendAll(loadData.queues.valueIterable())
        .flatMap((x) => x)
        .mapOption((mat) => {
          if (
            mat.action.type === api.ActionType.Loading &&
            mat.action.loadOntoPallet === pal.pallet &&
            mat.location.type === api.LocType.OnPallet &&
            mat.location.pallet === pal.pallet
          ) {
            // transfer, but use unload type
            return Option.some(
              instructionUrl(mat.partName, "unload", mat.materialID, pal.pallet, mat.process, operator)
            );
          } else if (mat.action.type === api.ActionType.Loading && mat.action.loadOntoPallet === pal.pallet) {
            return Option.some(
              instructionUrl(
                mat.partName,
                "load",
                mat.materialID,
                pal.pallet,
                mat.action.processAfterLoad ?? mat.process,
                operator
              )
            );
          } else if (
            mat.location.type === api.LocType.OnPallet &&
            mat.location.pallet === pal.pallet &&
            (mat.action.type === api.ActionType.UnloadToCompletedMaterial ||
              mat.action.type === api.ActionType.UnloadToInProcess)
          ) {
            return Option.some(
              instructionUrl(mat.partName, "unload", mat.materialID, pal.pallet, mat.process, operator)
            );
          } else {
            return Option.none<string>();
          }
        })
        .toArray();
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

const palletStyles = createStyles({
  palletContainerFill: {
    width: "100%",
    position: "relative",
    flexGrow: 1,
  },
  palletContainerScroll: {
    width: "100%",
    position: "relative",
    minHeight: "12em",
  },
  statStatusFill: {
    width: "100%",
    flexGrow: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  statStatusScroll: {
    width: "100%",
    minHeight: "12em",
  },
  labelContainer: {
    position: "absolute",
    top: "4px",
    left: "4px",
  },
  label: {
    color: "rgba(0,0,0,0.5)",
    fontSize: "small",
  },
  labelPalletNum: {
    color: "rgba(0,0,0,0.5)",
    fontSize: "xx-large",
  },
  faceContainer: {
    marginLeft: "4em",
    marginRight: "4em",
  },
});

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

const PalletColumn = withStyles(palletStyles)((props: PalletColumnProps & WithStyles<typeof palletStyles>) => {
  let palletClass: string;
  let statStatusClass: string;
  if (props.fillViewPort) {
    palletClass = props.classes.palletContainerFill;
    statStatusClass = props.classes.statStatusFill;
  } else {
    palletClass = props.classes.palletContainerScroll;
    statStatusClass = props.classes.statStatusScroll;
  }

  const maxFace = props.data.face
    .keySet()
    .maxOn((x) => x)
    .getOrElse(1);

  let palDetails: JSX.Element;
  const singleMat = props.data.face.single();
  if (singleMat.isSome()) {
    const mat = singleMat.get()[1];
    palDetails = (
      <div className={props.classes.faceContainer}>
        <MoveMaterialArrowNode type={MoveMaterialNodeKindType.PalletFaceZone} face={maxFace}>
          <WhiteboardRegion label={""} spaceAround>
            {(mat || []).map((m, idx) => (
              <MoveMaterialArrowNode
                key={idx}
                type={MoveMaterialNodeKindType.Material}
                material={showArrow(m) ? m : null}
              >
                <InProcMaterial mat={m} />
              </MoveMaterialArrowNode>
            ))}
          </WhiteboardRegion>
        </MoveMaterialArrowNode>
      </div>
    );
  } else {
    palDetails = (
      <div className={props.classes.faceContainer}>
        {props.data.face
          .toVector()
          .sortOn(([face, _]) => face)
          .map(([face, data]) => (
            <div key={face}>
              <MoveMaterialArrowNode type={MoveMaterialNodeKindType.PalletFaceZone} face={face}>
                <WhiteboardRegion label={"Face " + face.toString()} spaceAround>
                  {data.map((m, idx) => (
                    <MoveMaterialArrowNode
                      key={idx}
                      type={MoveMaterialNodeKindType.Material}
                      material={showArrow(m) ? m : null}
                    >
                      <InProcMaterial mat={m} />
                    </MoveMaterialArrowNode>
                  ))}
                </WhiteboardRegion>
              </MoveMaterialArrowNode>
              {face === maxFace ? undefined : <Divider key={1} />}
            </div>
          ))}
      </div>
    );
  }

  return (
    <>
      {props.data.allJobsHaveRawMatQueue && props.data.freeLoadingMaterial.length === 0 ? undefined : (
        <>
          <WhiteboardRegion label="Raw Material" spaceAround>
            {props.data.freeLoadingMaterial.map((m, idx) => (
              <MoveMaterialArrowNode key={idx} type={MoveMaterialNodeKindType.Material} material={m}>
                <InProcMaterial mat={m} />
              </MoveMaterialArrowNode>
            ))}
          </WhiteboardRegion>
          <Divider />
        </>
      )}
      {props.data.stationStatus ? ( // stationStatus is defined only when no pallet
        <div className={statStatusClass}>
          <StationStatus byStation={props.data.stationStatus} dateOfCurrentStatus={props.dateOfCurrentStatus} />
        </div>
      ) : (
        <div className={palletClass}>
          <div className={props.classes.labelContainer}>
            <div className={props.classes.label}>Pallet</div>
            {props.data.pallet ? (
              <div className={props.classes.labelPalletNum}>{props.data.pallet.pallet}</div>
            ) : undefined}
          </div>
          {palDetails}
        </div>
      )}
      <Divider />
      <MoveMaterialArrowNode type={MoveMaterialNodeKindType.CompletedMaterialZone}>
        <WhiteboardRegion label="Completed Material" />
      </MoveMaterialArrowNode>
    </>
  );
});

interface LoadMatDialogProps {
  readonly loadNum: number;
  readonly pallet: string | null;
}

function instructionType(mat: matDetails.MaterialDetail): string {
  let ty: "load" | "unload" = "load";
  for (const evt of mat.events) {
    if (evt.type === api.LogType.LoadUnloadCycle && evt.result === "UNLOAD") {
      if (evt.startofcycle) {
        ty = "unload";
      } else {
        ty = "load";
      }
    }
  }
  return ty;
}

const LoadMatDialog = React.memo(function LoadMatDialog(props: LoadMatDialogProps) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const quarantineQueue = fmsInfo.allowQuarantineAtLoadStation ? fmsInfo.quarantineQueue ?? null : null;
  const operator = useRecoilValue(currentOperator);
  const displayMat = useRecoilValue(matDetails.materialDetail);
  const setWorkorderDialogOpen = useSetRecoilState(matDetails.loadWorkordersForMaterialInDialog);
  const setMatToDisplay = useSetRecoilState(matDetails.materialToShowInDialog);
  const [printLabel, printingLabel] = matDetails.usePrintLabel();
  const [signalQuarantine, signalingQuarantine] = matDetails.useSignalForQuarantine();
  const setForceInspOpen = useSetRecoilState(selectInspTypeDialogOpen);

  const printRef = React.useRef(null);

  function openAssignWorkorder() {
    if (displayMat) {
      return;
    }
    setWorkorderDialogOpen(true);
  }

  return (
    <MaterialDialog
      display_material={displayMat}
      onClose={() => setMatToDisplay(null)}
      allowNote
      buttons={
        <>
          {displayMat && displayMat.partName !== "" ? (
            <InstructionButton
              material={displayMat}
              type={instructionType(displayMat)}
              operator={operator}
              pallet={props.pallet}
            />
          ) : undefined}
          {displayMat && fmsInfo.usingLabelPrinterForSerials ? (
            fmsInfo.useClientPrinterForLabels ? (
              <>
                <ReactToPrint
                  content={() => printRef.current}
                  copyStyles={false}
                  trigger={() => <Button color="primary">Print Label</Button>}
                />
                <div style={{ display: "none" }}>
                  <div ref={printRef}>
                    <PrintedLabel material={displayMat ? [displayMat] : []} oneJobPerPage={false} />
                  </div>
                </div>
              </>
            ) : (
              <Button
                color="primary"
                disabled={printingLabel}
                onClick={() =>
                  printLabel({
                    materialId: displayMat.materialID,
                    proc: LazySeq.ofIterable(displayMat.events)
                      .filter(
                        (e) =>
                          e.details?.["PalletCycleInvalidated"] !== "1" &&
                          (e.type === api.LogType.LoadUnloadCycle ||
                            e.type === api.LogType.MachineCycle ||
                            e.type === api.LogType.AddToQueue)
                      )
                      .flatMap((e) => e.material)
                      .filter((e) => e.id === displayMat.materialID)
                      .maxOn((e) => e.proc)
                      .map((e) => e.proc)
                      .getOrElse(1),
                    loadStation: props.loadNum,
                    queue: null,
                  })
                }
              >
                Print Label
              </Button>
            )
          ) : undefined}
          {displayMat && quarantineQueue ? (
            <Button
              color="primary"
              disabled={signalingQuarantine}
              onClick={() => {
                signalQuarantine(displayMat.materialID, quarantineQueue, operator);
                setMatToDisplay(null);
              }}
            >
              Quarantine
            </Button>
          ) : undefined}
          <Button color="primary" onClick={() => setForceInspOpen(true)}>
            Signal Inspection
          </Button>
          {fmsInfo.allowChangeWorkorderAtLoadStation ? (
            <Button color="primary" onClick={openAssignWorkorder}>
              {displayMat && displayMat.workorderId ? "Change Workorder" : "Assign Workorder"}
            </Button>
          ) : undefined}
        </>
      }
    />
  );
});

const loadStyles = createStyles({
  mainFillViewport: {
    height: "calc(100vh - 64px - 1em)",
    display: "flex",
    padding: "8px",
    width: "100%",
  },
  mainScrollable: {
    display: "flex",
    padding: "8px",
    width: "100%",
  },
  palCol: {
    flexGrow: 1,
    display: "flex",
    flexDirection: "column",
    position: "relative",
  },
  queueCol: {
    width: "16em",
    padding: "8px",
    display: "flex",
    flexDirection: "column",
    borderLeft: "1px solid rgba(0, 0, 0, 0.12)",
  },
  fabFillViewport: {
    position: "absolute",
    bottom: "0px",
    right: "5px",
  },
  fabScrollFixed: {
    position: "fixed",
    bottom: "5px",
    right: "5px",
  },
});

interface LoadStationProps {
  readonly loadNum: number;
  readonly showFree: boolean;
  readonly queues: ReadonlyArray<string>;
}

interface LoadStationDisplayProps extends LoadStationProps {
  readonly fillViewPort: boolean;
}

export const LoadStation = withStyles(loadStyles)((props: LoadStationDisplayProps & WithStyles<typeof loadStyles>) => {
  const operator = useRecoilValue(currentOperator);
  const [currentSt, setCurrentStatus] = useRecoilState(currentStatus);
  const data = React.useMemo(
    () => selectLoadStationAndQueueProps(props.loadNum, props.queues, props.showFree, currentSt),
    [currentSt, props.loadNum, props.showFree, props.queues]
  );
  const [addExistingMatToQueue] = matDetails.useAddExistingMaterialToQueue();
  const isDemo = useIsDemo();

  const queues = data.queues
    .toVector()
    .sortOn(([q, _]) => q)
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
      <main
        data-testid="stationmonitor-load"
        className={props.fillViewPort ? props.classes.mainFillViewport : props.classes.mainScrollable}
      >
        <div className={props.classes.palCol}>
          <PalletColumn
            fillViewPort={props.fillViewPort}
            data={data}
            dateOfCurrentStatus={currentSt.timeOfCurrentStatusUTC}
          />
          {isDemo ? undefined : (
            <div className={props.fillViewPort ? props.classes.fabFillViewport : props.classes.fabScrollFixed}>
              <MultiInstructionButton loadData={data} operator={operator} />
            </div>
          )}
        </div>
        {col1.length() === 0 ? undefined : (
          <div className={props.classes.queueCol}>
            {col1.zipWithIndex().map(([mat, idx]) => (
              <MoveMaterialArrowNode
                key={idx}
                {...(mat.isFree
                  ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
                  : {
                      type: MoveMaterialNodeKindType.QueueZone,
                      queue: mat.label,
                    })}
              >
                <SortableWhiteboardRegion
                  label={mat.label}
                  axis="y"
                  distance={5}
                  shouldCancelStart={() => false}
                  onSortEnd={(se: SortEnd) => {
                    addExistingMatToQueue({
                      materialId: mat.material[se.oldIndex].materialID,
                      queue: mat.label,
                      queuePosition: se.newIndex,
                      operator: operator,
                    });
                    setCurrentStatus(reorder_queued_mat(mat.label, mat.material[se.oldIndex].materialID, se.newIndex));
                  }}
                >
                  {mat.material.map((m, matIdx) => (
                    <MoveMaterialArrowNode
                      key={matIdx}
                      type={MoveMaterialNodeKindType.Material}
                      material={data.pallet && m.action.loadOntoPallet === data.pallet.pallet ? m : null}
                    >
                      <SortableInProcMaterial
                        index={matIdx}
                        mat={m}
                        displaySinglePallet={data.pallet ? data.pallet.pallet : ""}
                      />
                    </MoveMaterialArrowNode>
                  ))}
                </SortableWhiteboardRegion>
              </MoveMaterialArrowNode>
            ))}
          </div>
        )}
        {col2.length() === 0 ? undefined : (
          <div className={props.classes.queueCol}>
            {col2.zipWithIndex().map(([mat, idx]) => (
              <MoveMaterialArrowNode
                key={idx}
                {...(mat.isFree
                  ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
                  : {
                      type: MoveMaterialNodeKindType.QueueZone,
                      queue: mat.label,
                    })}
              >
                <SortableWhiteboardRegion
                  label={mat.label}
                  axis="y"
                  distance={5}
                  shouldCancelStart={() => false}
                  onSortEnd={(se: SortEnd) => {
                    addExistingMatToQueue({
                      materialId: mat.material[se.oldIndex].materialID,
                      queue: mat.label,
                      queuePosition: se.newIndex,
                      operator: operator,
                    });
                    setCurrentStatus(reorder_queued_mat(mat.label, mat.material[se.oldIndex].materialID, se.newIndex));
                  }}
                >
                  {mat.material.map((m, matIdx) => (
                    <MoveMaterialArrowNode
                      key={matIdx}
                      type={MoveMaterialNodeKindType.Material}
                      material={data.pallet && m.action.loadOntoPallet === data.pallet.pallet ? m : null}
                    >
                      <SortableInProcMaterial
                        index={matIdx}
                        mat={m}
                        displaySinglePallet={data.pallet ? data.pallet.pallet : ""}
                      />
                    </MoveMaterialArrowNode>
                  ))}
                </SortableWhiteboardRegion>
              </MoveMaterialArrowNode>
            ))}
          </div>
        )}
        <SelectWorkorderDialog />
        <SelectInspTypeDialog />
        <LoadMatDialog loadNum={data.loadNum} pallet={data.pallet?.pallet ?? null} />
      </main>
    </MoveMaterialArrowContainer>
  );
});

function LoadStationCheckWidth(props: LoadStationProps): JSX.Element {
  React.useEffect(() => {
    document.title = "Load " + props.loadNum.toString() + " - FMS Insight";
  }, [props.loadNum]);
  return (
    <div>
      <Hidden mdDown>
        <LoadStation {...props} fillViewPort />
      </Hidden>
      <Hidden lgUp>
        <LoadStation {...props} fillViewPort={false} />
      </Hidden>
    </div>
  );
}

export default LoadStationCheckWidth;
