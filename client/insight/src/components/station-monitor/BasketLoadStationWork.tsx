/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
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

import { useEffect, useMemo, useState } from "react";
import { Alert, Box, Button, Card, CardContent, Stack, Typography } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";

import * as api from "../../network/api.js";
import { InProcMaterial, MatCardFontSize } from "./Material.js";
import { MoveMaterialArrowNode } from "./MoveMaterialArrows.js";
import { MoveMaterialNodeKindType } from "../../data/move-arrows.js";

export interface BasketLoadStationCommand {
  readonly workId: string;
}

export type SubmitBasketLoadStationCommand = (
  stationNumber: number,
  command: BasketLoadStationCommand,
) => Promise<"accepted" | "conflict">;

interface BasketLoadStationWorkflowProps {
  readonly stationNumber: number;
  readonly basket: Readonly<api.IBasketStatus>;
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  readonly fsize: MatCardFontSize;
  readonly submitCommand: SubmitBasketLoadStationCommand | undefined;
}

type SubmissionState = "idle" | "submitting" | "accepted" | "conflict" | "error";
type WorkPhase = "unload" | "load";
type ActionPhase = WorkPhase | "invalid";

type ConfirmableWork = {
  readonly workId: string;
  readonly phase: WorkPhase;
};

type WorkState =
  | { readonly type: "none" }
  | { readonly type: "confirmable"; readonly work: ConfirmableWork }
  | { readonly type: "inconsistent" };

function isNonNegativeInteger(value: number | undefined): value is number {
  return Number.isInteger(value) && (value ?? -1) >= 0;
}

function isNonBlank(value: string | undefined): value is string {
  return typeof value === "string" && value.trim() !== "";
}

function actionPhase(
  mat: Readonly<api.IInProcessMaterial>,
  basketId: number,
): ActionPhase | undefined {
  if (
    mat.location.type === api.LocType.InBasket &&
    mat.location.basketId === basketId &&
    (mat.action.type === api.ActionType.UnloadToInProcess ||
      mat.action.type === api.ActionType.UnloadToCompletedMaterial)
  ) {
    if (!isNonNegativeInteger(mat.location.basketSlot)) {
      return "invalid";
    }
    return "unload";
  }
  if (mat.action.type === api.ActionType.LoadingToBasket) {
    if (!isNonNegativeInteger(mat.action.loadToBasketId)) return "invalid";
    if (mat.action.loadToBasketId !== basketId) return undefined;
    if (
      !isNonNegativeInteger(mat.action.loadToBasketSlot) ||
      (mat.location.type !== api.LocType.Free &&
        (mat.location.type !== api.LocType.InQueue || !isNonBlank(mat.location.currentQueue)))
    ) {
      return "invalid";
    }
    return "load";
  }
  return undefined;
}

function confirmableWork(
  material: ReadonlyArray<Readonly<api.IInProcessMaterial>>,
  basketId: number,
): WorkState {
  const classifiedActions = LazySeq.of(material)
    .collect((mat) => {
      const phase = actionPhase(mat, basketId);
      return phase === undefined ? undefined : { phase, workId: mat.action.workId };
    })
    .toRArray();
  if (classifiedActions.some(({ phase }) => phase === "invalid")) {
    return { type: "inconsistent" };
  }
  const phaseActions = classifiedActions.filter(
    (
      action,
    ): action is {
      readonly phase: WorkPhase;
      readonly workId: string | undefined;
    } => action.phase !== "invalid",
  );
  if (phaseActions.length === 0) return { type: "none" };
  const tagged = phaseActions.filter(
    (action): action is { readonly phase: WorkPhase; readonly workId: string } =>
      isNonBlank(action.workId),
  );
  const workIds = LazySeq.of(tagged).toHashSet(({ workId }) => workId);
  const phases = LazySeq.of(tagged).toHashSet(({ phase }) => phase);
  if (tagged.length !== phaseActions.length || workIds.size !== 1 || phases.size !== 1) {
    return { type: "inconsistent" };
  }
  return {
    type: "confirmable",
    work: { workId: tagged[0].workId, phase: tagged[0].phase },
  };
}

function loadingSource(mat: Readonly<api.IInProcessMaterial>): string {
  if (mat.location.type === api.LocType.InQueue && mat.location.currentQueue) {
    return mat.location.currentQueue;
  }
  return "raw material";
}

function SlotMaterial({
  material,
  fsize,
}: {
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  readonly fsize: MatCardFontSize;
}) {
  return material.map((mat) => (
    <MoveMaterialArrowNode
      key={mat.materialID}
      kind={{ type: MoveMaterialNodeKindType.Material, material: mat }}
    >
      <InProcMaterial mat={mat} fsize={fsize} />
    </MoveMaterialArrowNode>
  ));
}

export function BasketLoadStationWorkflow({
  stationNumber,
  basket,
  material,
  fsize,
  submitCommand,
}: BasketLoadStationWorkflowProps) {
  const [submission, setSubmission] = useState<SubmissionState>("idle");
  const workState = useMemo(
    () => confirmableWork(material, basket.basketId),
    [basket.basketId, material],
  );
  const work = workState.type === "confirmable" ? workState.work : undefined;

  useEffect(() => setSubmission("idle"), [work?.workId]);

  const materialBySlot = useMemo(
    () =>
      LazySeq.of(material)
        .filter(
          (mat) =>
            mat.location.type === api.LocType.InBasket &&
            mat.location.basketId === basket.basketId &&
            mat.location.basketSlot !== undefined,
        )
        .groupBy((mat) => mat.location.basketSlot ?? 0)
        .toHashMap((entry) => entry),
    [basket.basketId, material],
  );
  const loadsBySlot = useMemo(
    () =>
      LazySeq.of(material)
        .filter(
          (mat) =>
            mat.action.type === api.ActionType.LoadingToBasket &&
            mat.action.loadToBasketId === basket.basketId &&
            mat.action.loadToBasketSlot !== undefined,
        )
        .groupBy((mat) => mat.action.loadToBasketSlot ?? 0)
        .toHashMap((entry) => entry),
    [basket.basketId, material],
  );
  const slots = useMemo(
    () =>
      LazySeq.of(basket.emptySlots ?? [])
        .concat(basket.unknownSlots ?? [])
        .concat(materialBySlot.keysToLazySeq())
        .concat(loadsBySlot.keysToLazySeq())
        .distinctBy((slot) => slot)
        .toSortedArray((slot) => slot),
    [basket.emptySlots, basket.unknownSlots, materialBySlot, loadsBySlot],
  );

  async function submit(): Promise<void> {
    if (work === undefined || submitCommand === undefined) return;
    setSubmission("submitting");
    try {
      setSubmission(await submitCommand(stationNumber, { workId: work.workId }));
    } catch {
      setSubmission("error");
    }
  }

  const submissionDisabled =
    submission === "submitting" || submission === "accepted" || submission === "conflict";
  const buttonLabel = work?.phase === "unload" ? "Unload Complete" : "Load Complete";

  return (
    <Stack spacing={2} sx={{ mt: 2 }}>
      <Typography variant="h6">Basket slots</Typography>
      <Box
        sx={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(15em, 1fr))",
          gap: 2,
        }}
      >
        {slots.map((slot) => {
          const slotMaterial = materialBySlot.get(slot) ?? [];
          const loads = loadsBySlot.get(slot) ?? [];
          const isUnknown = basket.unknownSlots?.includes(slot) === true;
          return (
            <Card key={slot} data-testid={`basket-load-station-slot-${slot}`} variant="outlined">
              <CardContent>
                <Typography variant="h6">Slot {slot + 1}</Typography>
                {slotMaterial.length > 0 ? (
                  <SlotMaterial material={slotMaterial} fsize={fsize} />
                ) : (
                  <Typography color={isUnknown ? "warning.main" : "text.secondary"}>
                    {isUnknown ? "Unknown" : "Empty"}
                  </Typography>
                )}
                {loads.map((mat, index) => (
                  <Box key={`${mat.jobUnique}-${mat.process}-${index}`} sx={{ mt: 1 }}>
                    <Typography>Load from {loadingSource(mat)}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {mat.partName} · Process {mat.action.processAfterLoad ?? mat.process}
                    </Typography>
                  </Box>
                ))}
              </CardContent>
            </Card>
          );
        })}
      </Box>
      {work && submitCommand ? (
        <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
          <Button variant="contained" disabled={submissionDisabled} onClick={() => void submit()}>
            {buttonLabel}
          </Button>
        </Box>
      ) : work ? (
        <Alert severity="warning">Basket work confirmation is not configured.</Alert>
      ) : workState.type === "inconsistent" ? (
        <Alert severity="warning">
          Basket work is inconsistent. Wait for refreshed material actions before confirming.
        </Alert>
      ) : null}
      {submission === "accepted" ? (
        <Alert severity="success">Confirmation accepted. Waiting for refreshed work.</Alert>
      ) : submission === "conflict" ? (
        <Alert severity="warning">
          Basket work changed before confirmation. Review the refreshed slots and try again.
        </Alert>
      ) : submission === "error" ? (
        <Alert severity="error">Unable to confirm basket work. No work was assumed complete.</Alert>
      ) : null}
    </Stack>
  );
}
