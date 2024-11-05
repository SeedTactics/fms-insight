/* Copyright (c) 2024, John Lenz

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

import { Atom, atom, Setter } from "jotai";
import { ServerEventAndTime } from "./loading";
import { IJob, ILogEntry, IRebooking, IRecentHistoricData, LogType, MaterialDetails } from "../network/api";
import { LazySeq, OrderedMap } from "@seedtactics/immutable-collections";
import { JobsBackend, LogBackend } from "../network/backend";
import { loadable } from "jotai/utils";
import { addDays } from "date-fns";
import { useCallback, useState } from "react";

const rebookingEvts = atom(OrderedMap.empty<string, Readonly<IRebooking>>());

// unscheduled rebookings should be loaded from the last 30 events, but it is
// possible for some to be older than 30 days, so load them here.  But don't
// wait for them to be loaded before rendering, so everything is wrapped in loadable.
const unschRebookings = loadable(
  atom(async (_, { signal }) => {
    const b = await JobsBackend.unscheduledRebookings(signal);
    return OrderedMap.build(b, (b) => b.bookingId);
  }),
);

export const last30Rebookings: Atom<OrderedMap<string, Readonly<IRebooking>>> = atom((get) => {
  const evts = get(rebookingEvts);
  const unsch = get(unschRebookings);

  if (unsch.state === "hasData") {
    return evts.union(unsch.data, (fromEvt, fromUnsch) => fromEvt ?? fromUnsch);
  } else {
    return evts;
  }
});

const canceledRebookingsRW = atom(OrderedMap.empty<string, Date>());
export const canceledRebookings: Atom<OrderedMap<string, Date>> = canceledRebookingsRW;

type ScheduledBooking = {
  readonly scheduledTime: Date;
  readonly jobUnique: string;
};

const scheduledRW = atom(OrderedMap.empty<string, ScheduledBooking>());
export const last30ScheduledBookings: Atom<OrderedMap<string, ScheduledBooking>> = scheduledRW;

function convertLogToRebooking(log: Readonly<ILogEntry>): Readonly<IRebooking> {
  let qty = 1;
  if (log.details?.["Quantity"]) {
    qty = parseInt(log.details["Quantity"], 10);
  }
  let mat: MaterialDetails | undefined;
  if (log.material?.[0]?.id >= 0) {
    const m = log.material[0];
    mat = new MaterialDetails({
      materialID: m.id,
      jobUnique: m.uniq,
      partName: m.part,
      numProcesses: m.numproc,
      workorder: m.workorder,
      serial: m.serial,
    });
  }

  const restrictedProcs = LazySeq.of(log.material ?? [])
    .map((m) => m.proc)
    .filter((p) => p > 0)
    .toSortedArray((p) => p);

  return {
    bookingId: log.result,
    partName: log.program,
    quantity: qty,
    timeUTC: log.endUTC,
    priority: log.locnum,
    notes: log.details?.["Notes"],
    workorder: log.details?.["Workorder"] ?? log.material?.[0]?.workorder,
    restrictedProcs: restrictedProcs as number[],
    material: mat,
  };
}

export const setLast30Rebookings = atom(null, (get, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(rebookingEvts, (old) =>
    old.union(
      LazySeq.of(log)
        .filter((e) => e.type === LogType.Rebooking)
        .toOrderedMap((e) => [e.result, convertLogToRebooking(e)]),
    ),
  );
  set(canceledRebookingsRW, (old) =>
    old.union(
      LazySeq.of(log)
        .filter((e) => e.type === LogType.CancelRebooking)
        .toOrderedMap((e) => [e.result, e.endUTC]),
    ),
  );
});

function updateJobs(set: Setter, jobs: LazySeq<Readonly<IJob>>, expire?: Date) {
  set(scheduledRW, (old) => {
    if (expire) {
      old = old.filter((b) => b.scheduledTime >= expire);
    }
    return old.union(
      jobs
        .flatMap((j) =>
          (j.bookings ?? []).map(
            (b) =>
              [
                b,
                {
                  scheduledTime: j.routeStartUTC,
                  jobUnique: j.unique,
                },
              ] as const,
          ),
        )
        .toOrderedMap((x) => x),
    );
  });
}

export const setLast30RebookingJobs = atom(null, (_, set, historic: Readonly<IRecentHistoricData>) => {
  updateJobs(
    set,
    LazySeq.ofObject(historic.jobs).map(([_, j]) => j),
  );
});

export const updateLast30Rebookings = atom(null, (get, set, { evt, now, expire }: ServerEventAndTime) => {
  if (evt.newJobs) {
    updateJobs(set, LazySeq.of(evt.newJobs.jobs), expire ? addDays(now, -30) : undefined);
  } else if (evt.logEntry) {
    const e = evt.logEntry;
    if (e.type === LogType.Rebooking) {
      set(rebookingEvts, (old) => old.set(e.result, convertLogToRebooking(e)));
    } else if (e.type === LogType.CancelRebooking) {
      set(canceledRebookingsRW, (old) => old.set(e.result, e.endUTC));
    }
  }
});

export function useCancelRebooking(): [(bookingId: string) => Promise<void>, boolean] {
  const [loading, setLoading] = useState(false);
  const callback = useCallback((bookingId: string) => {
    setLoading(true);
    return LogBackend.cancelRebooking(bookingId)
      .then(() => {})
      .catch(console.log)
      .finally(() => setLoading(false));
  }, []);
  return [callback, loading];
}

export type NewRebooking = {
  readonly part: string;
  readonly qty?: number;
  readonly workorder?: string | null;
  readonly restrictedProcs?: ReadonlyArray<number> | null;
  readonly priority?: number | null;
  readonly notes?: string;
};

export function useNewRebooking(): [(n: NewRebooking) => Promise<void>, boolean] {
  const [loading, setLoading] = useState(false);
  const callback = useCallback((n: NewRebooking) => {
    setLoading(true);
    return LogBackend.requestRebookingWithoutMaterial(
      n.part,
      n.qty && !isNaN(n.qty) ? n.qty : 1,
      n.workorder,
      n.restrictedProcs as number[],
      n.priority !== undefined && n.priority !== null && !isNaN(n.priority) ? n.priority : undefined,
      n.notes,
    )
      .then(() => {})
      .catch(console.log)
      .finally(() => setLoading(false));
  }, []);
  return [callback, loading];
}
