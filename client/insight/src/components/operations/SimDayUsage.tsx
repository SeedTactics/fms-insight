/* Copyright (c) 2023, John Lenz

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
import { ReactNode, useMemo } from "react";
import { Box, Stack, Tooltip, Typography } from "@mui/material";
import { green, grey } from "@mui/material/colors";
import { LazySeq, OrderedMap, OrderedSet } from "@seedtactics/immutable-collections";
import { atom, useAtomValue } from "jotai";
import { latestSimDayUsage } from "../../cell-status/sim-day-usage";
import { ISimulatedDayUsage } from "../../network/api";
import { useSetTitle } from "../routes";
import { scaleLinear } from "@visx/scale";
import { Warning as WarningIcon } from "@mui/icons-material";

const color1 = green[50];
const color2 = green[600];
const downtimeColor = grey[100];
const monthBoxSize = "3em";
const monthWidth = `calc(${monthBoxSize} * 7)`;

type UsageMap = OrderedMap<string, OrderedMap<Date, Readonly<ISimulatedDayUsage>>>;

const groupedSimDayUsage = atom<UsageMap | null>((get) => {
  const usage = get(latestSimDayUsage);
  if (usage === null) return null;
  return LazySeq.of(usage.usage).toLookupOrderedMap(
    (u) => u.machineGroup,
    (u) => new Date(u.day.getUTCFullYear(), u.day.getUTCMonth(), u.day.getUTCDate()),
  );
});

const machineGroups = atom<OrderedSet<string>>((get) => {
  const usage = get(latestSimDayUsage);
  if (usage === null) return OrderedSet.empty();
  return OrderedSet.build(usage.usage, (u) => u.machineGroup);
});

const usageMonths = atom<OrderedSet<Date>>((get) => {
  const usage = get(latestSimDayUsage);
  if (usage === null) return OrderedSet.empty();
  return OrderedSet.build(usage.usage, (u) => new Date(u.day.getUTCFullYear(), u.day.getUTCMonth(), 1));
});

const maxUsage = atom<number>((get) => {
  const usage = get(latestSimDayUsage);
  if (usage === null) return 0;
  return (
    LazySeq.of(usage.usage)
      .map((u) => Math.ceil(u.usage))
      .maxBy((u) => u) ?? 1
  );
});

const minAndMaxUsageDay = atom<[Date | null, Date | null]>((get) => {
  const usage = get(latestSimDayUsage);
  if (usage === null) return [null, null];
  return [
    LazySeq.of(usage.usage)
      .map((u) => new Date(u.day.getUTCFullYear(), u.day.getUTCMonth(), u.day.getUTCDate()))
      .minBy((d) => d) ?? null,
    LazySeq.of(usage.usage)
      .map((u) => new Date(u.day.getUTCFullYear(), u.day.getUTCMonth(), u.day.getUTCDate()))
      .maxBy((d) => d) ?? null,
  ];
});

function ShowMonth({
  date,
  dayColor,
  tooltip,
}: {
  date: Date;
  dayColor: (d: Date) => string;
  tooltip: (d: Date) => ReactNode;
}) {
  const dayStart = new Date(date.getFullYear(), date.getMonth(), 1).getDay();
  const numDaysInMonth = new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate();
  return (
    <Box width={monthWidth}>
      <Typography variant="h6" textAlign="center">
        {date.toLocaleString("default", { month: "long", year: "numeric" })}
      </Typography>
      <Box display="flex" flexWrap="wrap">
        {LazySeq.ofRange(0, dayStart).map((_, i) => (
          <Box key={i} height={monthBoxSize} width={monthBoxSize} />
        ))}
        {LazySeq.ofRange(1, numDaysInMonth + 1).map((d, i) => (
          <div
            key={i}
            style={{
              width: monthBoxSize,
              height: monthBoxSize,
              cursor: "default",
              backgroundColor: dayColor(new Date(date.getFullYear(), date.getMonth(), d)),
            }}
          >
            <Tooltip title={tooltip(new Date(date.getFullYear(), date.getMonth(), d))}>
              <Box display="flex" justifyContent="center" alignItems="center" height="100%">
                {d}
              </Box>
            </Tooltip>
          </div>
        ))}
      </Box>
    </Box>
  );
}

function Warning() {
  const warning = useAtomValue(latestSimDayUsage)?.warning;
  return (
    <Stack direction="row" spacing={2} alignItems="center">
      <WarningIcon fontSize="small" />
      <Typography variant="caption">{warning ? ` ${warning}` : "Projected dates are estimates"}</Typography>
    </Stack>
  );
}

function MonthHeatmap({ group, month }: { group: string; month: Date }) {
  const usage = useAtomValue(groupedSimDayUsage);
  const [minDay, maxDay] = useAtomValue(minAndMaxUsageDay);
  const maxUse = useAtomValue(maxUsage);

  const dayColor = useMemo(() => {
    const scale = scaleLinear({
      domain: [0, maxUse],
      range: [color1, color2],
    });

    return (d: Date): string => {
      if (maxDay && d > maxDay) {
        return "white";
      }
      if (minDay && d < minDay) {
        return "white";
      }
      const u = usage?.get(group)?.get(d);
      if (u) {
        return scale(u.usage);
      } else {
        return downtimeColor;
      }
    };
  }, [usage, maxUse, minDay, maxDay, group]);

  function tooltip(d: Date): ReactNode {
    const u = usage?.get(group)?.get(d);
    return (
      <>
        <div>{d.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" })}</div>
        {maxDay && d > maxDay ? (
          <div>Not Simulated</div>
        ) : minDay && d < minDay ? (
          <div />
        ) : u ? (
          <div>Usage: {u.usage.toFixed(2)}</div>
        ) : (
          <div>Downtime</div>
        )}
      </>
    );
  }

  return <ShowMonth date={month} dayColor={dayColor} tooltip={tooltip} />;
}

export function SimDayUsagePage() {
  useSetTitle("Projected Machine Usage");
  const groups = useAtomValue(machineGroups);
  const months = useAtomValue(usageMonths);
  return (
    <Box component="main" padding="24px">
      <Stack direction="column" spacing={5}>
        <Warning />
        {groups.toAscLazySeq().map((g) => (
          <div key={g}>
            <Typography variant="h4">{g}</Typography>
            <Box display="flex" flexWrap="wrap" columnGap="50px">
              {months.toAscLazySeq().map((m) => (
                <MonthHeatmap key={m.getTime()} group={g} month={m} />
              ))}
            </Box>
          </div>
        ))}
      </Stack>
    </Box>
  );
}
