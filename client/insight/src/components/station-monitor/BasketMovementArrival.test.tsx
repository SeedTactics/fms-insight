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
      nor the names of other contributors may be used to endorse or promote
      products derived from this software without specific prior written
      permission.

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

import { act } from "react";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../network/api.js";
import {
  BasketArrivalReceipt,
  BasketLocationCorrectionCommand,
  BasketMovementArrival,
  BasketMovementCompletionReceipt,
  SubmitBasketLocationCorrection,
  SubmitBasketMovementCompletion,
} from "./BasketMovementArrival.js";

function deferred<T>(): { readonly promise: Promise<T>; readonly resolve: (value: T) => void } {
  let resolveDeferred!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((resolve) => {
    resolveDeferred = resolve;
  });
  return {
    promise,
    resolve: (value) => resolveDeferred(value),
  };
}

function makeInstruction(instructionId: string, basketId: number): api.IBasketMoveInstruction {
  return {
    instructionId,
    basketId,
    destination: new api.BasketPosition({
      location: api.BasketLocationEnum.LoadUnload,
      locationNum: 1,
    }),
    reason: api.BasketMoveReason.LoadMaterial,
    displayText: `Move basket ${basketId}`,
  };
}

function makeReceipt(
  instruction: api.IBasketMoveInstruction,
  observationId: string,
  observedBasketId: number,
): BasketArrivalReceipt {
  const command = {
    commandId: `command-${observationId}`,
    instructionId: instruction.instructionId,
    observedBasketId,
  };
  const receipt: BasketMovementCompletionReceipt = {
    stationNumber: 1,
    instructionId: instruction.instructionId,
    observationId,
    observedBasketId,
  };
  return {
    stationNumber: 1,
    instruction,
    command,
    receipt,
    status: "recorded",
  };
}

function findButton(container: HTMLElement, text: string): HTMLButtonElement {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find((candidate) =>
    candidate.textContent?.includes(text),
  );
  if (!button) throw new Error(`Could not find button containing ${text}`);
  return button;
}

async function flushDeferred<T>(promise: Promise<T>): Promise<void> {
  await act(async () => {
    await promise;
  });
}

const submitConflict: SubmitBasketMovementCompletion = async () => "conflict";

afterEach(() => {
  document.body.innerHTML = "";
});

it("ignores an arrival completion after the instruction changes", async () => {
  const completion = deferred<BasketMovementCompletionReceipt>();
  const onAccepted = vi.fn((_receipt: BasketArrivalReceipt): void => {});
  const instructionA = makeInstruction("instruction-a", 101);
  const instructionB = makeInstruction("instruction-b", 202);
  const submitCommand: SubmitBasketMovementCompletion = () => completion.promise;
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);

  try {
    await act(async () => {
      root.render(
        <BasketMovementArrival
          basketName="Basket"
          instruction={instructionA}
          onAccepted={onAccepted}
          stationNumber={1}
          submitCommand={submitCommand}
          submitCorrection={undefined}
        />,
      );
    });

    await act(async () => {
      findButton(container, "Basket 101 arrived").click();
    });
    await act(async () => {
      root.render(
        <BasketMovementArrival
          basketName="Basket"
          instruction={instructionB}
          onAccepted={onAccepted}
          stationNumber={1}
          submitCommand={submitCommand}
          submitCorrection={undefined}
        />,
      );
    });

    completion.resolve(makeReceipt(instructionA, "observation-a", 101).receipt);
    await flushDeferred(completion.promise);

    expect(onAccepted).not.toHaveBeenCalled();
  } finally {
    await act(async () => {
      root.unmount();
    });
  }
});

it("ignores a correction completion after the receipt target changes", async () => {
  const correction = deferred<"accepted" | "conflict">();
  const onCorrected = vi.fn((_command: BasketLocationCorrectionCommand): void => {});
  const instruction = makeInstruction("instruction", 101);
  const receiptA = makeReceipt(instruction, "observation-a", 101);
  const receiptB = makeReceipt(instruction, "observation-b", 202);
  const submitCorrection: SubmitBasketLocationCorrection = () => correction.promise;
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);

  try {
    await act(async () => {
      root.render(
        <BasketMovementArrival
          basketName="Basket"
          instruction={instruction}
          onCorrected={onCorrected}
          receipt={receiptA}
          stationNumber={1}
          submitCommand={submitConflict}
          submitCorrection={submitCorrection}
        />,
      );
    });

    await act(async () => {
      findButton(container, "Change").click();
      findButton(container, "Undo").click();
    });
    await act(async () => {
      root.render(
        <BasketMovementArrival
          basketName="Basket"
          instruction={instruction}
          onCorrected={onCorrected}
          receipt={receiptB}
          stationNumber={1}
          submitCommand={submitConflict}
          submitCorrection={submitCorrection}
        />,
      );
    });

    correction.resolve("accepted");
    await flushDeferred(correction.promise);

    expect(onCorrected).not.toHaveBeenCalled();
  } finally {
    await act(async () => {
      root.unmount();
    });
  }
});
