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

import { type ReactNode, useMemo, useState } from "react";
import { Alert, Box, Button, Stack, Typography } from "@mui/material";
import { LazySeq } from "@seedtactics/immutable-collections";
import { useAtomValue, useSetAtom } from "jotai";

import { basketSlotLabel } from "../../cell-status/station-cycles.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { alarmsBlockingChanges } from "../../cell-status/alarms.js";
import * as api from "../../network/api.js";
import { serverErrorMessage } from "../../network/api-errors.js";
import { JobsBackend } from "../../network/backend.js";
import { refreshCurrentStatus } from "../../network/websocket.js";
import { InProcMaterial, MatCardFontSize } from "./Material.js";
import { MoveMaterialArrowNode } from "./MoveMaterialArrows.js";
import { MoveMaterialNodeKindType } from "../../data/move-arrows.js";

interface BasketLoadStationWorkflowProps {
  readonly basket: Readonly<api.IBasketStatus>;
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  readonly fsize: MatCardFontSize;
  readonly title: ReactNode;
  readonly elapsed: ReactNode;
}

type SubmissionState = "idle" | "submitting" | "accepted" | "conflict" | "error";
interface Submission {
  readonly workId: string;
  readonly basket: Readonly<api.IBasketStatus>;
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  readonly state: Exclude<SubmissionState, "idle">;
  // The server's reason for a rejected or failed confirmation, when it supplied one.
  readonly reason: string | null;
}

type BasketAction =
  | { readonly type: "valid"; readonly workId: string | undefined }
  | { readonly type: "invalid" };

type StationWork = {
  readonly workId: string;
  readonly ready: boolean;
  readonly confirmEmpty: boolean;
  readonly awaitingSlots: ReadonlyArray<number>;
};

type WorkState =
  | { readonly type: "none" }
  | { readonly type: "active"; readonly work: StationWork }
  | { readonly type: "inconsistent" };

function isPositiveInteger(value: number | undefined): value is number {
  return Number.isInteger(value) && (value ?? 0) > 0;
}

function isNonBlank(value: string | undefined): value is string {
  return typeof value === "string" && value.trim() !== "";
}

function basketAction(
  mat: Readonly<api.IInProcessMaterial>,
  basketId: number,
): BasketAction | undefined {
  if (
    mat.location.type === api.LocType.InBasket &&
    mat.location.basketId === basketId &&
    (mat.action.type === api.ActionType.UnloadToInProcess ||
      mat.action.type === api.ActionType.UnloadToCompletedMaterial)
  ) {
    if (!isPositiveInteger(mat.location.basketSlot)) {
      return { type: "invalid" };
    }
    return { type: "valid", workId: mat.action.workId };
  }
  if (mat.action.type === api.ActionType.LoadingToBasket) {
    if (!isPositiveInteger(mat.action.loadToBasketId)) return { type: "invalid" };
    if (mat.action.loadToBasketId !== basketId) return undefined;
    const reloadsSameSlot =
      mat.location.type === api.LocType.InBasket &&
      mat.location.basketId === basketId &&
      mat.location.basketSlot === mat.action.loadToBasketSlot;
    if (
      !isPositiveInteger(mat.action.loadToBasketSlot) ||
      (!reloadsSameSlot &&
        mat.location.type !== api.LocType.Free &&
        (mat.location.type !== api.LocType.InQueue || !isNonBlank(mat.location.currentQueue)))
    ) {
      return { type: "invalid" };
    }
    return { type: "valid", workId: mat.action.workId };
  }
  return undefined;
}

function stationWork(
  material: ReadonlyArray<Readonly<api.IInProcessMaterial>>,
  basket: Readonly<api.IBasketStatus>,
): WorkState {
  const classifiedActions = LazySeq.of(material)
    .collect((mat) => basketAction(mat, basket.basketId))
    .toRArray();
  if (classifiedActions.some((action) => action.type === "invalid")) {
    return { type: "inconsistent" };
  }
  const validActions = classifiedActions.filter(
    (action): action is Extract<BasketAction, { readonly type: "valid" }> =>
      action.type === "valid",
  );
  const descriptor = basket.loadStationWork;
  if (descriptor !== undefined) {
    if (
      !descriptor ||
      !isNonBlank(descriptor.workId) ||
      typeof descriptor.readyToConfirm !== "boolean"
    )
      return { type: "inconsistent" };
    const awaiting = descriptor.awaitingMaterialSlots ?? [];
    if (
      !Array.isArray(awaiting) ||
      !awaiting.every(isPositiveInteger) ||
      new Set(awaiting).size !== awaiting.length ||
      (descriptor.readyToConfirm && awaiting.length > 0) ||
      validActions.some((action) => action.workId !== descriptor.workId)
    )
      return { type: "inconsistent" };
    const empty = descriptor.type === api.BasketLoadStationWorkType.ConfirmEmptyBasket;
    if (empty) {
      if (
        validActions.length > 0 ||
        awaiting.length > 0 ||
        material.some(
          (mat) =>
            mat.location.type === api.LocType.InBasket && mat.location.basketId === basket.basketId,
        )
      )
        return { type: "inconsistent" };
    } else if (
      descriptor.type !== api.BasketLoadStationWorkType.Material ||
      (descriptor.readyToConfirm && validActions.length === 0)
    ) {
      return { type: "inconsistent" };
    }
    return {
      type: "active",
      work: {
        workId: descriptor.workId,
        ready: descriptor.readyToConfirm,
        confirmEmpty: empty,
        awaitingSlots: awaiting,
      },
    };
  }
  return { type: validActions.length === 0 ? "none" : "inconsistent" };
}

function SlotMaterial({
  material,
  basketId,
  fsize,
}: {
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
  readonly basketId: number;
  readonly fsize: MatCardFontSize;
}) {
  return material.map((mat) => (
    <MoveMaterialArrowNode
      key={mat.materialID}
      kind={{ type: MoveMaterialNodeKindType.Material, material: mat }}
    >
      <InProcMaterial mat={mat} displayActionForSingleBasket={basketId} fsize={fsize} />
    </MoveMaterialArrowNode>
  ));
}

export function BasketLoadStationWorkflow({
  basket,
  material,
  fsize,
  title,
  elapsed,
}: BasketLoadStationWorkflowProps) {
  const [submission, setSubmission] = useState<Submission | undefined>();
  const refresh = useSetAtom(refreshCurrentStatus);
  const [refreshError, setRefreshError] = useState<
    { readonly workId: string; readonly message: string } | undefined
  >();
  const workState = useMemo(() => stationWork(material, basket), [basket, material]);
  const changesSuspended =
    alarmsBlockingChanges(useAtomValue(currentStatus), {
      activity: api.AlarmActivity.BasketLoadStation,
      basketId: basket.basketId,
    }).length > 0;
  const blockedReason = basket.loadStationWork?.confirmationBlockedReason?.trim();
  const instructionWarning = basket.loadStationWork?.instructionWarning?.trim();
  const work = workState.type === "active" ? workState.work : undefined;
  const submissionState =
    submission !== undefined &&
    submission.workId === work?.workId &&
    // A conflict rejects the submitted status snapshot, not the preserved occurrence.
    // Compare the captured snapshot even when refresh precedes the response.
    (submission.state !== "conflict" ||
      (submission.basket === basket && submission.material === material))
      ? submission.state
      : "idle";
  const submissionReason = submissionState === "idle" ? null : (submission?.reason ?? null);

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
    [basket, material],
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
    [basket, material],
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

  async function refreshStatus(workId: string): Promise<void> {
    setRefreshError(undefined);
    try {
      if (!(await refresh(AbortSignal.timeout(5000))))
        setRefreshError({ workId, message: "The connection changed while refreshing status." });
    } catch (error) {
      setRefreshError({
        workId,
        message: serverErrorMessage(error) ?? "Unable to refresh status.",
      });
    }
  }

  async function submit(): Promise<void> {
    if (work === undefined || !work.ready || changesSuspended) return;
    const submittedWorkId = work.workId;
    const pending: Submission = {
      workId: submittedWorkId,
      basket,
      material,
      state: "submitting",
      reason: null,
    };
    setSubmission(pending);
    try {
      await JobsBackend.completeBasketLoadStation(
        new api.BasketLoadStationCompletion({ workId: submittedWorkId }),
      );
      const state = "accepted";
      setSubmission((current) => (current === pending ? { ...pending, state } : current));
    } catch (error) {
      const state =
        api.ApiException.isApiException(error) && error.status === 409 ? "conflict" : "error";
      const reason = serverErrorMessage(error);
      setSubmission((current) => (current === pending ? { ...pending, state, reason } : current));
    }
    await refreshStatus(submittedWorkId);
  }

  const submissionDisabled =
    changesSuspended ||
    !work?.ready ||
    submissionState === "submitting" ||
    submissionState === "accepted" ||
    submissionState === "conflict";

  // Confirm sits in the header so it is visible without scrolling. Slots are drawn like pallet
  // faces: stacked regions holding their material side by side. All material in a slot moves
  // together.
  return (
    <div>
      <Box
        sx={{
          display: "grid",
          gridTemplateColumns: "minmax(max-content, 1fr) auto auto",
          alignItems: "center",
          columnGap: 4,
        }}
      >
        {title}
        <Box
          role="group"
          aria-label="Basket confirmation"
          sx={{ display: "flex", alignItems: "center", gap: 2 }}
        >
          {work ? (
            <>
              <Typography
                color="error"
                noWrap
                sx={{ visibility: changesSuspended ? "visible" : "hidden" }}
              >
                System alarm
              </Typography>
              <Button
                variant="contained"
                color="secondary"
                size="large"
                disabled={submissionDisabled}
                onClick={() => void submit()}
              >
                Confirm
              </Button>
            </>
          ) : null}
        </Box>
        <Box
          sx={{
            justifySelf: "flex-end",
            minWidth: "8ch",
            textAlign: "right",
            fontVariantNumeric: "tabular-nums",
          }}
        >
          {elapsed}
        </Box>
      </Box>
      <Stack spacing={2} sx={{ ml: "4em", mr: "4em", mt: 2 }}>
        {refreshError?.workId === work?.workId && refreshError && (
          <Alert
            severity="warning"
            action={
              <Button onClick={() => void refreshStatus(refreshError.workId)}>
                Refresh status
              </Button>
            }
          >
            {refreshError.message} Refresh current status to review the latest work.
          </Alert>
        )}
        {work?.confirmEmpty && work.ready && !changesSuspended && (
          <Typography>Confirm basket {basket.basketId} is empty.</Typography>
        )}
        {instructionWarning && <Alert severity="warning">{instructionWarning}</Alert>}
        {work && !work.ready && (
          <Alert severity="info">
            {blockedReason ||
              (work.awaitingSlots.length > 0
                ? `Waiting for material for slots ${work.awaitingSlots.map(basketSlotLabel).join(", ")}.`
                : "Basket work is not ready for confirmation.")}
          </Alert>
        )}
        {!work && workState.type === "inconsistent" ? (
          <Alert severity="warning">
            Basket work is inconsistent. Wait for refreshed material actions before confirming.
          </Alert>
        ) : null}
        {submissionState === "accepted" ? (
          <Alert severity="success">Confirmation accepted. Waiting for refreshed work.</Alert>
        ) : submissionState === "conflict" ? (
          <Alert severity="warning">
            {submissionReason ??
              "Basket work changed before confirmation. Review the refreshed slots and try again."}
          </Alert>
        ) : submissionState === "error" ? (
          <Alert severity="error">
            Unable to confirm basket work{submissionReason ? `: ${submissionReason}` : "."} No work
            was assumed complete.
          </Alert>
        ) : null}
      </Stack>
      <MoveMaterialArrowNode
        kind={{ type: MoveMaterialNodeKindType.BasketZone, basketId: basket.basketId }}
      >
        {slots.map((slot, idx) => {
          const slotMaterial = materialBySlot.get(slot) ?? [];
          const isUnknown = basket.unknownSlots?.includes(slot) === true;
          const targetContents = LazySeq.of(slotMaterial)
            .filter(
              (mat) =>
                mat.action.type !== api.ActionType.UnloadToInProcess &&
                mat.action.type !== api.ActionType.UnloadToCompletedMaterial,
            )
            .concat(loadsBySlot.get(slot) ?? [])
            .map((mat) => {
              const process =
                mat.action.type === api.ActionType.LoadingToBasket
                  ? mat.action.processAfterLoad
                  : mat.process;
              return `${mat.partName}${process !== undefined ? ` proc ${process}` : ""}`;
            })
            .distinctBy((label) => label)
            .toRArray();
          const targetEmpty =
            work?.confirmEmpty ||
            (!isUnknown && (slotMaterial.length > 0 || basket.emptySlots?.includes(slot) === true));
          const target =
            work && !work.awaitingSlots.includes(slot)
              ? targetContents.length > 0
                ? `Fill with ${targetContents.join(", ")}`
                : targetEmpty
                  ? "Leave empty"
                  : undefined
              : undefined;
          return (
            <Box
              key={slot}
              component="section"
              data-testid={`basket-load-station-slot-${slot}`}
              sx={{ borderTop: idx !== 0 ? "1px solid black" : undefined, py: 3 }}
            >
              <Box
                sx={{
                  display: "flex",
                  justifyContent: "center",
                  alignItems: "center",
                  flexWrap: "wrap",
                  columnGap: 1,
                  color: "text.secondary",
                  mb: 2,
                }}
              >
                <Typography variant="h6" sx={{ color: "text.primary" }}>
                  Slot {basketSlotLabel(slot)}
                </Typography>
                {target && (
                  <Box
                    component="span"
                    sx={{ display: "inline-flex", alignItems: "center", gap: 1 }}
                  >
                    <Typography component="span" variant="body2" color="inherit" aria-hidden>
                      ·
                    </Typography>
                    <Typography component="span" variant="body2" color="inherit">
                      {changesSuspended ? `Saved target: ${target}` : target}
                    </Typography>
                  </Box>
                )}
              </Box>
              <Box sx={{ ml: "4em", mr: "4em" }}>
                <MoveMaterialArrowNode
                  kind={{
                    type: MoveMaterialNodeKindType.BasketSlotZone,
                    basketId: basket.basketId,
                    slot,
                  }}
                >
                  <Box
                    sx={{
                      display: "flex",
                      flexWrap: "wrap",
                      justifyContent: "space-around",
                      minHeight: "4em",
                    }}
                  >
                    {slotMaterial.length > 0 ? (
                      <SlotMaterial
                        material={slotMaterial}
                        basketId={basket.basketId}
                        fsize={fsize}
                      />
                    ) : isUnknown ? (
                      <Typography color="warning.main">Unknown</Typography>
                    ) : null}
                  </Box>
                </MoveMaterialArrowNode>
              </Box>
            </Box>
          );
        })}
      </MoveMaterialArrowNode>
    </div>
  );
}
