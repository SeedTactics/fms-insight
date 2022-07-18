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
import * as jdenticon from "jdenticon";
import { Typography } from "@mui/material";
import { ButtonBase } from "@mui/material";
import { Button } from "@mui/material";
import { Tooltip } from "@mui/material";
import { Avatar } from "@mui/material";
import { Paper } from "@mui/material";
import { CircularProgress } from "@mui/material";
import { TextField } from "@mui/material";
import TimeAgo from "react-timeago";
import { Dialog } from "@mui/material";
import { DialogActions } from "@mui/material";
import { DialogContent } from "@mui/material";
import { DialogTitle } from "@mui/material";
import { SortableElement, SortableContainer } from "react-sortable-hoc";
import { DraggableProvided } from "react-beautiful-dnd";

import { DragIndicator, Warning as WarningIcon, Search as SearchIcon } from "@mui/icons-material";

import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import { LogEntries } from "../LogEntry.js";
import {
  inproc_mat_to_summary,
  MaterialSummaryAndCompletedData,
} from "../../cell-status/material-summary.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentOperator } from "../../data/operators.js";
import { instructionUrl } from "../../network/backend.js";
import { useRecoilValue, useSetRecoilState } from "recoil";

export class PartIdenticon extends React.PureComponent<{
  part: string;
  size?: number;
}> {
  override render() {
    const iconSize = this.props.size || 50;
    const icon = jdenticon.toSvg(this.props.part, iconSize);

    return <div style={{ width: iconSize, height: iconSize }} dangerouslySetInnerHTML={{ __html: icon }} />;
  }
}

function materialAction(
  mat: Readonly<api.IInProcessMaterial>,
  displaySinglePallet?: string
): string | undefined {
  switch (mat.action.type) {
    case api.ActionType.Loading:
      switch (mat.location.type) {
        case api.LocType.OnPallet:
          if (displaySinglePallet === undefined || displaySinglePallet === mat.location.pallet) {
            if (mat.action.loadOntoFace === undefined || mat.action.loadOntoFace === mat.location.face) {
              // material is not moving, just having some manual work done on it
              return undefined;
            } else {
              return "Transfer to face " + (mat.action.loadOntoFace || 0).toString();
            }
          } else {
            return undefined;
          }
        default:
          if (displaySinglePallet === undefined) {
            return (
              "Load onto face " +
              (mat.action.loadOntoFace || 0).toString() +
              " of pal " +
              (mat.action.loadOntoPallet ?? "")
            );
          } else if (displaySinglePallet === mat.action.loadOntoPallet) {
            return "Load onto face " + (mat.action.loadOntoFace || 0).toString();
          } else {
            return undefined;
          }
      }

    case api.ActionType.UnloadToInProcess:
    case api.ActionType.UnloadToCompletedMaterial:
      if (mat.action.unloadIntoQueue) {
        return "Unload into queue " + mat.action.unloadIntoQueue;
      } else {
        return "Unload from pallet";
      }

    case api.ActionType.Waiting:
      if (mat.location.type === api.LocType.InQueue && !!mat.jobUnique && mat.jobUnique !== "") {
        return "Waiting; next process is #" + (mat.process + 1).toString();
      }
      break;
  }
  return undefined;
}

export interface MaterialSummaryProps {
  readonly mat: Readonly<MaterialSummaryAndCompletedData>; // TODO: deep readonly
  readonly action?: string;
  readonly focusInspectionType?: string | null;
  readonly hideInspectionIcon?: boolean;
  readonly displayJob?: boolean;
  readonly draggableProvided?: DraggableProvided;
  readonly hideAvatar?: boolean;
  readonly hideEmptySerial?: boolean;
  readonly isDragging?: boolean;
}

export const MatSummary = React.memo(function MatSummary(props: MaterialSummaryProps) {
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);

  const inspections = props.mat.signaledInspections.join(", ");
  const completed = props.mat.completedInspections || {};

  let completedMsg: JSX.Element | undefined;
  if (props.focusInspectionType && completed[props.focusInspectionType]) {
    completedMsg = (
      <small>
        <span>Inspection completed </span>
        <TimeAgo date={completed[props.focusInspectionType].time} />
      </small>
    );
  } else if (props.focusInspectionType && props.mat.last_unload_time) {
    completedMsg = (
      <small>
        <span>Unloaded </span>
        <TimeAgo date={props.mat.last_unload_time} />
      </small>
    );
  } else if (props.mat.wash_completed) {
    completedMsg = (
      <small>
        <span>Wash completed </span>
        <TimeAgo date={props.mat.wash_completed} />
      </small>
    );
  }

  const dragHandleProps = props.draggableProvided?.dragHandleProps;

  return (
    <Paper
      ref={props.draggableProvided?.innerRef}
      elevation={4}
      {...props.draggableProvided?.draggableProps}
      style={{
        display: "flex",
        minWidth: "10em",
        padding: "8px",
        margin: "8px",
        ...props.draggableProvided?.draggableProps.style,
      }}
    >
      {dragHandleProps ? (
        <div
          style={{ display: "flex", flexDirection: "column", justifyContent: "center" }}
          {...dragHandleProps}
        >
          <DragIndicator fontSize="large" color={props.isDragging ? "primary" : "action"} />
        </div>
      ) : undefined}
      <ButtonBase focusRipple onClick={() => setMatToShow({ type: "MatSummary", summary: props.mat })}>
        <div style={{ display: "flex", textAlign: "left" }}>
          <PartIdenticon part={props.mat.partName} />
          <div style={{ marginLeft: "8px", flexGrow: 1 }}>
            <Typography variant="h6">{props.mat.partName}</Typography>
            {props.displayJob ? (
              <div>
                <small>
                  {props.mat.jobUnique && props.mat.jobUnique !== ""
                    ? "Assigned to " + props.mat.jobUnique
                    : "Unassigned material"}
                </small>
              </div>
            ) : undefined}
            {!props.hideEmptySerial || props.mat.serial ? (
              <div>
                <small>Serial: {props.mat.serial ? props.mat.serial : "none"}</small>
              </div>
            ) : undefined}
            {props.mat.workorderId === undefined ||
            props.mat.workorderId === "" ||
            props.mat.workorderId === props.mat.serial ? undefined : (
              <div>
                <small>Workorder: {props.mat.workorderId}</small>
              </div>
            )}
            {props.action === undefined ? undefined : (
              <div>
                <small>{props.action}</small>
              </div>
            )}
            {completedMsg}
          </div>
          <div
            style={{
              marginLeft: "4px",
              display: "flex",
              flexDirection: "column",
              justifyContent: "space-between",
              alignItems: "flex-end",
            }}
          >
            {props.mat.serial && props.mat.serial.length >= 1 && !props.hideAvatar ? (
              <div>
                <Avatar style={{ width: "30px", height: "30px" }}>
                  {props.mat.serial.substr(props.mat.serial.length - 1, 1)}
                </Avatar>
              </div>
            ) : undefined}
            {props.hideInspectionIcon || props.mat.signaledInspections.length === 0 ? undefined : (
              <div>
                <Tooltip title={inspections}>
                  <WarningIcon />
                </Tooltip>
              </div>
            )}
          </div>
        </div>
      </ButtonBase>
    </Paper>
  );
});

export interface InProcMaterialProps {
  readonly mat: Readonly<api.IInProcessMaterial>; // TODO: deep readonly
  readonly displaySinglePallet?: string;
  readonly displayJob?: boolean;
  readonly draggableProvided?: DraggableProvided;
  readonly hideAvatar?: boolean;
  readonly hideEmptySerial?: boolean;
  readonly isDragging?: boolean;
}

export class InProcMaterial extends React.PureComponent<InProcMaterialProps> {
  override render() {
    return (
      <MatSummary
        mat={inproc_mat_to_summary(this.props.mat)}
        action={materialAction(this.props.mat, this.props.displaySinglePallet)}
        draggableProvided={this.props.draggableProvided}
        hideAvatar={this.props.hideAvatar}
        displayJob={this.props.displayJob}
        isDragging={this.props.isDragging}
        hideEmptySerial={this.props.hideEmptySerial}
      />
    );
  }
}

export const SortableInProcMaterial = SortableElement(InProcMaterial);

export interface MultiMaterialProps {
  readonly partOrCasting: string;
  readonly assignedJobUnique: string | null;
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  onOpen: () => void;
}

export const MultiMaterial = React.memo(function MultiMaterial(props: MultiMaterialProps) {
  return (
    <Paper elevation={4} style={{ display: "flex", minWidth: "10em", padding: "8px", margin: "8px" }}>
      <ButtonBase focusRipple onClick={() => props.onOpen()}>
        <div style={{ display: "flex", textAlign: "left" }}>
          <PartIdenticon part={props.partOrCasting} />
          <div style={{ marginLeft: "8px", flexGrow: 1 }}>
            <Typography variant="h6">{props.partOrCasting}</Typography>
            <div>
              <small>
                {props.assignedJobUnique && props.assignedJobUnique !== ""
                  ? "Assigned to " + props.assignedJobUnique
                  : "Unassigned material"}
              </small>
            </div>
          </div>
          <div
            style={{
              marginLeft: "4px",
              display: "flex",
              flexDirection: "column",
              justifyContent: "space-between",
              alignItems: "flex-end",
            }}
          >
            <div>
              <Avatar style={{ width: "37px", height: "30px", backgroundColor: "#757575" }}>
                {props.material.length > 100
                  ? props.material.length.toString()
                  : "x" + props.material.length.toString()}
              </Avatar>
            </div>
          </div>
        </div>
      </ButtonBase>
    </Paper>
  );
});

export class MaterialDetailTitle extends React.PureComponent<{
  partName: string;
  serial?: string;
  subtitle?: string;
  notes?: boolean;
}> {
  override render() {
    let title;
    if (this.props.partName === "" && (this.props.serial === undefined || this.props.serial === "")) {
      title = "Loading";
    } else if (this.props.partName === "") {
      title = "Loading " + (this.props.serial ?? "");
    } else if (this.props.serial === undefined || this.props.serial === "") {
      if (this.props.notes) {
        title = "Add note for " + this.props.partName;
      } else {
        title = this.props.partName;
      }
    } else {
      if (this.props.notes) {
        title = "Add note for " + this.props.serial;
      } else {
        title = this.props.partName + " - " + this.props.serial;
      }
    }

    return (
      <div style={{ display: "flex", textAlign: "left" }}>
        {this.props.partName === "" ? <SearchIcon /> : <PartIdenticon part={this.props.partName} />}
        <div style={{ marginLeft: "8px", flexGrow: 1 }}>
          <Typography variant="h6">{title}</Typography>
          {this.props.subtitle ? <Typography variant="caption">{this.props.subtitle}</Typography> : undefined}
        </div>
      </div>
    );
  }
}

export interface MaterialDetailProps {
  readonly mat: matDetails.MaterialDetail;
  readonly highlightProcess?: number;
}

export class MaterialDetailContent extends React.PureComponent<MaterialDetailProps> {
  override render() {
    const mat = this.props.mat;
    function colorForInspType(type: string): string {
      if (mat.completedInspections && mat.completedInspections.includes(type)) {
        return "black";
      } else {
        return "red";
      }
    }
    return (
      <>
        <div style={{ marginLeft: "1em" }}>
          <div>
            <small>Workorder: {mat.workorderId || "none"}</small>
          </div>
          <div>
            <small>Inspections: </small>
            {mat.signaledInspections.length === 0 ? (
              <small>none</small>
            ) : (
              mat.signaledInspections.map((type, i) => (
                <span key={i}>
                  <small>{i === 0 ? "" : ", "}</small>
                  <small style={{ color: colorForInspType(type) }}>{type}</small>
                </span>
              ))
            )}
          </div>
        </div>
        {mat.loading_events ? (
          <CircularProgress data-testid="material-events-loading" color="secondary" />
        ) : (
          <LogEntries entries={mat.events} copyToClipboard highlightProcess={this.props.highlightProcess} />
        )}
      </>
    );
  }
}

export function InstructionButton({
  material,
  type,
  operator,
  pallet,
}: {
  readonly material: matDetails.MaterialDetail;
  readonly type: string;
  readonly operator: string | null;
  readonly pallet: string | null;
}) {
  const maxProc =
    LazySeq.ofIterable(material.events)
      .filter(
        (e) =>
          e.details?.["PalletCycleInvalidated"] !== "1" &&
          (e.type === api.LogType.LoadUnloadCycle ||
            e.type === api.LogType.MachineCycle ||
            e.type === api.LogType.AddToQueue)
      )
      .flatMap((e) => e.material)
      .filter((e) => e.id === material.materialID)
      .maxBy((e) => e.proc)?.proc ?? null;
  const url = instructionUrl(material.partName, type, material.materialID, pallet, maxProc, operator);
  return (
    <Button href={url} target="bms-instructions" color="primary">
      Instructions
    </Button>
  );
}

interface NotesDialogBodyProps {
  mat: matDetails.MaterialDetail;
  setNotesOpen: (o: boolean) => void;
}

function NotesDialogBody(props: NotesDialogBodyProps) {
  const [curNote, setCurNote] = React.useState<string>("");
  const operator = useRecoilValue(currentOperator);
  const [addNote] = matDetails.useAddNote();

  return (
    <>
      <DialogTitle>
        <MaterialDetailTitle notes partName={props.mat.partName} serial={props.mat.serial} />
      </DialogTitle>
      <DialogContent>
        <TextField
          sx={{ mt: "5px" }}
          multiline
          label="Note"
          autoFocus
          variant="outlined"
          value={curNote}
          onChange={(e) => setCurNote(e.target.value)}
        />
      </DialogContent>
      <DialogActions>
        <Button
          onClick={() => {
            addNote({ matId: props.mat.materialID, process: 0, operator: operator, notes: curNote });
            props.setNotesOpen(false);
            setCurNote("");
          }}
          disabled={curNote === ""}
          color="secondary"
        >
          Save
        </Button>
        <Button
          onClick={() => {
            props.setNotesOpen(false);
            setCurNote("");
          }}
          color="secondary"
        >
          Cancel
        </Button>
      </DialogActions>
    </>
  );
}

export interface MaterialDialogProps {
  display_material: matDetails.MaterialDetail | null;
  buttons?: JSX.Element;
  onClose: () => void;
  allowNote?: boolean;
  extraDialogElements?: JSX.Element;
  highlightProcess?: number;
}

export function MaterialDialog(props: MaterialDialogProps) {
  const [notesOpen, setNotesOpen] = React.useState<boolean>(false);

  let body: JSX.Element | undefined;
  let notesBody: JSX.Element | undefined;
  if (props.display_material === null) {
    body = <p>None</p>;
    notesBody = <p>None</p>;
  } else {
    const mat = props.display_material;
    body = (
      <>
        <DialogTitle>
          <MaterialDetailTitle partName={mat.partName} serial={mat.serial} />
        </DialogTitle>
        <DialogContent>
          <MaterialDetailContent mat={mat} highlightProcess={props.highlightProcess} />
        </DialogContent>
        {props.extraDialogElements}
        <DialogActions>
          {props.allowNote ? (
            <Button onClick={() => setNotesOpen(true)} color="primary">
              Add Note
            </Button>
          ) : undefined}
          {props.buttons}
          <Button onClick={props.onClose} color="secondary">
            Close
          </Button>
        </DialogActions>
      </>
    );
    if (props.allowNote) {
      notesBody = <NotesDialogBody mat={mat} setNotesOpen={setNotesOpen} />;
    }
  }
  return (
    <>
      <Dialog open={props.display_material !== null} onClose={props.onClose} maxWidth="md">
        {body}
      </Dialog>
      {props.allowNote ? (
        <Dialog open={notesOpen} onClose={() => setNotesOpen(false)} maxWidth="md">
          {notesBody}
        </Dialog>
      ) : undefined}
    </>
  );
}

export const BasicMaterialDialog = React.memo(function BasicMaterialDialog() {
  const mat = useRecoilValue(matDetails.materialDetail);
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);
  const close = React.useCallback(() => setMatToShow(null), []);
  return <MaterialDialog display_material={mat} onClose={close} />;
});

export interface WhiteboardRegionProps {
  readonly children?: React.ReactNode;
  readonly label: string;
  readonly spaceAround?: boolean;
  readonly flexStart?: boolean;
  readonly borderLeft?: boolean;
  readonly borderBottom?: boolean;
  readonly borderRight?: boolean;
  readonly addMaterialButton?: JSX.Element;
}

export const WhiteboardRegion = React.memo(function WhiteboardRegion(props: WhiteboardRegionProps) {
  let justifyContent = "space-between";
  if (props.spaceAround) {
    justifyContent = "space-around";
  } else if (props.flexStart) {
    justifyContent = "flex-start";
  }
  return (
    <div
      style={{
        width: "100%",
        minHeight: "70px",
        borderLeft: props.borderLeft ? "1px solid rgba(0,0,0,0.12)" : undefined,
        borderBottom: props.borderBottom ? "1px solid rgba(0,0,0,0.12)" : undefined,
        borderRight: props.borderRight ? "1px solid rgba(0,0,0,0.12)" : undefined,
      }}
    >
      {props.label !== "" || props.addMaterialButton ? (
        <div style={{ display: "flex" }}>
          <span
            style={{
              color: "rgba(0,0,0,0.5)",
              fontSize: "small",
              flexGrow: 1,
            }}
          >
            {props.label}
          </span>
          {props.addMaterialButton}
        </div>
      ) : undefined}
      <div style={{ justifyContent, width: "100%", display: "flex", flexWrap: "wrap" }}>{props.children}</div>
    </div>
  );
});

export const SortableWhiteboardRegion = SortableContainer(WhiteboardRegion);
