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
import Typography from "@material-ui/core/Typography";
import ButtonBase from "@material-ui/core/ButtonBase";
import Button from "@material-ui/core/Button";
import Tooltip from "@material-ui/core/Tooltip";
import WarningIcon from "@material-ui/icons/Warning";
import SearchIcon from "@material-ui/icons/Search";
import Avatar from "@material-ui/core/Avatar";
import Paper from "@material-ui/core/Paper";
import CircularProgress from "@material-ui/core/CircularProgress";
import TextField from "@material-ui/core/TextField";
import TimeAgo from "react-timeago";
import Dialog from "@material-ui/core/Dialog";
import DialogActions from "@material-ui/core/DialogActions";
import DialogContent from "@material-ui/core/DialogContent";
import DialogTitle from "@material-ui/core/DialogTitle";
import DragIndicator from "@material-ui/icons/DragIndicator";
import { WithStyles, createStyles, withStyles } from "@material-ui/core/styles";
import { SortableElement, SortableContainer } from "react-sortable-hoc";
import { DraggableProvided } from "react-beautiful-dnd";

import * as api from "../../data/api";
import * as matDetails from "../../data/material-details";
import { LogEntries } from "../LogEntry";
import { connect, mkAC } from "../../store/store";
import { inproc_mat_to_summary, MaterialSummaryAndCompletedData, MaterialSummary } from "../../data/events.matsummary";
import { LazySeq } from "../../data/lazyseq";
import { currentOperator } from "../../data/operators";
import { instructionUrl } from "../../data/backend";

/*
function getPosition(el: Element) {
  const box = el.getBoundingClientRect();
  const doc = document.documentElement;
  const body = document.body;
  var clientTop  = doc.clientTop  || body.clientTop  || 0;
  var clientLeft = doc.clientLeft || body.clientLeft || 0;
  var scrollTop  = window.pageYOffset || doc.scrollTop;
  var scrollLeft = window.pageXOffset || doc.scrollLeft;
  return {
    top: box.top  + scrollTop  - clientTop,
    left: box.left + scrollLeft - clientLeft
  };
}*/

export class PartIdenticon extends React.PureComponent<{
  part: string;
  size?: number;
}> {
  render() {
    const iconSize = this.props.size || 50;
    const icon = jdenticon.toSvg(this.props.part, iconSize);

    return <div style={{ width: iconSize, height: iconSize }} dangerouslySetInnerHTML={{ __html: icon }} />;
  }
}

function materialAction(mat: Readonly<api.IInProcessMaterial>, displaySinglePallet?: string): string | undefined {
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
              "Load onto face " + (mat.action.loadOntoFace || 0).toString() + " of pal " + mat.action.loadOntoPallet
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

const matStyles = createStyles({
  paper: {
    minWidth: "10em",
    padding: "8px",
    margin: "8px",
  },
  container: {
    display: "flex" as "flex",
    textAlign: "left" as "left",
  },
  mainContent: {
    marginLeft: "8px",
    flexGrow: 1,
  },
  rightContent: {
    marginLeft: "4px",
    display: "flex",
    "flex-direction": "column",
    "justify-content": "space-between",
    "align-items": "flex-end",
  },
  avatar: {
    width: "30px",
    height: "30px",
  },
  avatarCount: {
    backgroundColor: "#757575",
    width: "37px",
  },
});

export interface MaterialSummaryProps {
  readonly mat: Readonly<MaterialSummaryAndCompletedData>; // TODO: deep readonly
  readonly action?: string;
  readonly focusInspectionType?: string;
  readonly hideInspectionIcon?: boolean;
  readonly displayJob?: boolean;
  readonly draggableProvided?: DraggableProvided;
  readonly hideAvatar?: boolean;
  readonly hideEmptySerial?: boolean;
  readonly isDragging?: boolean;
  onOpen: (m: Readonly<MaterialSummary>) => void;
}

const MatSummaryWithStyles = withStyles(matStyles)((props: MaterialSummaryProps & WithStyles<typeof matStyles>) => {
  const inspections = props.mat.signaledInspections.join(", ");
  const completed = props.mat.completedInspections || {};

  let completedMsg: JSX.Element | undefined;
  if (props.focusInspectionType && completed[props.focusInspectionType]) {
    completedMsg = (
      <small>
        <span>Inspection completed </span>
        <TimeAgo date={completed[props.focusInspectionType]} />
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
      className={props.classes.paper}
      {...props.draggableProvided?.draggableProps}
      style={{
        display: "flex",
        ...props.draggableProvided?.draggableProps.style,
      }}
    >
      {dragHandleProps ? (
        <div style={{ display: "flex", flexDirection: "column", justifyContent: "center" }} {...dragHandleProps}>
          <DragIndicator fontSize="large" color={props.isDragging ? "primary" : "action"} />
        </div>
      ) : undefined}
      <ButtonBase focusRipple onClick={() => props.onOpen(props.mat)}>
        <div className={props.classes.container}>
          <PartIdenticon part={props.mat.partName} />
          <div className={props.classes.mainContent}>
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
          <div className={props.classes.rightContent}>
            {props.mat.serial && props.mat.serial.length >= 1 && !props.hideAvatar ? (
              <div>
                <Avatar className={props.classes.avatar}>
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

// decorate doesn't work well with classes yet.
// https://github.com/Microsoft/TypeScript/issues/4881
export class MatSummary extends React.PureComponent<MaterialSummaryProps> {
  render() {
    return <MatSummaryWithStyles {...this.props} />;
  }
}

export interface InProcMaterialProps {
  readonly mat: Readonly<api.IInProcessMaterial>; // TODO: deep readonly
  readonly displaySinglePallet?: string;
  readonly displayJob?: boolean;
  readonly draggableProvided?: DraggableProvided;
  readonly hideAvatar?: boolean;
  readonly hideEmptySerial?: boolean;
  readonly isDragging?: boolean;
  onOpen: (m: Readonly<MaterialSummary>) => void;
}

export class InProcMaterial extends React.PureComponent<InProcMaterialProps> {
  render() {
    return (
      <MatSummaryWithStyles
        mat={inproc_mat_to_summary(this.props.mat)}
        action={materialAction(this.props.mat, this.props.displaySinglePallet)}
        onOpen={this.props.onOpen}
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

const MultiMaterialWithStyles = withStyles(matStyles)((props: MultiMaterialProps & WithStyles<typeof matStyles>) => {
  return (
    <Paper elevation={4} className={props.classes.paper} style={{ display: "flex" }}>
      <ButtonBase focusRipple onClick={() => props.onOpen()}>
        <div className={props.classes.container}>
          <PartIdenticon part={props.partOrCasting} />
          <div className={props.classes.mainContent}>
            <Typography variant="h6">{props.partOrCasting}</Typography>
            <div>
              <small>
                {props.assignedJobUnique && props.assignedJobUnique !== ""
                  ? "Assigned to " + props.assignedJobUnique
                  : "Unassigned material"}
              </small>
            </div>
          </div>
          <div className={props.classes.rightContent}>
            <div>
              <Avatar className={props.classes.avatar + " " + props.classes.avatarCount}>
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

// decorate doesn't work well with classes yet.
// https://github.com/Microsoft/TypeScript/issues/4881
export class MultiMaterial extends React.PureComponent<MultiMaterialProps> {
  render() {
    return <MultiMaterialWithStyles {...this.props} />;
  }
}

export class MaterialDetailTitle extends React.PureComponent<{
  partName: string;
  serial?: string;
  subtitle?: string;
  notes?: boolean;
}> {
  render() {
    let title;
    if (this.props.partName === "" && (this.props.serial === undefined || this.props.serial === "")) {
      title = "Loading";
    } else if (this.props.partName === "") {
      title = "Loading " + this.props.serial;
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
  render() {
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
  const maxProc = LazySeq.ofIterable(material.events)
    .filter(
      (e) =>
        e.details?.["PalletCycleInvalidated"] !== "1" &&
        (e.type === api.LogType.LoadUnloadCycle ||
          e.type === api.LogType.MachineCycle ||
          e.type === api.LogType.AddToQueue)
    )
    .flatMap((e) => e.material)
    .filter((e) => e.id === material.materialID)
    .maxOn((e) => e.proc)
    .map((e) => e.proc);
  const url = instructionUrl(material.partName, type, material.materialID, pallet, maxProc.getOrNull(), operator);
  return (
    <Button href={url} target="bms-instructions" color="primary">
      Instructions
    </Button>
  );
}

interface NotesDialogBodyProps {
  mat: matDetails.MaterialDetail;
  operator: string | null;
  setNotesOpen: (o: boolean) => void;
  addNote: (matId: number, process: number, operator: string | null, notes: string) => void;
}

function NotesDialogBody(props: NotesDialogBodyProps) {
  const [curNote, setCurNote] = React.useState<string>("");

  return (
    <>
      <DialogTitle disableTypography>
        <MaterialDetailTitle notes partName={props.mat.partName} serial={props.mat.serial} />
      </DialogTitle>
      <DialogContent>
        <TextField
          multiline
          label="Notes"
          autoFocus
          variant="outlined"
          value={curNote}
          onChange={(e) => setCurNote(e.target.value)}
        />
      </DialogContent>
      <DialogActions>
        <Button
          onClick={() => {
            props.addNote(props.mat.materialID, 0, props.operator, curNote);
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

const ConnectedNotesDialogBody = connect(
  (st) => ({
    operator: currentOperator(st),
  }),
  {
    addNote: matDetails.addNote,
  }
)(NotesDialogBody);

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
        <DialogTitle disableTypography>
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
      notesBody = <ConnectedNotesDialogBody mat={mat} setNotesOpen={setNotesOpen} />;
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

export const BasicMaterialDialog = connect(
  (st) => ({
    display_material: st.MaterialDetails.material,
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
  }
)(MaterialDialog);

const whiteboardRegionStyle = createStyles({
  container: {
    width: "100%",
    minHeight: "70px",
  },
  labelContainer: {
    display: "flex",
  },
  label: {
    color: "rgba(0,0,0,0.5)",
    fontSize: "small",
    flexGrow: 1,
  },
  contentContainer: {
    width: "100%",
    display: "flex",
    flexWrap: "wrap" as "wrap",
  },
  borderLeft: {
    borderLeft: "1px solid rgba(0,0,0,0.12)",
  },
  borderBottom: {
    borderBottom: "1px solid rgba(0,0,0,0.12)",
  },
  borderRight: {
    borderRight: "1px solid rgba(0,0,0,0.12)",
  },
});

export interface WhiteboardRegionProps {
  readonly label: string;
  readonly spaceAround?: boolean;
  readonly flexStart?: boolean;
  readonly borderLeft?: boolean;
  readonly borderBottom?: boolean;
  readonly borderRight?: boolean;
  readonly addMaterialButton?: JSX.Element;
}

const WhiteboardRegionWithStyle = withStyles(whiteboardRegionStyle)(
  (props: WhiteboardRegionProps & WithStyles<typeof whiteboardRegionStyle> & { children?: React.ReactNode }) => {
    let justifyContent = "space-between";
    if (props.spaceAround) {
      justifyContent = "space-around";
    } else if (props.flexStart) {
      justifyContent = "flex-start";
    }
    const mainClasses = [props.classes.container];
    if (props.borderLeft) {
      mainClasses.push(props.classes.borderLeft);
    }
    if (props.borderBottom) {
      mainClasses.push(props.classes.borderBottom);
    }
    if (props.borderRight) {
      mainClasses.push(props.classes.borderRight);
    }
    return (
      <div className={mainClasses.join(" ")}>
        {props.label !== "" || props.addMaterialButton ? (
          <div className={props.classes.labelContainer}>
            <span className={props.classes.label}>{props.label}</span>
            {props.addMaterialButton}
          </div>
        ) : undefined}
        <div className={props.classes.contentContainer} style={{ justifyContent }}>
          {props.children}
        </div>
      </div>
    );
  }
);

// decorate doesn't work well with classes yet.
// https://github.com/Microsoft/TypeScript/issues/4881
export class WhiteboardRegion extends React.PureComponent<WhiteboardRegionProps> {
  render() {
    return <WhiteboardRegionWithStyle {...this.props} />;
  }
}

export const SortableWhiteboardRegion = SortableContainer(WhiteboardRegion);
