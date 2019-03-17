/* Copyright (c) 2018, John Lenz

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
import Divider from "@material-ui/core/Divider";
import { WithStyles, createStyles, withStyles } from "@material-ui/core/styles";
import { createSelector } from "reselect";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58
import Button from "@material-ui/core/Button";
import TimeAgo from "react-timeago";
import { addSeconds } from "date-fns";
import { duration } from "moment";

import { LoadStationAndQueueData, selectLoadStationAndQueueProps, PalletData } from "../../data/load-station";
import {
  MaterialDialog,
  InProcMaterial,
  SortableInProcMaterial,
  WhiteboardRegion,
  SortableWhiteboardRegion,
  MaterialDialogProps,
  InstructionButton
} from "./Material";
import * as api from "../../data/api";
import * as routes from "../../data/routes";
import * as guiState from "../../data/gui-state";
import { Store, connect, mkAC, AppActionBeforeMiddleware } from "../../store/store";
import * as matDetails from "../../data/material-details";
import * as currentSt from "../../data/current-status";
import { MaterialSummary } from "../../data/events";
import SelectWorkorderDialog from "./SelectWorkorder";
import SetSerialDialog from "./EnterSerial";
import SelectInspTypeDialog from "./SelectInspType";
import { MoveMaterialArrowContainer, MoveMaterialArrowNode } from "./MoveMaterialArrows";
import { MoveMaterialNodeKindType } from "../../data/move-arrows";
import { SortEnd } from "react-sortable-hoc";
import { HashMap } from "prelude-ts";

function stationPalMaterialStatus(mat: Readonly<api.IInProcessMaterial>, dateOfCurrentStatus: Date): JSX.Element {
  const name = mat.partName + "-" + mat.process.toString();

  let matStatus = "";
  let matTime: JSX.Element | undefined;
  switch (mat.action.type) {
    case api.ActionType.Loading:
      matStatus = " (loading)";
      break;
    case api.ActionType.UnloadToCompletedMaterial:
    case api.ActionType.UnloadToInProcess:
      matStatus = " (unloading)";
      break;
    case api.ActionType.Machining:
      matStatus = " (machining)";
      if (mat.action.expectedRemainingMachiningTime) {
        matStatus += " completing ";
        const seconds = duration(mat.action.expectedRemainingMachiningTime).asSeconds();
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
    color: "rgba(0,0,0,0.6)"
  },
  defItem: {
    marginTop: "1em"
  }
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
        .sortOn(([s, p]) => s)
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
            ) : (
              undefined
            )}
            {pals.queue ? (
              <>
                <dt className={props.classes.defList}>
                  {stat} - Pallet {pals.queue.pallet.pallet} - queue
                </dt>
                {pals.queue.material.map((mat, idx) => (
                  <dd key={idx}>{stationPalMaterialStatus(mat, props.dateOfCurrentStatus)}</dd>
                ))}
              </>
            ) : (
              undefined
            )}
          </React.Fragment>
        ))}
    </dl>
  );
});

const palletStyles = createStyles({
  palletContainerFill: {
    width: "100%",
    position: "relative" as "relative",
    flexGrow: 1
  },
  palletContainerScroll: {
    width: "100%",
    position: "relative" as "relative",
    minHeight: "12em"
  },
  statStatusFill: {
    width: "100%",
    flexGrow: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center"
  },
  statStatusScroll: {
    width: "100%",
    minHeight: "12em"
  },
  labelContainer: {
    position: "absolute" as "absolute",
    top: "4px",
    left: "4px"
  },
  label: {
    color: "rgba(0,0,0,0.5)",
    fontSize: "small"
  },
  labelPalletNum: {
    color: "rgba(0,0,0,0.5)",
    fontSize: "xx-large"
  },
  faceContainer: {
    marginLeft: "4em",
    marginRight: "4em"
  }
});

const PalletColumn = withStyles(palletStyles)((props: LoadStationProps & WithStyles<typeof palletStyles>) => {
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
    .maxOn(x => x)
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
              <MoveMaterialArrowNode key={idx} type={MoveMaterialNodeKindType.Material} action={m.action}>
                <InProcMaterial mat={m} onOpen={props.openMat} />
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
          .sortOn(([face, data]) => face)
          .map(([face, data]) => (
            <div key={face}>
              <MoveMaterialArrowNode type={MoveMaterialNodeKindType.PalletFaceZone} face={face}>
                <WhiteboardRegion label={"Face " + face.toString()} spaceAround>
                  {data.map((m, idx) => (
                    <MoveMaterialArrowNode key={idx} type={MoveMaterialNodeKindType.Material} action={m.action}>
                      <InProcMaterial mat={m} onOpen={props.openMat} />
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
      <WhiteboardRegion label="Raw Material" spaceAround>
        {props.data.castings.map((m, idx) => (
          <MoveMaterialArrowNode key={idx} type={MoveMaterialNodeKindType.Material} action={m.action}>
            <InProcMaterial mat={m} onOpen={props.openMat} />
          </MoveMaterialArrowNode>
        ))}
      </WhiteboardRegion>
      <Divider />
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
            ) : (
              undefined
            )}
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

interface LoadMatDialogProps extends MaterialDialogProps {
  readonly openSelectWorkorder: (mat: matDetails.MaterialDetail) => void;
  readonly openSetSerial: () => void;
  readonly openForceInspection: () => void;
}

function instructionType(mat: matDetails.MaterialDetail): string {
  let openUnload = mat.events.find(e => e.result === "UNLOAD");
  if (openUnload.isSome() && openUnload.get().startofcycle) {
    return "unload";
  } else {
    return "load";
  }
}

function LoadMatDialog(props: LoadMatDialogProps) {
  function openAssignWorkorder() {
    if (!props.display_material) {
      return;
    }
    props.openSelectWorkorder(props.display_material);
  }
  return (
    <MaterialDialog
      display_material={props.display_material}
      onClose={props.onClose}
      buttons={
        <>
          {props.display_material && props.display_material.partName !== "" ? (
            <InstructionButton material={props.display_material} type={instructionType(props.display_material)} />
          ) : (
            undefined
          )}
          <Button color="primary" onClick={props.openSetSerial}>
            {props.display_material && props.display_material.serial ? "Change Serial" : "Assign Serial"}
          </Button>
          <Button color="primary" onClick={props.openForceInspection}>
            Signal Inspection
          </Button>
          <Button color="primary" onClick={openAssignWorkorder}>
            {props.display_material && props.display_material.workorderId ? "Change Workorder" : "Assign Workorder"}
          </Button>
        </>
      }
    />
  );
}

const ConnectedMaterialDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
    openSelectWorkorder: (mat: matDetails.MaterialDetail) =>
      [
        {
          type: guiState.ActionType.SetWorkorderDialogOpen,
          open: true
        },
        matDetails.loadWorkorders(mat)
      ] as AppActionBeforeMiddleware,
    openSetSerial: () => ({
      type: guiState.ActionType.SetSerialDialogOpen,
      open: true
    }),
    openForceInspection: () => ({
      type: guiState.ActionType.SetInspTypeDialogOpen,
      open: true
    })
  }
)(LoadMatDialog);

const loadStyles = createStyles({
  mainFillViewport: {
    height: "calc(100vh - 64px - 2.5em)",
    display: "flex",
    padding: "8px",
    width: "100%"
  },
  mainScrollable: {
    display: "flex",
    padding: "8px",
    width: "100%"
  },
  palCol: {
    flexGrow: 1,
    display: "flex",
    flexDirection: "column" as "column"
  },
  queueCol: {
    width: "16em",
    padding: "8px",
    display: "flex",
    flexDirection: "column" as "column",
    borderLeft: "1px solid rgba(0, 0, 0, 0.12)"
  }
});

interface LoadStationProps {
  readonly fillViewPort: boolean;
  readonly data: LoadStationAndQueueData;
  readonly dateOfCurrentStatus: Date;
  openMat: (m: Readonly<MaterialSummary>) => void;
  moveMaterialInQueue: (d: matDetails.AddExistingMaterialToQueueData) => void;
}

const LoadStation = withStyles(loadStyles)((props: LoadStationProps & WithStyles<typeof loadStyles>) => {
  const palProps = { ...props, classes: undefined };

  let queues = props.data.queues
    .toVector()
    .sortOn(([q, mats]) => q)
    .map(([q, mats]) => ({
      label: q,
      material: mats,
      isFree: false
    }));

  let cells = queues;
  if (props.data.free) {
    cells = queues.prepend({
      label: "In Process Material",
      material: props.data.free,
      isFree: true
    });
  }

  const col1 = cells.take(2);
  const col2 = cells.drop(2).take(2);

  return (
    <DocumentTitle title={"Load " + props.data.loadNum.toString() + " - FMS Insight"}>
      <MoveMaterialArrowContainer>
        <main
          data-testid="stationmonitor-load"
          className={props.fillViewPort ? props.classes.mainFillViewport : props.classes.mainScrollable}
        >
          <div className={props.classes.palCol}>
            <PalletColumn {...palProps} />
          </div>
          {col1.length() === 0 ? (
            undefined
          ) : (
            <div className={props.classes.queueCol}>
              {col1.zipWithIndex().map(([mat, idx]) => (
                <MoveMaterialArrowNode
                  key={idx}
                  {...(mat.isFree
                    ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
                    : {
                        type: MoveMaterialNodeKindType.QueueZone,
                        queue: mat.label
                      })}
                >
                  <SortableWhiteboardRegion
                    label={mat.label}
                    axis="y"
                    distance={5}
                    shouldCancelStart={() => false}
                    onSortEnd={(se: SortEnd) =>
                      props.moveMaterialInQueue({
                        materialId: mat.material[se.oldIndex].materialID,
                        queue: mat.label,
                        queuePosition: se.newIndex
                      })
                    }
                  >
                    {mat.material.map((m, matIdx) => (
                      <MoveMaterialArrowNode
                        key={matIdx}
                        type={MoveMaterialNodeKindType.Material}
                        action={
                          props.data.pallet && m.action.loadOntoPallet === props.data.pallet.pallet ? m.action : null
                        }
                      >
                        <SortableInProcMaterial
                          index={matIdx}
                          mat={m}
                          onOpen={props.openMat}
                          displaySinglePallet={props.data.pallet ? props.data.pallet.pallet : ""}
                        />
                      </MoveMaterialArrowNode>
                    ))}
                  </SortableWhiteboardRegion>
                </MoveMaterialArrowNode>
              ))}
            </div>
          )}
          {col2.length() === 0 ? (
            undefined
          ) : (
            <div className={props.classes.queueCol}>
              {col2.zipWithIndex().map(([mat, idx]) => (
                <MoveMaterialArrowNode
                  key={idx}
                  {...(mat.isFree
                    ? { type: MoveMaterialNodeKindType.FreeMaterialZone }
                    : {
                        type: MoveMaterialNodeKindType.QueueZone,
                        queue: mat.label
                      })}
                >
                  <SortableWhiteboardRegion
                    label={mat.label}
                    axis="y"
                    distance={5}
                    shouldCancelStart={() => false}
                    onSortEnd={(se: SortEnd) =>
                      props.moveMaterialInQueue({
                        materialId: mat.material[se.oldIndex].materialID,
                        queue: mat.label,
                        queuePosition: se.newIndex
                      })
                    }
                  >
                    {mat.material.map((m, matIdx) => (
                      <MoveMaterialArrowNode
                        key={matIdx}
                        type={MoveMaterialNodeKindType.Material}
                        action={
                          props.data.pallet && m.action.loadOntoPallet === props.data.pallet.pallet ? m.action : null
                        }
                      >
                        <SortableInProcMaterial
                          index={matIdx}
                          mat={m}
                          onOpen={props.openMat}
                          displaySinglePallet={props.data.pallet ? props.data.pallet.pallet : ""}
                        />
                      </MoveMaterialArrowNode>
                    ))}
                  </SortableWhiteboardRegion>
                </MoveMaterialArrowNode>
              ))}
            </div>
          )}
          <SelectWorkorderDialog />
          <SetSerialDialog />
          <SelectInspTypeDialog />
          <ConnectedMaterialDialog />
        </main>
      </MoveMaterialArrowContainer>
    </DocumentTitle>
  );
});

const buildLoadData = createSelector(
  (st: Store) => st.Current.current_status,
  (st: Store) => st.Route,
  (curStatus: Readonly<api.ICurrentStatus>, route: routes.State): LoadStationAndQueueData => {
    return selectLoadStationAndQueueProps(
      route.selected_load_id,
      route.load_queues,
      route.load_free_material,
      curStatus
    );
  }
);

export default connect(
  (st: Store) => ({
    data: buildLoadData(st),
    dateOfCurrentStatus: st.Current.current_status.timeOfCurrentStatusUTC
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
)(LoadStation);
