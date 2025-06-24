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

import { Button, Grid, List, ListItem, ListItemButton, ListItemIcon, ListItemText } from "@mui/material";
import { MenuItem } from "@mui/material";
import { TextField } from "@mui/material";
import { ActionType, IActiveJob, IInProcessMaterial, IJob, LocType } from "../../network/api.js";
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
import { ReactNode } from "react";
import { last30Jobs } from "../../cell-status/scheduled-jobs.js";
import { PartIdenticon } from "./Material.js";

export type InvalidateCycleState = {
  readonly process: number | null;
  readonly changeRawMat: string | null;
  readonly changeJobUnique: string | null;
  readonly updating: boolean;
};

export interface InvalidateCycleProps {
  readonly st: InvalidateCycleState | null;
  readonly setState: (s: InvalidateCycleState | null) => void;
}

function SelectProcess(props: InvalidateCycleProps) {
  const lastMat = useAtomValue(materialInDialogLargestUsedProcess);
  if (lastMat === null) return null;

  return (
    <TextField
      value={props.st?.process ?? ""}
      select
      onChange={(e) =>
        props.st &&
        props.setState({
          ...props.st,
          process: e.target.value === "" ? null : parseInt(e.target.value),
        })
      }
      style={{ width: "20em" }}
      variant="outlined"
      label="Select process to invalidate"
    >
      <MenuItem value="">
        <em>None</em>
      </MenuItem>
      {LazySeq.ofRange(1, lastMat.process + 1).map((p) => (
        <MenuItem key={p} value={p}>
          {p}
        </MenuItem>
      ))}
    </TextField>
  );
}

function SelectRawMat(props: InvalidateCycleProps & { rawMat: string }) {
  return (
    <ListItem>
      <ListItemButton
        selected={props.st?.changeRawMat === props.rawMat}
        onClick={() =>
          props.setState({
            changeRawMat: props.rawMat,
            changeJobUnique: null,
            process: null,
            updating: false,
          })
        }
      >
        Change to {props.rawMat}
      </ListItemButton>
    </ListItem>
  );
}

function SelectJob({
  st,
  setState,
  jobUnique,
  job,
}: InvalidateCycleProps & { jobUnique: string; job?: Readonly<IJob> }) {
  return (
    <ListItem>
      <ListItemButton
        selected={st?.changeJobUnique === jobUnique}
        onClick={() =>
          setState({
            changeRawMat: null,
            changeJobUnique: jobUnique,
            process: null,
            updating: false,
          })
        }
      >
        {job ? (
          <ListItemIcon>
            <PartIdenticon part={job.partName} />
          </ListItemIcon>
        ) : undefined}
        <ListItemText primary={"Change to job " + jobUnique} secondary={job?.partName} />
      </ListItemButton>
    </ListItem>
  );
}

function ChangeMaterialType(props: InvalidateCycleProps) {
  const possible = useAtomValue(barcodePotentialNewMaterial);
  const currentSt = useAtomValue(currentStatus);
  const history = useAtomValue(last30Jobs);

  return (
    <List>
      {Object.values(possible?.possibleCastingsByQueue ?? {})
        .flatMap((cs) => cs)
        .map((c) => (
          <SelectRawMat key={c} rawMat={c} st={props.st} setState={props.setState} />
        ))}
      {Object.values(possible?.possibleJobsByQueue ?? {})
        .flatMap((js) => js)
        .filter((j) => j.lastCompletedProcess === 0)
        .map((j) => (
          <SelectJob
            key={j.jobUnique}
            st={props.st}
            setState={props.setState}
            jobUnique={j.jobUnique}
            job={currentSt.jobs[j.jobUnique] ?? history.get(j.jobUnique) ?? undefined}
          />
        ))}
    </List>
  );
}

export function InvalidateCycleDialogContent(props: InvalidateCycleProps) {
  const curMat = useAtomValue(materialInDialogInfo);

  if (props.st === null || curMat === null) return <div />;

  return (
    <Grid container spacing={2} sx={{ margin: "2em" }}>
      <Grid size={{ xs: 12, md: 6 }}>
        <p>
          An invalidated cycle remains in the event log, but is not considered when determining the next
          process to be machined on a piece of material.
        </p>
        <SelectProcess st={props.st} setState={props.setState} />
      </Grid>
      <Grid size={{ xs: 12, md: 6 }}>
        <ChangeMaterialType st={props.st} setState={props.setState} />
      </Grid>
    </Grid>
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
  const lastMat = useAtomValue(materialInDialogLargestUsedProcess);
  const possibleNew = useAtomValue(barcodePotentialNewMaterial);
  const setMatToShow = useSetAtom(materialDialogOpen);
  let operator = useAtomValue(currentOperator);

  if (props.loadStation && !fmsInfo.allowInvalidateMaterialAtLoadStation) return null;
  if (!props.loadStation && !fmsInfo.allowInvalidateMaterialOnQueuesPage) return null;
  if (curMat === null) return null;

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
        .catch(console.log)
        .finally(() => {
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
          disabled={props.st.process === null || props.st.updating}
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
