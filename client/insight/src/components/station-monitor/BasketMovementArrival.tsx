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

import { useEffect, useRef, useState } from "react";
import { Alert, Box, Button, Paper, Stack, TextField, Typography } from "@mui/material";

import * as api from "../../network/api.js";

export interface BasketMovementCompletionCommand {
  readonly commandId: string;
  readonly instructionId: string;
  readonly observedBasketId: number;
}

export interface BasketMovementCompletionReceipt {
  readonly stationNumber: number;
  readonly instructionId: string;
  readonly observationId: string;
  readonly observedBasketId: number;
}

export type SubmitBasketMovementCompletion = (
  stationNumber: number,
  command: BasketMovementCompletionCommand,
) => Promise<BasketMovementCompletionReceipt | "conflict">;

export interface BasketLocationCorrectionCommand {
  readonly correctionId: string;
  readonly targetObservationId: string;
  readonly replacementBasketId: number | null;
  readonly replacementObservationId: string | null;
}

export type SubmitBasketLocationCorrection = (
  stationNumber: number,
  command: BasketLocationCorrectionCommand,
) => Promise<"accepted" | "conflict">;

export interface BasketArrivalReceipt {
  readonly stationNumber: number;
  readonly instruction: Readonly<api.IBasketMoveInstruction>;
  readonly command: BasketMovementCompletionCommand;
  readonly receipt: BasketMovementCompletionReceipt;
  readonly status: "recorded" | "corrected" | "retracted";
}

type Submission =
  | {
      readonly state: "submitting" | "conflict";
      readonly command: BasketMovementCompletionCommand;
    }
  | {
      readonly state: "accepted";
      readonly command: BasketMovementCompletionCommand;
      readonly receipt: BasketMovementCompletionReceipt;
    }
  | {
      readonly state: "error";
      readonly command: BasketMovementCompletionCommand;
      readonly message: string;
    };

type CorrectionSubmission =
  | {
      readonly state: "submitting" | "accepted" | "conflict";
      readonly command: BasketLocationCorrectionCommand;
    }
  | {
      readonly state: "error";
      readonly command: BasketLocationCorrectionCommand;
    };

export function loadStationArrivalInstruction(
  instructions: ReadonlyArray<Readonly<api.IBasketMoveInstruction>>,
  stationNumber: number,
): Readonly<api.IBasketMoveInstruction> | undefined {
  const activeIds = new Set(instructions.map((instruction) => instruction.instructionId));
  return instructions.find(
    (instruction) =>
      instruction.basketId !== undefined &&
      instruction.destination.location === api.BasketLocationEnum.LoadUnload &&
      instruction.destination.locationNum === stationNumber &&
      (instruction.prerequisiteInstructionId === undefined ||
        !activeIds.has(instruction.prerequisiteInstructionId)),
  );
}

export function BasketMovementArrival({
  stationNumber,
  basketName,
  instruction,
  submitCommand,
  submitCorrection,
  receipt,
  onAccepted,
  onCorrected,
}: {
  readonly stationNumber: number;
  readonly basketName: string;
  readonly instruction: Readonly<api.IBasketMoveInstruction>;
  readonly submitCommand: SubmitBasketMovementCompletion | undefined;
  readonly submitCorrection: SubmitBasketLocationCorrection | undefined;
  readonly receipt?: BasketArrivalReceipt;
  readonly onAccepted?: (receipt: BasketArrivalReceipt) => void;
  readonly onCorrected?: (command: BasketLocationCorrectionCommand) => void;
}) {
  const [chooseDifferent, setChooseDifferent] = useState(false);
  const [differentBasket, setDifferentBasket] = useState("");
  const [submission, setSubmission] = useState<Submission>();
  const [changingRecordedBasket, setChangingRecordedBasket] = useState(false);
  const [correctionSubmission, setCorrectionSubmission] = useState<CorrectionSubmission>();
  const currentInstructionId = useRef(instruction.instructionId);
  const currentStationNumber = useRef(stationNumber);
  const currentCorrectionTargetId = useRef<string | undefined>(undefined);
  currentInstructionId.current = instruction.instructionId;
  currentStationNumber.current = stationNumber;

  useEffect(() => {
    setChooseDifferent(false);
    setDifferentBasket("");
    setSubmission((current) =>
      current?.state === "error" && current.command.instructionId === instruction.instructionId
        ? current
        : undefined,
    );
    setChangingRecordedBasket(false);
    setCorrectionSubmission(undefined);
  }, [instruction.instructionId, stationNumber]);
  useEffect(() => {
    setChangingRecordedBasket(false);
    setDifferentBasket("");
    setCorrectionSubmission((current) =>
      current?.state === "error" &&
      receipt?.status !== "retracted" &&
      current.command.targetObservationId === receipt?.receipt.observationId
        ? current
        : undefined,
    );
  }, [receipt?.receipt.observationId, receipt?.status]);

  const expectedBasketId = instruction.basketId;
  if (expectedBasketId === undefined) {
    return (
      <Alert severity="warning">
        This tracked basket must be handled through the recovery workflow.
      </Alert>
    );
  }

  async function record(command: BasketMovementCompletionCommand): Promise<void> {
    if (submitCommand === undefined) return;
    setSubmission({ state: "submitting", command });
    try {
      const result = await submitCommand(stationNumber, command);
      if (
        currentStationNumber.current !== stationNumber ||
        currentInstructionId.current !== command.instructionId
      )
        return;
      if (result === "conflict") {
        setSubmission({ state: result, command });
      } else {
        setSubmission({ state: "accepted", command, receipt: result });
        onAccepted?.({
          stationNumber,
          instruction,
          command,
          receipt: result,
          status: "recorded",
        });
      }
    } catch (error: unknown) {
      if (
        currentStationNumber.current !== stationNumber ||
        currentInstructionId.current !== command.instructionId
      )
        return;
      setSubmission({
        state: "error",
        command,
        message: error instanceof Error ? error.message : "Unable to record basket arrival",
      });
    }
  }

  async function correct(command: BasketLocationCorrectionCommand): Promise<void> {
    if (submitCorrection === undefined || activeCommand === undefined) return;
    setCorrectionSubmission({ state: "submitting", command });
    try {
      const result = await submitCorrection(stationNumber, command);
      if (
        currentStationNumber.current !== stationNumber ||
        currentCorrectionTargetId.current !== command.targetObservationId
      )
        return;
      setCorrectionSubmission({ state: result, command });
      if (result === "accepted") onCorrected?.(command);
    } catch {
      if (
        currentStationNumber.current !== stationNumber ||
        currentCorrectionTargetId.current !== command.targetObservationId
      )
        return;
      setCorrectionSubmission({ state: "error", command });
    }
  }

  function createCommand(basketId: number): BasketMovementCompletionCommand {
    return {
      commandId: crypto.randomUUID(),
      instructionId: instruction.instructionId,
      observedBasketId: basketId,
    };
  }

  const parsedDifferentBasket = Number(differentBasket);
  const activeCommand =
    receipt?.status === "retracted"
      ? undefined
      : (receipt?.command ??
        (submission?.state === "accepted" &&
        submission.command.instructionId === instruction.instructionId
          ? submission.command
          : undefined));
  const activeObservationId =
    receipt?.status === "retracted"
      ? undefined
      : (receipt?.receipt.observationId ??
        (submission?.state === "accepted" ? submission.receipt.observationId : undefined));
  currentCorrectionTargetId.current = activeObservationId;
  function createCorrection(replacementBasketId: number | null): BasketLocationCorrectionCommand {
    return {
      correctionId: crypto.randomUUID(),
      targetObservationId: activeObservationId ?? "",
      replacementBasketId,
      replacementObservationId: replacementBasketId === null ? null : crypto.randomUUID(),
    };
  }
  const validDifferentBasket =
    Number.isInteger(parsedDifferentBasket) &&
    parsedDifferentBasket > 0 &&
    parsedDifferentBasket !== (activeCommand?.observedBasketId ?? expectedBasketId);
  const disabled =
    submitCommand === undefined ||
    activeCommand !== undefined ||
    (submission !== undefined && submission.state !== "error");
  const receiptMessage =
    receipt?.status === "retracted"
      ? "Arrival observation retracted."
      : receipt?.status === "corrected"
        ? `Arrival corrected to ${basketName.toLocaleLowerCase()} ${receipt.command.observedBasketId}.`
        : "Arrival recorded. Waiting for updated status.";

  return (
    <Paper component="section" elevation={2} sx={{ margin: 1, padding: 2 }}>
      <Stack spacing={2}>
        <Box>
          <Typography variant="overline">
            {receipt === undefined ? "Basket arrival" : "Basket arrival receipt"}
          </Typography>
          <Typography variant="h4">
            {receipt?.status === "retracted"
              ? "Retracted basket arrival"
              : receipt === undefined
                ? `Bring ${basketName.toLocaleLowerCase()} ${expectedBasketId} to this station`
                : `${basketName} ${receipt.command.observedBasketId} at this station`}
          </Typography>
          {receipt === undefined ? (
            <Typography>Record arrival after the basket is physically in place.</Typography>
          ) : null}
        </Box>

        {receipt === undefined ? (
          <Stack direction={{ xs: "column", sm: "row" }} spacing={1}>
            <Button
              disabled={disabled}
              onClick={() => void record(createCommand(expectedBasketId))}
              size="large"
              variant="contained"
            >
              {submission?.state === "submitting"
                ? "Recording arrival…"
                : `${basketName} ${expectedBasketId} arrived`}
            </Button>
            <Button
              disabled={disabled}
              onClick={() => setChooseDifferent((visible) => !visible)}
              size="large"
              variant="outlined"
            >
              Different {basketName.toLocaleLowerCase()}
            </Button>
          </Stack>
        ) : null}

        {receipt === undefined && chooseDifferent && !disabled ? (
          <Stack direction={{ xs: "column", sm: "row" }} spacing={1}>
            <TextField
              label={`${basketName} number`}
              onChange={(event) => setDifferentBasket(event.target.value)}
              type="number"
              value={differentBasket}
              slotProps={{ htmlInput: { inputMode: "numeric", min: 1 } }}
            />
            <Button
              disabled={!validDifferentBasket}
              onClick={() => void record(createCommand(parsedDifferentBasket))}
              variant="contained"
            >
              Record {basketName.toLocaleLowerCase()} arrival
            </Button>
          </Stack>
        ) : null}

        {submitCommand === undefined ? (
          <Alert severity="warning">Basket arrival completion is not configured.</Alert>
        ) : activeCommand !== undefined || receipt?.status === "retracted" ? (
          <Stack spacing={1}>
            <Alert severity="success">{receiptMessage}</Alert>
            {submitCorrection === undefined || activeCommand === undefined ? null : (
              <Stack direction={{ xs: "column", sm: "row" }} spacing={1}>
                <Button
                  disabled={correctionSubmission !== undefined}
                  onClick={() => setChangingRecordedBasket((visible) => !visible)}
                  variant="outlined"
                >
                  Change
                </Button>
                <Button
                  color="error"
                  disabled={correctionSubmission !== undefined}
                  onClick={() => void correct(createCorrection(null))}
                  variant="text"
                >
                  Undo
                </Button>
              </Stack>
            )}
            {changingRecordedBasket && correctionSubmission === undefined ? (
              <Stack direction={{ xs: "column", sm: "row" }} spacing={1}>
                <TextField
                  label={`Correct ${basketName.toLocaleLowerCase()} number`}
                  onChange={(event) => setDifferentBasket(event.target.value)}
                  type="number"
                  value={differentBasket}
                  slotProps={{ htmlInput: { inputMode: "numeric", min: 1 } }}
                />
                <Button
                  disabled={!validDifferentBasket}
                  onClick={() => void correct(createCorrection(parsedDifferentBasket))}
                  variant="contained"
                >
                  Save correction
                </Button>
              </Stack>
            ) : null}
            {correctionSubmission?.state === "accepted" ? (
              <Alert severity="success">Correction recorded.</Alert>
            ) : correctionSubmission?.state === "conflict" ? (
              <Alert severity="warning">
                Basket evidence changed. Review the refreshed status.
              </Alert>
            ) : correctionSubmission?.state === "error" ? (
              <Alert
                action={
                  <Button
                    color="inherit"
                    onClick={() => void correct(correctionSubmission.command)}
                  >
                    Retry correction
                  </Button>
                }
                severity="error"
              >
                Unable to correct the recorded arrival.
              </Alert>
            ) : null}
          </Stack>
        ) : submission?.state === "conflict" ? (
          <Alert severity="warning">
            Basket movement changed. Review the refreshed instruction.
          </Alert>
        ) : submission?.state === "error" ? (
          <Alert
            action={
              <Button onClick={() => void record(submission.command)} color="inherit">
                Retry arrival
              </Button>
            }
            severity="error"
          >
            {submission.message}
          </Alert>
        ) : null}
      </Stack>
    </Paper>
  );
}
