/* Copyright (c) 2022, John Lenz

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

import * as React from "react";
import { selector, useRecoilValue } from "recoil";
import { last30StationCycles, stat_name_and_num } from "../../cell-status/station-cycles.js";
import {
  last30EstimatedCycleTimes,
  PartAndStationOperation,
  isOutlier,
} from "../../cell-status/estimated-cycle-times.js";
import { addHours, addMinutes } from "date-fns";
import { PickD3Scale, scaleBand, scaleTime } from "@visx/scale";
import { Grid } from "@visx/grid";
import { Group } from "@visx/group";
import { OrderedSet } from "@seedtactics/immutable-collections";
import { Axis } from "@visx/axis";
import { chartTheme } from "../../util/chart-colors.js";

interface RecentCycle {
  readonly station: string;
  readonly startTime: Date;
  readonly endActive?: Date;
  readonly endOccupied?: Date;
  readonly outlier: boolean;
}

const activeColor = "green";
const occupiedNonOutlierColor = "blue";
const occupiedOutlierColor = "red";

const recentCycles = selector<ReadonlyArray<RecentCycle>>({
  key: "insight-recent-cycles-for-chart",
  get: ({ get }) => {
    const allCycles = get(last30StationCycles);
    const estimated = get(last30EstimatedCycleTimes);
    const cutoff = addHours(new Date(), -12);
    return allCycles
      .valuesToLazySeq()
      .filter((c) => c.x >= cutoff)
      .map((c) => {
        const stats = estimated.get(PartAndStationOperation.ofPartCycle(c));
        return {
          station: stat_name_and_num(c.stationGroup, c.stationNumber),
          startTime: addMinutes(c.x, -c.y),
          endActive: addMinutes(c.x, c.activeMinutes - c.y),
          endOccupied: c.x,
          outlier: stats ? isOutlier(stats, c.y) : false,
        };
      })
      .toRArray();
  },
});

interface ChartScales {
  readonly xScale: PickD3Scale<"time", number>;
  readonly yScale: PickD3Scale<"band", number, string>;
  readonly actualPlannedScale: PickD3Scale<"band", number, "actual" | "planned">;
}

const marginLeft = 50;
const marginBottom = 20;
const marginTop = 10;
const marginRight = 2;

function useScales(
  cycles: ReadonlyArray<RecentCycle>,
  now: Date,
  containerWidth: number,
  containerHeight: number
): ChartScales {
  const stats = OrderedSet.build(cycles, (c) => c.station);

  const xMax = containerWidth - marginLeft - marginRight;
  const yMax = containerHeight - marginTop - marginBottom;

  const xScale = scaleTime({
    domain: [addHours(now, -12), addHours(now, 8)],
    range: [0, xMax],
  });

  const yScale = scaleBand({
    domain: Array.from(stats),
    range: [0, yMax],
    padding: 0.3,
  });

  const actualPlannedScale = scaleBand({
    domain: ["actual", "planned"],
    range: [0, yScale.bandwidth()],
    padding: 0.1,
  }) as PickD3Scale<"band", number, "actual" | "planned">;

  return { xScale, yScale, actualPlannedScale };
}

function AxisAndGrid({ xScale, yScale }: Pick<ChartScales, "xScale" | "yScale">): JSX.Element {
  return (
    <>
      <Axis
        scale={xScale}
        top={yScale.range()[1]}
        orientation="bottom"
        labelProps={chartTheme.axisStyles.y.left.axisLabel}
        stroke={chartTheme.axisStyles.x.bottom.axisLine.stroke}
        strokeWidth={chartTheme.axisStyles.x.bottom.axisLine.strokeWidth}
        tickLength={chartTheme.axisStyles.x.bottom.tickLength}
        tickStroke={chartTheme.axisStyles.x.bottom.tickLine.stroke}
        tickLabelProps={() => chartTheme.axisStyles.x.bottom.tickLabel}
      />
      <Axis
        scale={yScale}
        orientation="left"
        left={xScale.range()[0]}
        labelProps={chartTheme.axisStyles.y.left.axisLabel}
        stroke={chartTheme.axisStyles.y.left.axisLine.stroke}
        strokeWidth={chartTheme.axisStyles.y.left.axisLine.strokeWidth}
        tickLength={chartTheme.axisStyles.y.left.tickLength}
        tickStroke={chartTheme.axisStyles.y.left.tickLine.stroke}
        tickLabelProps={() => ({ ...chartTheme.axisStyles.y.left.tickLabel, width: marginLeft })}
      />
      <Grid
        xScale={xScale}
        yScale={yScale}
        width={xScale.range()[1] - xScale.range()[0]}
        height={yScale.range()[1] - yScale.range()[0]}
        rowLineStyle={chartTheme.gridStyles}
        columnLineStyle={chartTheme.gridStyles}
      />
    </>
  );
}

function ActualSeries({
  cycles,
  xScale,
  yScale,
  actualPlannedScale,
  now,
}: ChartScales & { cycles: ReadonlyArray<RecentCycle>; now: Date }): JSX.Element {
  const actualOffset = actualPlannedScale("actual") ?? 0;
  return (
    <g>
      {cycles.map((c, i) => {
        if (c.endActive) {
          return (
            <g key={i}>
              <rect
                x={xScale(c.startTime)}
                y={(yScale(c.station) ?? 0) + actualOffset}
                width={xScale(c.endActive) - xScale(c.startTime)}
                height={actualPlannedScale.bandwidth()}
                fill={activeColor}
              />
              <rect
                x={xScale(c.endActive)}
                y={(yScale(c.station) ?? 0) + actualOffset}
                width={xScale(c.endOccupied ?? now) - xScale(c.endActive)}
                height={actualPlannedScale.bandwidth()}
                fill={c.outlier ? occupiedOutlierColor : occupiedNonOutlierColor}
              />
            </g>
          );
        } else if (c.endOccupied) {
          // no active time known, so just assume whole thing is ok.
          return (
            <g key={i}>
              <rect
                x={xScale(c.startTime)}
                y={(yScale(c.station) ?? 0) + actualOffset}
                width={xScale(c.endOccupied) - xScale(c.startTime)}
                height={actualPlannedScale.bandwidth()}
                fill={activeColor}
              />
            </g>
          );
        } else {
          // no active time known, and this is a currently running cycle.
          // Just assume everything is OK
          return (
            <g key={i}>
              <rect
                x={xScale(c.startTime)}
                y={(yScale(c.station) ?? 0) + actualOffset}
                width={xScale(now) - xScale(c.startTime)}
                height={actualPlannedScale.bandwidth()}
                fill={activeColor}
              />
            </g>
          );
        }
      })}
    </g>
  );
}

export function RecentCycleChart({ height, width }: { height: number; width: number }) {
  const cycles = useRecoilValue(recentCycles);
  const now = new Date();
  const { xScale, yScale, actualPlannedScale } = useScales(cycles, now, width, height);
  return (
    <svg height={height} width={width}>
      <clipPath id="recent-cycle-clip-body">
        <rect
          x={marginLeft}
          y={marginTop}
          width={width - marginLeft - marginRight}
          height={height - marginTop - marginBottom}
        />
      </clipPath>
      <Group left={marginLeft} top={marginTop}>
        <AxisAndGrid xScale={xScale} yScale={yScale} />
        <g clipPath="url(#recent-cycle-clip-body)">
          <ActualSeries
            cycles={cycles}
            xScale={xScale}
            yScale={yScale}
            now={now}
            actualPlannedScale={actualPlannedScale}
          />
        </g>
      </Group>
    </svg>
  );
}
