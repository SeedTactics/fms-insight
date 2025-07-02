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

import { Box, Button, ListItemIcon, ListItemText, ListSubheader, Stack } from "@mui/material";
import { MenuItem } from "@mui/material";
import { TextField } from "@mui/material";
import { ActionType, IActiveJob, IInProcessMaterial, LocType } from "../../network/api.js";
import { JobsBackend } from "../../network/backend.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentStatus } from "../../cell-status/current-status.js";
import {
  barcodePotentialNewMaterial,
  inProcessMaterialInDialog,
  materialDialogOpen,
  materialInDialogInfo,
  materialInDialogLargestUsedProcess,
} from "../../cell-status/material-details.js";
import { currentOperator } from "../../data/operators.js";
import { fmsInformation } from "../../network/server-settings.js";
import { useAtomValue, useSetAtom } from "jotai";
import { ReactNode, useEffect, useRef } from "react";
import { last30Jobs } from "../../cell-status/scheduled-jobs.js";
import { PartIdenticon } from "./Material.js";

export type InvalidateCycleState = {
  readonly process: number | null;
  readonly changeRawMat: string | null;
  readonly changeJobUnique: string | null;
  readonly updating: boolean;
};

export type InvalidateCycleProps = {
  readonly st: InvalidateCycleState | null;
  readonly setState: (s: InvalidateCycleState | null) => void;
};

function InvalidateSelect(props: InvalidateCycleProps) {
  const lastMat = useAtomValue(materialInDialogLargestUsedProcess);
  const possible = useAtomValue(barcodePotentialNewMaterial);
  const currentSt = useAtomValue(currentStatus);
  const history = useAtomValue(last30Jobs);

  const castings = Object.values(possible?.possibleCastingsByQueue ?? {}).flatMap((cs) => cs);
  const jobs = Object.values(possible?.possibleJobsByQueue ?? {})
    .flatMap((js) => js)
    .filter((j) => j.lastCompletedProcess === 0)
    .map((j) => ({
      jobUnique: j.jobUnique,
      job: currentSt.jobs[j.jobUnique] ?? history.get(j.jobUnique) ?? null,
    }));

  const hasProc = lastMat !== null && lastMat.process >= 1;
  const hasChangeMat = castings.length > 0 || jobs.length > 0;

  function change(e: string) {
    props.setState({
      updating: false,
      process: e.startsWith("proc") ? parseInt(e.substring(4)) : null,
      changeRawMat: e.startsWith("rawMat") ? e.substring(6) : null,
      changeJobUnique: e.startsWith("job") ? e.substring(3) : null,
    });
  }

  return (
    <TextField
      value={
        props.st?.process
          ? "proc" + props.st.process.toString()
          : props.st?.changeRawMat
            ? "rawMat" + props.st.changeRawMat
            : props.st?.changeJobUnique
              ? "job" + props.st.changeJobUnique
              : ""
      }
      select
      onChange={(e) => change(e.target.value)}
      variant="outlined"
      label={
        props.st?.process || !hasChangeMat
          ? "Invalidate Process"
          : props.st?.changeRawMat
            ? "Change Raw Material"
            : props.st?.changeJobUnique
              ? "Change Assigned Job"
              : !hasProc
                ? "Change Assignment"
                : ""
      }
    >
      {[
        hasProc && hasChangeMat
          ? [<ListSubheader key={"procheader"}>Invalidate Selected Process</ListSubheader>]
          : [],
        lastMat
          ? [
              ...LazySeq.ofRange(1, lastMat.process + 1).map((p) => (
                <MenuItem key={p} value={"proc" + p.toString()}>
                  Invalidate Process {p}
                </MenuItem>
              )),
            ]
          : [],
        hasProc && hasChangeMat ? [<ListSubheader key="matheader">Change Material Type</ListSubheader>] : [],
        hasChangeMat
          ? [
              ...castings.map((c) => (
                <MenuItem key={"rawMat" + c} value={"rawMat" + c}>
                  Invalidate Everything And Change to {c}
                </MenuItem>
              )),
              ...jobs.map(({ jobUnique, job }) => (
                <MenuItem key={"job" + jobUnique} value={"job" + jobUnique}>
                  {job ? (
                    <ListItemIcon>
                      <PartIdenticon part={job.partName} />
                    </ListItemIcon>
                  ) : undefined}
                  <ListItemText
                    primary={"Invalidate Everything And Change To Job " + jobUnique}
                    secondary={job?.partName}
                  />
                </MenuItem>
              )),
            ]
          : [],
      ]}
    </TextField>
  );
}

export function InvalidateCycleDialogContent(props: InvalidateCycleProps) {
  const curMat = useAtomValue(materialInDialogInfo);
  const show = props.st !== null && curMat !== null;
  const boxRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    boxRef.current?.scrollIntoView({
      behavior: "smooth",
      block: "end",
    });
  }, [boxRef, show]);

  if (!show) return <div />;

  return (
    <Box
      ref={boxRef}
      sx={{ display: "flex", flexDirection: "column", alignItems: "center", marginTop: "1em" }}
    >
      <Stack spacing={2}>
        <p style={{ maxWidth: "35em" }}>
          An invalidated cycle remains in the event log, but is not considered when determining the next
          process to be machined on a piece of material.
        </p>
        <InvalidateSelect st={props.st} setState={props.setState} />
      </Stack>
    </Box>
  );
}

export function InvalidateCycleDialogButton(
  props: InvalidateCycleProps & {
    readonly onClose: () => void;
    readonly ignoreOperator?: boolean;
    readonly loadStation?: boolean;
  },
) {
  const fmsInfo = useAtomValue(fmsInformation);
  const curMat = useAtomValue(materialInDialogInfo);
  const inProcMat = useAtomValue(inProcessMaterialInDialog);
  const lastMat = useAtomValue(materialInDialogLargestUsedProcess);
  const possibleNew = useAtomValue(barcodePotentialNewMaterial);
  const setMatToShow = useSetAtom(materialDialogOpen);
  let operator = useAtomValue(currentOperator);

  if (props.loadStation && !fmsInfo.allowInvalidateMaterialAtLoadStation) return null;
  if (!props.loadStation && !fmsInfo.allowInvalidateMaterialOnQueuesPage) return null;
  if (curMat === null) return null;

  if (inProcMat && inProcMat.location.type === LocType.OnPallet) return null;

  const allowChange =
    possibleNew !== null &&
    (!LazySeq.ofObject(possibleNew.possibleCastingsByQueue ?? {}).isEmpty() ||
      !LazySeq.ofObject(possibleNew.possibleJobsByQueue ?? {}).isEmpty());
  if (!allowChange && (lastMat === null || lastMat.process < 1)) return null;

  if (props.ignoreOperator) operator = null;

  function showInvalidate() {
    props.setState({ process: null, updating: false, changeRawMat: null, changeJobUnique: null });
  }

  function invalidateCycle() {
    if (curMat && (props.st?.process || props.st?.changeRawMat || props.st?.changeJobUnique)) {
      props.setState({ ...props.st, updating: true });
      JobsBackend.invalidatePalletCycle(
        curMat.materialID,
        operator,
        props.st?.changeRawMat,
        props.st?.changeJobUnique,
        props.st?.process ?? 0,
      )
        .then((mat) => {
          if (mat) {
            setMatToShow({
              type: "MatDetails",
              details: mat,
            });
            props.onClose();
          } else {
            setMatToShow(null);
            props.onClose();
          }
        })
        .catch((e) => {
          console.log(e);
          setMatToShow(null);
          props.onClose();
        });
    }
  }

  return (
    <>
      {props.st === null ? (
        <Button color="primary" onClick={showInvalidate}>
          Invalidate Cycle
        </Button>
      ) : undefined}
      {props.st !== null ? (
        <Button
          color="primary"
          onClick={invalidateCycle}
          disabled={
            props.st.updating ||
            (props.st.process === null && props.st.changeRawMat === null && props.st.changeJobUnique === null)
          }
        >
          {props.st.process !== null
            ? "Invalidate Process " + props.st.process.toString()
            : props.st.changeJobUnique !== null
              ? "Invalidate And Change To Job " + props.st.changeJobUnique
              : props.st.changeRawMat !== null
                ? "Invalidate And Change To " + props.st.changeRawMat
                : "Invalidate Cycle"}
        </Button>
      ) : undefined}
    </>
  );
}

// ----------------------------------------------------------------------------------
// Swap
// ----------------------------------------------------------------------------------

interface SwapMaterial {
  readonly selectedMatToSwap: Readonly<IInProcessMaterial> | null;
  readonly updating: boolean;
}

export type SwapMaterialState = SwapMaterial | null;

function isNullOrEmpty(s: string | null | undefined): boolean {
  return s === undefined || s === null || s == "";
}

function matCanSwap(
  curMat: Readonly<IInProcessMaterial>,
  job: Readonly<IActiveJob> | undefined,
): (m: Readonly<IInProcessMaterial>) => boolean {
  return (newMat) => {
    if (isNullOrEmpty(newMat.serial)) return false;
    if (newMat.location.type === LocType.OnPallet) return false;
    if (newMat.process !== curMat.process - 1) return false;
    if (isNullOrEmpty(newMat.jobUnique)) {
      // if part name is wrong, check casting
      if (isNullOrEmpty(newMat.partName)) return false;
      if (newMat.partName !== curMat.partName) {
        if (!job) return false;
        if (
          !LazySeq.of(job.procsAndPaths)
            .flatMap((p) => p.paths)
            .some((p) => p.casting === newMat.partName)
        ) {
          return false;
        }
      }
    } else {
      // check path
      if (newMat.jobUnique !== curMat.jobUnique) return false;
      if (newMat.path !== curMat.path) return false;
    }
    return true;
  };
}

export interface SwapMaterialProps {
  readonly st: SwapMaterialState;
  readonly setState: (s: SwapMaterialState) => void;
}

export function SwapMaterialDialogContent(props: SwapMaterialProps): ReactNode {
  const status = useAtomValue(currentStatus);
  const curMat = useAtomValue(inProcessMaterialInDialog);

  if (curMat === null || props.st === null) return <div />;
  const curMatJob = status.jobs[curMat.jobUnique];

  const availMats = status.material.filter(matCanSwap(curMat, curMatJob));
  if (availMats.length === 0) {
    return (
      <p style={{ margin: "2em" }}>
        No material with the same job is available for swapping. You must edit the pallet using the cell
        controller software to remove the material from the pallet. Insight will automatically refresh once
        the cell controller software is updated.
      </p>
    );
  } else {
    return (
      <div style={{ margin: "2em" }}>
        <p>Swap serial on pallet with material from the same job.</p>
        <p>
          If material on the pallet is from a different job, you cannot use this screen. Instead, the material
          must first be removed from the pallet using the cell controller software. Insight will automatically
          refresh when this occurs.
        </p>
        <TextField
          value={props.st?.selectedMatToSwap?.serial ?? ""}
          select
          onChange={(e) =>
            props.st &&
            props.setState({
              ...props.st,
              selectedMatToSwap: availMats.find((m) => m.serial === e.target.value) ?? null,
            })
          }
          style={{ width: "20em" }}
          variant="outlined"
          label={"Select serial to swap with " + (curMat.serial ?? "")}
        >
          {availMats.map((m) => (
            <MenuItem key={m.materialID} value={m.serial}>
              {m.serial}
            </MenuItem>
          ))}
        </TextField>
      </div>
    );
  }
}

export function SwapMaterialButtons(
  props: SwapMaterialProps & { readonly onClose: () => void; readonly ignoreOperator?: boolean },
) {
  const fmsInfo = useAtomValue(fmsInformation);
  const curMat = useAtomValue(inProcessMaterialInDialog);
  const closeMatDialog = useSetAtom(materialDialogOpen);
  let operator = useAtomValue(currentOperator);

  if (!fmsInfo.allowSwapSerialAtLoadStation) return null;

  if (props.ignoreOperator) operator = null;

  if (
    !curMat ||
    curMat.location.type !== LocType.OnPallet ||
    curMat.action.type === ActionType.Loading ||
    curMat.action.type === ActionType.UnloadToCompletedMaterial ||
    curMat.action.type === ActionType.UnloadToInProcess
  ) {
    return null;
  }

  function swapMats() {
    if (curMat && props.st && props.st.selectedMatToSwap && curMat.location.type === LocType.OnPallet) {
      props.setState({ selectedMatToSwap: props.st.selectedMatToSwap, updating: true });
      JobsBackend.swapMaterialOnPallet(curMat.materialID, operator, {
        pallet: curMat.location.palletNum ?? 0,
        materialIDToSetOnPallet: props.st.selectedMatToSwap.materialID,
      })
        .catch(console.log)
        .finally(() => {
          closeMatDialog(null);
          props.onClose();
        });
    }
  }

  return (
    <>
      {props.st === null ? (
        <Button color="primary" onClick={() => props.setState({ selectedMatToSwap: null, updating: false })}>
          Swap Serial
        </Button>
      ) : undefined}
      {props.st !== null ? (
        <Button
          color="primary"
          onClick={swapMats}
          disabled={props.st.selectedMatToSwap === null || props.st.updating}
        >
          {props.st.selectedMatToSwap === null
            ? "Swap Serial"
            : "Swap with " + (props.st.selectedMatToSwap.serial ?? "")}
        </Button>
      ) : undefined}
    </>
  );
}
