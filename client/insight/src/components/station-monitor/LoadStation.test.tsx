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
import { Provider, createStore } from "jotai";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it } from "vitest";

import * as api from "../../network/api.js";
import { onLoadCurrentSt } from "../../cell-status/loading.js";
import { fmsInformation } from "../../network/server-settings.js";
import { hideNonLoadingMaterialOnLoadStation } from "../../data/queue-material.js";
import { LoadStation } from "./LoadStation.js";
import type {
  BasketMovementCompletionReceipt,
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

function makeStatus(instructions: ReadonlyArray<api.IBasketMoveInstruction>): api.ICurrentStatus {
  return {
    timeOfCurrentStatusUTC: new Date(),
    jobs: {},
    pallets: {},
    material: [],
    alarms: [],
    queues: {},
    basketMoveInstructions: instructions.map(
      (instruction) => new api.BasketMoveInstruction(instruction),
    ),
  };
}

function findButton(container: HTMLElement, text: string): HTMLButtonElement {
  const button = [...container.querySelectorAll<HTMLButtonElement>("button")].find((candidate) =>
    candidate.textContent?.includes(text),
  );
  if (!button) throw new Error(`Could not find button containing ${text}`);
  return button;
}

afterEach(() => {
  document.body.innerHTML = "";
});

it("does not resurface a receipt after a newer instruction supersedes it", async () => {
  const completion = deferred<BasketMovementCompletionReceipt>();
  const instructionA = makeInstruction("instruction-a", 101);
  const instructionB = makeInstruction("instruction-b", 202);
  const submitCommand: SubmitBasketMovementCompletion = () => completion.promise;
  const store = createStore();
  store.set(fmsInformation, { name: "FMS Insight", version: "", basketName: "Basket" });
  store.set(hideNonLoadingMaterialOnLoadStation, false);
  store.set(onLoadCurrentSt, makeStatus([instructionA]));

  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);

  try {
    await act(async () => {
      root.render(
        <Provider store={store}>
          <LoadStation
            completed={false}
            loadNum={1}
            queues={[]}
            submitBasketMovementCompletion={submitCommand}
          />
        </Provider>,
      );
    });

    await act(async () => {
      findButton(container, "Basket 101 arrived").click();
    });
    completion.resolve({
      stationNumber: 1,
      instructionId: instructionA.instructionId,
      observationId: "observation-a",
      observedBasketId: 101,
    });
    await act(async () => {
      await completion.promise;
    });
    expect(container.textContent).toContain("Basket 101 at this station");

    await act(async () => {
      store.set(onLoadCurrentSt, makeStatus([instructionB]));
      await Promise.resolve();
    });
    await act(async () => {
      store.set(onLoadCurrentSt, makeStatus([]));
      await Promise.resolve();
    });

    expect(container.textContent).not.toContain("Basket 101 at this station");
  } finally {
    await act(async () => {
      root.unmount();
    });
  }
});
