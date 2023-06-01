/* Copyright (c) 2020, John Lenz

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

import { Button } from "@mui/material";
import { MenuItem } from "@mui/material";
import { TextField } from "@mui/material";
import * as React from "react";
import { ActionType, IActiveJob, IInProcessMaterial, LocType } from "../../network/api.js";
import { JobsBackend } from "../../network/backend.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentStatus } from "../../cell-status/current-status.js";
import { inProcessMaterialInDialog, materialDialogOpen } from "../../cell-status/material-details.js";
import { useRecoilValue } from "recoil";
import { currentOperator } from "../../data/operators.js";
import { fmsInformation } from "../../network/server-settings.js";
import { useAtomValue, useSetAtom } from "jotai";

interface InvalidateCycle {
  readonly process: number | null;
  readonly updating: boolean;
}

export type InvalidateCycleState = InvalidateCycle | null;

export interface InvalidateCycleProps {
  readonly st: InvalidateCycleState;
  readonly setState: (s: InvalidateCycleState) => void;
}

export function InvalidateCycleDialogContent(props: InvalidateCycleProps) {
  const curMat = useAtomValue(inProcessMaterialInDialog);

  if (curMat === null || curMat.location.type !== LocType.InQueue) return <div />;

  return (
    <div style={{ margin: "2em" }}>
      <p>
        An invalidated cycle remains in the event log, but is not considered when determining the next process
        to be machined on a piece of material.
      </p>
      <TextField
        value={props.st?.process ?? ""}
        select
        onChange={(e) =>
          props.st &&
          props.setState({
            ...props.st,
            process: parseInt(e.target.value),
          })
        }
        style={{ width: "20em" }}
        variant="outlined"
        label="Select process to invalidate"
      >
        {LazySeq.ofRange(1, curMat.process + 1).map((p) => (
          <MenuItem key={p} value={p}>
            {p}
          </MenuItem>
        ))}
      </TextField>
    </div>
  );
}

export function InvalidateCycleDialogButtons(
  props: InvalidateCycleProps & { readonly onClose: () => void; readonly ignoreOperator?: boolean }
) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const curMat = useAtomValue(inProcessMaterialInDialog);
  const setMatToShow = useSetAtom(materialDialogOpen);
  let operator = useRecoilValue(currentOperator);

  if (!fmsInfo.allowSwapAndInvalidateMaterialAtLoadStation) return null;

  if (props.ignoreOperator) operator = null;

  if (curMat === null || curMat.location.type !== LocType.InQueue) return null;

  function invalidateCycle() {
    if (curMat && props.st && props.st.process) {
      props.setState({ ...props.st, updating: true });
      JobsBackend.invalidatePalletCycle(curMat.materialID, null, operator, props.st.process).finally(() => {
        setMatToShow(null);
        props.onClose();
      });
    }
  }

  return (
    <>
      {props.st === null ? (
        <Button color="primary" onClick={() => props.setState({ process: null, updating: false })}>
          Invalidate Cycle
        </Button>
      ) : undefined}
      {props.st !== null ? (
        <Button
          color="primary"
          onClick={invalidateCycle}
          disabled={props.st.process === null || props.st.updating}
        >
          {props.st.process === null
            ? "Invalidate Cycle"
            : "Invalidate Process " + props.st.process.toString()}
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
  job: Readonly<IActiveJob> | undefined
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
            .anyMatch((p) => p.casting === newMat.partName)
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

export function SwapMaterialDialogContent(props: SwapMaterialProps): JSX.Element {
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
  props: SwapMaterialProps & { readonly onClose: () => void; readonly ignoreOperator?: boolean }
) {
  const fmsInfo = useRecoilValue(fmsInformation);
  const curMat = useAtomValue(inProcessMaterialInDialog);
  const closeMatDialog = useSetAtom(materialDialogOpen);
  let operator = useRecoilValue(currentOperator);

  if (!fmsInfo.allowSwapAndInvalidateMaterialAtLoadStation) return null;

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
        pallet: curMat.location.pallet ?? "",
        materialIDToSetOnPallet: props.st.selectedMatToSwap.materialID,
      }).finally(() => {
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
