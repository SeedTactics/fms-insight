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

/* eslint-disable react/prop-types */
/* eslint-disable react/display-name */
import * as React from "react";
import * as jdenticon from "jdenticon";
import {
  Typography,
  Badge,
  Box,
  ButtonBase,
  Button,
  Tooltip,
  Avatar,
  Paper,
  CircularProgress,
  TextField,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  keyframes,
  styled,
} from "@mui/material";
import TimeAgo from "react-timeago";
import { DragIndicator, Warning as WarningIcon, Search as SearchIcon } from "@mui/icons-material";
import { useSortable } from "@dnd-kit/sortable";
import { useRecoilValue, useRecoilValueLoadable } from "recoil";

import * as api from "../../network/api.js";
import * as matDetails from "../../cell-status/material-details.js";
import { LogEntries } from "../LogEntry.js";
import {
  inproc_mat_to_summary,
  MaterialSummaryAndCompletedData,
} from "../../cell-status/material-summary.js";
import { currentOperator } from "../../data/operators.js";
import { DisplayLoadingAndError } from "../ErrorsAndLoading.js";

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

export function materialAction(
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
              if (mat.action.processAfterLoad && mat.action.processAfterLoad !== mat.process) {
                return "Reclamp material to process #" + mat.action.processAfterLoad.toString();
              } else {
                return undefined;
              }
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
      } else if (
        mat.location.type === api.LocType.OnPallet &&
        (mat.lastCompletedMachiningRouteStopIndex === null ||
          mat.lastCompletedMachiningRouteStopIndex === undefined)
      ) {
        return "Waiting for machining";
      }
      break;

    case api.ActionType.Machining:
      return "Machining program " + (mat.action.program ?? "");
  }
  return undefined;
}

const shakeSize = 2;
const shakeHorizKeyframes = keyframes`
  from, to { transform: translate(0, 0) }
  10% { transform: translate(${shakeSize}px, 0) }
  20% { transform: translate(0, 0) }
  30% { transform: translate(${shakeSize}px, 0) }
  40% { transform: translate(0, 0) }
  50% { transform: translate(${shakeSize}px, 0) }
  60% { transform: translate(0, 0) }
`;
const shakeHorizAnimation = `${shakeHorizKeyframes} 1s ease-in-out infinite`;

// global sync of all shake animations
function shakeAnimationIteration(event: React.AnimationEvent<HTMLDivElement>) {
  const anim = event.currentTarget
    .getAnimations()
    .find((a) => (a as CSSAnimation).animationName === shakeHorizKeyframes.name);
  if (anim && anim.startTime) {
    // the start time can drift due to the pause on hover, so to keep it in sync always
    // round the start time down to be a multiple of the duration (1s)
    anim.startTime = anim.startTime - (anim.startTime % 1000);
  }
}

export type MatCardFontSize = "normal" | "large" | "x-large";

const MatCardHeader = styled("div", { shouldForwardProp: (prop) => prop !== "fsize" })<{
  fsize?: MatCardFontSize;
}>(({ fsize, theme }) => {
  if (!fsize) return { fontSize: "1.25rem" };
  switch (fsize) {
    case "normal":
      return { fontSize: "1.5rem" };
    case "large":
      return {
        fontSize: "1.5rem",
        [theme.breakpoints.up("lg")]: {
          fontSize: "1.75rem",
        },
        [theme.breakpoints.up("xl")]: {
          fontSize: "2rem",
        },
      };
    case "x-large":
      return {
        fontSize: "1.5rem",
        [theme.breakpoints.up("md")]: {
          fontSize: "1.75rem",
        },
        [theme.breakpoints.up("lg")]: {
          fontSize: "2.75rem",
        },
        [theme.breakpoints.up("xl")]: {
          fontSize: "3.75rem",
        },
      };
  }
});

const MatCardDetail = styled("div", { shouldForwardProp: (prop) => prop !== "fsize" })<{
  fsize?: MatCardFontSize;
}>(({ fsize, theme }) => {
  if (!fsize) return { fontSize: "0.75rem" };
  switch (fsize) {
    case "normal":
      return { fontSize: "1rem" };
    case "large":
      return {
        fontSize: "1rem",
        [theme.breakpoints.up("lg")]: {
          fontSize: "1.5rem",
        },
        [theme.breakpoints.up("xl")]: {
          fontSize: "1.75rem",
        },
      };
    case "x-large":
      return {
        fontSize: "1rem",
        [theme.breakpoints.up("md")]: {
          fontSize: "1.5rem",
        },
        [theme.breakpoints.up("lg")]: {
          fontSize: "2.5rem",
        },
        [theme.breakpoints.up("xl")]: {
          fontSize: "3.5rem",
        },
      };
  }
});

interface MaterialDragProps {
  readonly dragRootProps?: React.HTMLAttributes<HTMLDivElement>;
  readonly showDragHandle?: boolean;
  readonly dragHandleProps?: React.HTMLAttributes<HTMLDivElement>;
  readonly setDragHandleRef?: React.RefCallback<HTMLDivElement>;
  readonly isDragOverlay?: boolean;
  readonly isActiveDrag?: boolean;
  readonly shake?: boolean;
}

export interface MaterialSummaryProps {
  readonly mat: Readonly<MaterialSummaryAndCompletedData>;
  readonly inProcMat?: Readonly<api.IInProcessMaterial>;
  readonly fsize?: MatCardFontSize;
  readonly action?: string;
  readonly focusInspectionType?: string | null;
  readonly hideInspectionIcon?: boolean;
  readonly displayJob?: boolean;
  readonly hideAvatar?: boolean;
  readonly hideEmptySerial?: boolean;
}

const MatCard = React.forwardRef(function MatCard(
  props: MaterialSummaryProps & MaterialDragProps,
  ref: React.ForwardedRef<HTMLDivElement>
) {
  const setMatToShow = matDetails.useSetMaterialToShowInDialog();

  const inspections = props.mat.signaledInspections.join(", ");
  const completed = props.mat.completedInspections || {};

  let completedMsg: JSX.Element | undefined;
  if (props.focusInspectionType && completed[props.focusInspectionType]) {
    completedMsg = (
      <MatCardDetail fsize={props.fsize}>
        <span>Inspection completed </span>
        <TimeAgo date={completed[props.focusInspectionType].time} />
      </MatCardDetail>
    );
  } else if (props.focusInspectionType && props.mat.last_unload_time) {
    completedMsg = (
      <MatCardDetail fsize={props.fsize}>
        <span>Unloaded </span>
        <TimeAgo date={props.mat.last_unload_time} />
      </MatCardDetail>
    );
  } else if (props.mat.closeout_completed) {
    completedMsg = (
      <MatCardDetail fsize={props.fsize}>
        <span>Closed Out </span>
        <TimeAgo date={props.mat.closeout_completed} />
      </MatCardDetail>
    );
  }

  return (
    <Paper
      ref={ref}
      elevation={4}
      sx={{
        display: "flex",
        minWidth: "10em",
        padding: "8px",
        margin: props.isDragOverlay ? undefined : "8px",
        opacity: props.isActiveDrag ? 0.2 : 1,
        animation: props.shake ? shakeHorizAnimation : undefined,
        "&:hover": {
          animationPlayState: "paused",
        },
      }}
      onAnimationIteration={shakeAnimationIteration}
      {...props.dragRootProps}
    >
      {props.showDragHandle ? (
        <div
          ref={props.setDragHandleRef}
          role="button"
          tabIndex={0}
          style={{
            display: "flex",
            flexDirection: "column",
            justifyContent: "center",
            cursor: props.isDragOverlay ? "grabbing" : "grab",
            touchAction: "none",
          }}
          {...props.dragHandleProps}
        >
          <DragIndicator fontSize="large" color="action" />
        </div>
      ) : undefined}
      <ButtonBase
        focusRipple
        sx={{ width: "100%" }}
        onClick={() =>
          setMatToShow(
            props.inProcMat
              ? { type: "InProcMat", inproc: props.inProcMat }
              : { type: "MatSummary", summary: props.mat }
          )
        }
      >
        <Box display="flex" textAlign="left" alignItems="center" width="100%">
          <PartIdenticon part={props.mat.partName} />
          <Box marginLeft="8px" flexGrow={1}>
            <MatCardHeader fsize={props.fsize}>{props.mat.partName}</MatCardHeader>
            {props.displayJob ? (
              <MatCardDetail fsize={props.fsize}>
                {props.mat.jobUnique && props.mat.jobUnique !== ""
                  ? "Assigned to " + props.mat.jobUnique
                  : "Unassigned material"}
              </MatCardDetail>
            ) : undefined}
            {!props.hideEmptySerial || props.mat.serial ? (
              <MatCardDetail fsize={props.fsize}>
                Serial: {props.mat.serial ? props.mat.serial : "none"}
              </MatCardDetail>
            ) : undefined}
            {props.mat.workorderId === undefined ||
            props.mat.workorderId === "" ||
            props.mat.workorderId === props.mat.serial ? undefined : (
              <MatCardDetail fsize={props.fsize}>Workorder: {props.mat.workorderId}</MatCardDetail>
            )}
            {props.action === undefined ? undefined : (
              <MatCardDetail fsize={props.fsize}>{props.action}</MatCardDetail>
            )}
            {completedMsg}
          </Box>
          <Box
            marginLeft="4px"
            display="flex"
            flexDirection="column"
            justifyContent="space-between"
            alignItems="flex-end"
            alignSelf="start"
          >
            {props.mat.serial && props.mat.serial.length >= 1 && !props.hideAvatar ? (
              <div>
                <Avatar style={{ width: "30px", height: "30px" }}>{props.mat.serial.slice(-1)}</Avatar>
              </div>
            ) : undefined}
            {props.hideInspectionIcon || props.mat.signaledInspections.length === 0 ? undefined : (
              <div>
                <Tooltip title={inspections}>
                  <WarningIcon />
                </Tooltip>
              </div>
            )}
          </Box>
        </Box>
      </ButtonBase>
    </Paper>
  );
});

export const MatSummary: React.ComponentType<MaterialSummaryProps> = React.memo(MatCard);

export type InProcMaterialProps = {
  readonly mat: Readonly<api.IInProcessMaterial>;
  readonly fsize?: MatCardFontSize;
  readonly displaySinglePallet?: string;
  readonly displayJob?: boolean;
  readonly hideAvatar?: boolean;
  readonly hideEmptySerial?: boolean;
};

export type ShakeProp = {
  readonly shake?: boolean;
};

export const InProcMaterial = React.memo(function InProcMaterial(
  props: InProcMaterialProps & ShakeProp & { readonly showHandle?: boolean }
) {
  return (
    <MatCard
      mat={inproc_mat_to_summary(props.mat)}
      inProcMat={props.mat}
      action={materialAction(props.mat, props.displaySinglePallet)}
      fsize={props.fsize}
      hideAvatar={props.hideAvatar}
      displayJob={props.displayJob}
      showDragHandle={props.showHandle}
      hideEmptySerial={props.hideEmptySerial}
      shake={props.shake}
    />
  );
});

export type SortableMatData = {
  readonly mat: Readonly<api.IInProcessMaterial>;
};

export const SortableInProcMaterial = React.memo(function SortableInProcMaterial(
  props: InProcMaterialProps & ShakeProp
) {
  const d: SortableMatData = { mat: props.mat };
  const {
    active,
    isDragging,
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
  } = useSortable({
    id: props.mat.materialID,
    data: d,
  });

  const handleProps: { [key: string]: unknown } = {
    ...listeners,
  };
  for (const [a, v] of Object.entries(attributes)) {
    if (a.startsWith("aria")) {
      handleProps[a] = v;
    }
  }
  const style = {
    transform: transform
      ? `translate3d(${Math.round(transform.x)}px, ${Math.round(transform.y)}px, 0)`
      : undefined,
    transition: active !== null ? transition : undefined,
  };

  return (
    <MatCard
      ref={setNodeRef}
      dragRootProps={{ style }}
      showDragHandle={true}
      dragHandleProps={handleProps}
      setDragHandleRef={setActivatorNodeRef}
      isActiveDrag={isDragging}
      mat={inproc_mat_to_summary(props.mat)}
      inProcMat={props.mat}
      action={materialAction(props.mat, props.displaySinglePallet)}
      hideAvatar={props.hideAvatar}
      displayJob={props.displayJob}
      hideEmptySerial={props.hideEmptySerial}
      fsize={props.fsize}
      shake={active ? undefined : props.shake}
    />
  );
});

export function DragOverlayInProcMaterial(props: InProcMaterialProps) {
  return (
    <MatCard
      mat={inproc_mat_to_summary(props.mat)}
      inProcMat={props.mat}
      action={materialAction(props.mat, props.displaySinglePallet)}
      showDragHandle={true}
      hideAvatar={props.hideAvatar}
      displayJob={props.displayJob}
      fsize={props.fsize}
      hideEmptySerial={props.hideEmptySerial}
      isDragOverlay
    />
  );
}

export interface MultiMaterialProps {
  readonly partOrCasting: string;
  readonly fsize?: MatCardFontSize;
  readonly assignedJobUnique: string | null;
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  onOpen: () => void;
}

export const MultiMaterial = React.memo(function MultiMaterial(props: MultiMaterialProps) {
  return (
    <Paper elevation={4} sx={{ display: "flex", minWidth: "10em", padding: "8px", margin: "8px" }}>
      <Badge badgeContent={props.material.length < 2 ? 0 : props.material.length} color="secondary">
        <ButtonBase focusRipple onClick={() => props.onOpen()}>
          <Box display="flex" textAlign="left">
            <PartIdenticon part={props.partOrCasting} />
            <Box marginLeft="8px" flexGrow={1}>
              <Typography variant="h6">{props.partOrCasting}</Typography>
              <MatCardDetail fsize={props.fsize}>
                {props.assignedJobUnique && props.assignedJobUnique !== ""
                  ? "Assigned to " + props.assignedJobUnique
                  : "Unassigned material"}
              </MatCardDetail>
            </Box>
            {props.material.length > 0 && props.material[0].serial && props.material[0].serial.length >= 1 ? (
              <div>
                <Avatar style={{ width: "30px", height: "30px" }}>
                  {props.material[0].serial.slice(-1)}
                </Avatar>
              </div>
            ) : undefined}
          </Box>
        </ButtonBase>
      </Badge>
    </Paper>
  );
});

export const MaterialDetailTitle = React.memo(function MaterialDetailTitle({
  partName,
  serial,
  subtitle,
  notes,
}: {
  partName: string;
  serial?: string | null;
  subtitle?: string;
  notes?: boolean;
}) {
  let title;
  if (partName === "") {
    title = serial ?? "Material";
  } else if (serial === undefined || serial === null || serial === "") {
    if (notes) {
      title = "Add note for " + partName;
    } else {
      title = partName;
    }
  } else {
    if (notes) {
      title = "Add note for " + serial;
    } else {
      title = partName + " - " + serial;
    }
  }

  return (
    <Box display="flex" textAlign="left">
      {partName === "" ? <SearchIcon /> : <PartIdenticon part={partName} />}
      <Box marginLeft="8px" flexGrow={1}>
        <Typography variant="h6">{title}</Typography>
        {subtitle ? <Typography variant="caption">{subtitle}</Typography> : undefined}
      </Box>
    </Box>
  );
});

function MaterialDialogTitle({ notes }: { notes?: boolean }) {
  const mat = useRecoilValueLoadable(matDetails.materialInDialogInfo).valueMaybe();
  const serial = useRecoilValueLoadable(matDetails.serialInMaterialDialog).valueMaybe();
  return <MaterialDetailTitle notes={notes} partName={mat?.partName ?? ""} serial={mat?.serial ?? serial} />;
}

function MaterialInspections() {
  const insps = useRecoilValue(matDetails.materialInDialogInspections);
  function colorForInspType(type: string): string {
    if (insps.completedInspections.includes(type)) {
      return "black";
    } else {
      return "red";
    }
  }
  if (insps.signaledInspections.length === 0) {
    return <small>Inspections: none</small>;
  } else {
    return (
      <small>
        Inspections:{" "}
        {insps.signaledInspections.map((type, i) => (
          <span key={i}>
            {i === 0 ? "" : ", "}
            <span style={{ color: colorForInspType(type) }}>{type}</span>
          </span>
        ))}
      </small>
    );
  }
}

function MaterialEvents({ highlightProcess }: { highlightProcess?: number }) {
  const events = useRecoilValue(matDetails.materialInDialogEvents);
  return <LogEntries entries={events} copyToClipboard highlightProcess={highlightProcess} />;
}

export const MaterialDetailContent = React.memo(function MaterialDetailContent({
  highlightProcess,
}: {
  highlightProcess?: number;
}) {
  const toShow = useRecoilValue(matDetails.materialDialogOpen);
  const mat = useRecoilValue(matDetails.materialInDialogInfo);

  if (toShow === null) return null;

  if (mat === null) {
    if (toShow.type === "AddMatWithEnteredSerial" || toShow.type === "ManuallyEnteredSerial") {
      return <div style={{ marginLeft: "1em" }}>Material with serial {toShow.serial} not found.</div>;
    } else if (toShow.type === "Barcode") {
      return <div style={{ marginLeft: "1em" }}>Material with barcode {toShow.barcode} not found.</div>;
    } else {
      return <div style={{ marginLeft: "1em" }}>Material not found.</div>;
    }
  }

  return (
    <>
      <div style={{ marginLeft: "1em" }}>
        <div>
          <small>Workorder: {mat?.workorderId ?? "none"}</small>
        </div>
        <div>
          <DisplayLoadingAndError
            fallback={
              <small>
                Inspections: <CircularProgress size="10" />
              </small>
            }
          >
            <MaterialInspections />
          </DisplayLoadingAndError>
        </div>
      </div>
      <DisplayLoadingAndError fallback={<CircularProgress />}>
        <MaterialEvents highlightProcess={highlightProcess} />
      </DisplayLoadingAndError>
    </>
  );
});

interface NotesDialogBodyProps {
  setNotesOpen: (o: boolean) => void;
}

function NotesDialogBody(props: NotesDialogBodyProps) {
  const [curNote, setCurNote] = React.useState<string>("");
  const operator = useRecoilValue(currentOperator);
  const [addNote] = matDetails.useAddNote();
  const mat = useRecoilValue(matDetails.materialInDialogInfo);
  if (mat === null) return null;

  return (
    <>
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
            addNote({ matId: mat.materialID, process: 0, operator: operator, notes: curNote });
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

export function MaterialLoading() {
  const toShow = useRecoilValue(matDetails.materialDialogOpen);
  if (toShow === null) return null;

  let msg: string;
  switch (toShow.type) {
    case "Barcode":
      msg = "Loading material with barcode " + toShow.barcode + "...";
      break;
    case "AddMatWithEnteredSerial":
    case "ManuallyEnteredSerial":
      msg = "Loading material with serial " + toShow.serial + "...";
      break;
    default:
      msg = "Loading material...";
      break;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", alignItems: "center" }}>
      <CircularProgress />
      <div style={{ marginTop: "1em" }}>{msg}</div>
    </div>
  );
}

function AddNoteButton({ setNotesOpen }: { setNotesOpen: (o: boolean) => void }) {
  const mat = useRecoilValue(matDetails.materialInDialogInfo);
  if (mat === null || mat.materialID < 0) return null;

  return (
    <Button onClick={() => setNotesOpen(true)} color="primary">
      Add Note
    </Button>
  );
}

export interface MaterialDialogProps {
  buttons?: JSX.Element;
  onClose?: () => void;
  allowNote?: boolean;
  extraDialogElements?: JSX.Element;
  highlightProcess?: number;
}

export const MaterialDialog = React.memo(function MaterialDialog(props: MaterialDialogProps) {
  const [notesOpen, setNotesOpen] = React.useState<boolean>(false);
  const closeMatDialog = matDetails.useCloseMaterialDialog();
  const dialogOpen = useRecoilValue(matDetails.materialDialogOpen);

  function close() {
    closeMatDialog();
    if (props.onClose) props.onClose();
  }

  let body: JSX.Element | undefined;
  let notesBody: JSX.Element | undefined;
  if (dialogOpen) {
    body = (
      <>
        <DialogTitle>
          <MaterialDialogTitle />
        </DialogTitle>
        <DialogContent>
          <DisplayLoadingAndError fallback={<MaterialLoading />}>
            <MaterialDetailContent highlightProcess={props.highlightProcess} />
            <DisplayLoadingAndError fallback={<CircularProgress />}>
              {props.extraDialogElements}
            </DisplayLoadingAndError>
          </DisplayLoadingAndError>
        </DialogContent>
        <DialogActions>
          {dialogOpen && (props.buttons || props.allowNote) ? (
            <DisplayLoadingAndError fallback={<CircularProgress />}>
              {dialogOpen && props.allowNote ? <AddNoteButton setNotesOpen={setNotesOpen} /> : undefined}
              {props.buttons}
            </DisplayLoadingAndError>
          ) : null}
          <Button onClick={close} color="secondary">
            Close
          </Button>
        </DialogActions>
      </>
    );
    if (props.allowNote) {
      notesBody = (
        <>
          <DialogTitle>
            <MaterialDialogTitle notes />
          </DialogTitle>
          <DisplayLoadingAndError
            fallback={
              <DialogContent>
                <CircularProgress />
              </DialogContent>
            }
          >
            <NotesDialogBody setNotesOpen={setNotesOpen} />;
          </DisplayLoadingAndError>
        </>
      );
    }
  }
  return (
    <>
      <Dialog open={dialogOpen !== null} onClose={close} maxWidth="md">
        {body}
      </Dialog>
      {props.allowNote ? (
        <Dialog open={notesOpen} onClose={() => setNotesOpen(false)} maxWidth="md">
          {notesBody}
        </Dialog>
      ) : undefined}
    </>
  );
});
