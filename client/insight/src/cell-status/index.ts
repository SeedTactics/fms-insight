/* Copyright (c) 2021, John Lenz

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

import {
  atom,
  selector,
  TransactionInterface_UNSTABLE,
  useRecoilTransaction_UNSTABLE,
  useSetRecoilState,
} from "recoil";
import { IHistoricData, ServerEvent } from "../data/api";
import * as simProd from "./sim-production";
import * as simUse from "./sim-station-use";
import * as names from "./names";
import React from "react";
import { addDays, addMonths } from "date-fns";
import { JobsBackend, LogBackend } from "../data/backend";
import { useDispatch } from "react-redux";
import * as events from "../data/events";

export function onServerEvent(t: TransactionInterface_UNSTABLE, now: Date, evt: ServerEvent): void {
  if (evt.newJobs) {
    simProd.onNewJobs(t, evt.newJobs.jobs, now);
    simUse.onNewJobs(t, evt.newJobs.stationUse, now);
    names.onNewJobs(t, evt.newJobs.jobs);
  }
}

function onLast30Jobs(t: TransactionInterface_UNSTABLE, historicData: Readonly<IHistoricData>): void {
  simUse.onNewJobs(t, historicData.stationUse);
  simProd.onNewJobs(t, Object.values(historicData.jobs));
}

function onSpecificMonthJobs(t: TransactionInterface_UNSTABLE, historicData: Readonly<IHistoricData>): void {
  simUse.onSpecificMonthJobs(t, historicData.stationUse);
  simProd.onSpecificMonthJobs(t, Object.values(historicData.jobs));
}

const loadingJobsLast30 = atom<boolean>({ key: "insightBackendLoadingJobs", default: false });
const loadingJobsMonth = atom<boolean>({ key: "insightBackendLoadingJobsMonth", default: false });
export const loadingCellStatus = selector<boolean>({
  key: "insightBackendLoading",
  get: ({ get }) => {
    return get(loadingJobsLast30) || get(loadingJobsMonth);
  },
});

export function useLoadLast30Days(): (now: Date) => void {
  const setJobsLoading = useSetRecoilState(loadingJobsLast30);
  const handleHistoricData = useRecoilTransaction_UNSTABLE(
    (t) => (historicData: Readonly<IHistoricData>) => onLast30Jobs(t, historicData),
    []
  );
  const dispatch = useDispatch();

  return React.useCallback((now: Date) => {
    const thirtyDaysAgo = addDays(now, -30);
    dispatch({
      type: events.ActionType.LoadRecentLogEntries,
      now: now,
      pledge: LogBackend.get(thirtyDaysAgo, now),
    });

    // TODO: errors

    setJobsLoading(true);
    void JobsBackend.history(thirtyDaysAgo, now)
      .then(handleHistoricData)
      .finally(() => setJobsLoading(false));
  }, []);
}

export function useLoadSpecificMonth(): (month: Date) => void {
  const setJobsLoading = useSetRecoilState(loadingJobsMonth);
  const handleHistoricData = useRecoilTransaction_UNSTABLE(
    (t) => (historicData: Readonly<IHistoricData>) => onSpecificMonthJobs(t, historicData),
    []
  );
  const dispatch = useDispatch();

  return React.useCallback((month: Date) => {
    const startOfNextMonth = addMonths(month, 1);

    dispatch({
      type: events.ActionType.LoadSpecificMonthLogEntries,
      month: month,
      pledge: LogBackend.get(month, startOfNextMonth),
    });

    // TODO: errors

    setJobsLoading(true);
    void JobsBackend.history(month, startOfNextMonth)
      .then(handleHistoricData)
      .finally(() => setJobsLoading(false));
  }, []);
}
