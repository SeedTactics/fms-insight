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
import { last30StationCycles } from "../../cell-status/station-cycles.js";
import { last30EstimatedCycleTimes } from "../../cell-status/estimated-cycle-times.js";
import { RecentCycle, recentCycles } from "../../data/results.cycles.js";
import { addHours, addMinutes, differenceInMinutes } from "date-fns";
import { PickD3Scale, scaleBand, scaleTime } from "@visx/scale";
import { Grid } from "@visx/grid";
import { Group } from "@visx/group";
import { Axis } from "@visx/axis";
import { LazySeq, OrderedSet } from "@seedtactics/immutable-collections";
import { chartTheme } from "../../util/chart-colors.js";
import { last30SimStationUse } from "../../cell-status/sim-station-use.js";
import { red, green, grey } from "@mui/material/colors";
import { localPoint } from "@visx/event";
import { Stack } from "@mui/material";
import { ChartTooltip } from "../ChartTooltip.js";
import { CurrentCycle, currentCycles } from "../../data/current-cycles.js";
import { currentStatus } from "../../cell-status/current-status.js";
import { last30Jobs } from "../../cell-status/scheduled-jobs.js";
import { atom, useAtomValue, useSetAtom } from "jotai";

const projectedColor = green[200];
const activeColor = green[600];
const occupiedNonOutlierColor = green[900];
const occupiedOutlierColor = red[700];
const simColor = grey[400];
const downtimeColor = grey[100];

type SimCycle = {
  readonly station: string;
  readonly start: Date;
  readonly end: Date;
  readonly utilizationTime: number;
  readonly plannedDownTime: number;
  readonly parts: ReadonlyArray<string>;
};

function useSimCycles(): ReadonlyArray<SimCycle> {
  const jobs = useAtomValue(last30Jobs);
  const statUse = useAtomValue(last30SimStationUse);
  return React.useMemo(() => {
    const cutoff = addHours(new Date(), -12);
    return LazySeq.of(statUse)
      .filter((s) => s.end >= cutoff)
      .map((s) => ({
        station: s.station,
        start: s.start,
        end: s.end,
        utilizationTime: s.utilizationTime,
        plannedDownTime: s.plannedDownTime,
        parts: LazySeq.of(s.parts ?? [])
          .collect((p) => {
            const j = jobs.get(p.uniq);
            if (!j) return null;
            return j.partName + "-" + p.proc.toString();
          })
          .distinctAndSortBy((p) => p)
          .toRArray(),
      }))
      .toRArray();
  }, [jobs, statUse]);
}

interface TooltipData {
  readonly left: number;
  readonly top: number;
  readonly data:
    | { kind: "actual"; cycle: RecentCycle }
    | { kind: "sim"; cycle: SimCycle }
    | { kind: "current"; cycle: CurrentCycle; now: Date };
}

const tooltipData = atom<TooltipData | null>(null);

interface ChartScales {
  readonly xScale: PickD3Scale<"time", number>;
  readonly yScale: PickD3Scale<"band", number, string>;
  readonly actualPlannedScale: PickD3Scale<"band", number, "actual" | "planned">;
}

const marginLeft = 150;
const marginBottom = 20;
const marginTop = 10;
const marginRight = 2;

function useScales(
  cycles: ReadonlyArray<RecentCycle>,
  current: ReadonlyArray<CurrentCycle>,
  now: Date,
  containerWidth: number,
  containerHeight: number,
): ChartScales {
  const stats = OrderedSet.build(cycles, (c) => c.station).union(OrderedSet.build(current, (c) => c.station));

  const xMax = Math.max(containerWidth - marginLeft - marginRight, 5);
  const yMax = Math.max(containerHeight - marginTop - marginBottom, 5);

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
    domain: ["actual", "planned"] as ["actual", "planned"],
    range: [0, yScale.bandwidth()],
    padding: 0.1,
  });

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
        tickLabelProps={() => ({
          ...chartTheme.axisStyles.y.left.tickLabel,
          width: marginLeft,
          fontSize: "large",
        })}
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

function RecentSeries({
  cycles,
  xScale,
  yScale,
  actualPlannedScale,
  hideTooltipRef,
}: ChartScales & {
  cycles: ReadonlyArray<RecentCycle>;
  hideTooltipRef: React.MutableRefObject<NodeJS.Timeout | null>;
}): JSX.Element {
  const actualOffset = actualPlannedScale("actual") ?? 0;
  const setTooltip = useSetAtom(tooltipData);

  function showTooltip(c: RecentCycle): (e: React.PointerEvent<SVGGElement>) => void {
    return (e) => {
      const pt = localPoint(e);
      if (!pt) return;
      if (hideTooltipRef.current !== null) {
        clearTimeout(hideTooltipRef.current);
        hideTooltipRef.current = null;
      }
      setTooltip({
        left: pt.x,
        top: pt.y,
        data: { kind: "actual", cycle: c },
      });
    };
  }
  const hideTooltip = React.useCallback(() => {
    hideTooltipRef.current = setTimeout(() => {
      setTooltip(null);
    }, 300);
  }, []);

  return (
    <g>
      {cycles.map((c, i) => {
        if (c.endActive && c.endActive < c.endOccupied) {
          return (
            <g key={i} onMouseOver={showTooltip(c)} onMouseLeave={hideTooltip}>
              <rect
                x={xScale(c.startTime)}
                y={(yScale(c.station) ?? 0) + actualOffset}
                width={xScale(c.endOccupied) - xScale(c.startTime)}
                height={actualPlannedScale.bandwidth()}
                fill={activeColor}
              />
              <rect
                x={xScale(c.endActive)}
                y={(yScale(c.station) ?? 0) + actualOffset + actualPlannedScale.bandwidth() / 10}
                width={xScale(c.endOccupied) - xScale(c.endActive)}
                height={(actualPlannedScale.bandwidth() * 8) / 10}
                fill={c.outlier ? occupiedOutlierColor : occupiedNonOutlierColor}
              />
            </g>
          );
        } else {
          // no active time known, so just assume whole thing is ok.
          return (
            <g key={i} onMouseOver={showTooltip(c)} onMouseLeave={hideTooltip}>
              <rect
                x={xScale(c.startTime)}
                y={(yScale(c.station) ?? 0) + actualOffset}
                width={xScale(c.endOccupied) - xScale(c.startTime)}
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

function halfCirclePath(x: number, y: number, rx: number, ry: number): string {
  return `M ${x} ${y} A ${rx} ${ry} 0 0 1 ${x} ${y + ry}`;
}

function CurrentSeries({
  now,
  cycles,
  xScale,
  yScale,
  actualPlannedScale,
  hideTooltipRef,
}: ChartScales & {
  now: Date;
  cycles: ReadonlyArray<CurrentCycle>;
  hideTooltipRef: React.MutableRefObject<NodeJS.Timeout | null>;
}): JSX.Element {
  const actualOffset = actualPlannedScale("actual") ?? 0;
  const setTooltip = useSetAtom(tooltipData);

  function showTooltip(c: CurrentCycle): (e: React.PointerEvent<SVGGElement>) => void {
    return (e) => {
      const pt = localPoint(e);
      if (!pt) return;
      if (hideTooltipRef.current !== null) {
        clearTimeout(hideTooltipRef.current);
        hideTooltipRef.current = null;
      }
      setTooltip({
        left: pt.x,
        top: pt.y,
        data: { kind: "current", cycle: c, now },
      });
    };
  }
  const hideTooltip = React.useCallback(() => {
    hideTooltipRef.current = setTimeout(() => {
      setTooltip(null);
    }, 300);
  }, []);

  return (
    <g>
      {cycles.map((c, i) => {
        return (
          <g key={i} onMouseOver={showTooltip(c)} onMouseLeave={hideTooltip}>
            <rect
              x={xScale(c.start)}
              y={(yScale(c.station) ?? 0) + actualOffset}
              width={xScale(now) - xScale(c.start)}
              height={actualPlannedScale.bandwidth()}
              fill={activeColor}
            />
            {c.expectedEnd < now ? (
              <>
                <rect
                  x={xScale(c.expectedEnd)}
                  y={(yScale(c.station) ?? 0) + actualOffset + actualPlannedScale.bandwidth() / 10}
                  width={xScale(now) - xScale(c.expectedEnd)}
                  height={(actualPlannedScale.bandwidth() * 8) / 10}
                  fill={c.isOutlier ? occupiedOutlierColor : occupiedNonOutlierColor}
                />
                <path
                  d={halfCirclePath(
                    xScale(now),
                    (yScale(c.station) ?? 0) + actualOffset,
                    80, // rx
                    actualPlannedScale.bandwidth(),
                  )}
                  fill={projectedColor}
                />
              </>
            ) : (
              <rect
                x={xScale(now)}
                y={(yScale(c.station) ?? 0) + actualOffset}
                width={xScale(c.expectedEnd) - xScale(now)}
                height={actualPlannedScale.bandwidth()}
                fill={projectedColor}
              />
            )}
          </g>
        );
      })}
    </g>
  );
}

function SimSeries({
  sim,
  xScale,
  yScale,
  actualPlannedScale,
  hideTooltipRef,
}: ChartScales & {
  sim: ReadonlyArray<SimCycle>;
  hideTooltipRef: React.MutableRefObject<NodeJS.Timeout | null>;
}): JSX.Element {
  const plannedOffset = actualPlannedScale("planned") ?? 0;

  const setTooltip = useSetAtom(tooltipData);

  function showTooltip(c: SimCycle): (e: React.PointerEvent<SVGGElement>) => void {
    return (e) => {
      const pt = localPoint(e);
      if (!pt) return;
      if (hideTooltipRef.current !== null) {
        clearTimeout(hideTooltipRef.current);
        hideTooltipRef.current = null;
      }
      setTooltip({
        left: pt.x,
        top: pt.y,
        data: { kind: "sim", cycle: c },
      });
    };
  }
  const hideTooltip = React.useCallback(() => {
    hideTooltipRef.current = setTimeout(() => {
      setTooltip(null);
    }, 300);
  }, []);

  return (
    <g>
      {sim.map((c, i) => (
        <g key={i} onMouseOver={showTooltip(c)} onMouseLeave={hideTooltip}>
          {c.utilizationTime > 0 ? (
            <rect
              x={xScale(c.start)}
              y={(yScale(c.station) ?? 0) + plannedOffset}
              width={xScale(addMinutes(c.start, c.utilizationTime)) - xScale(c.start)}
              height={actualPlannedScale.bandwidth()}
              fill={simColor}
            />
          ) : undefined}
          {c.plannedDownTime > 0 ? (
            <rect
              x={xScale(c.start)}
              y={(yScale(c.station) ?? 0) + plannedOffset}
              width={xScale(addMinutes(c.start, c.plannedDownTime)) - xScale(c.start)}
              height={actualPlannedScale.bandwidth()}
              fill={downtimeColor}
            />
          ) : undefined}
        </g>
      ))}
    </g>
  );
}

const Tooltip = React.memo(function Tooltip() {
  const tooltip = useAtomValue(tooltipData);

  if (tooltip === null) return null;

  return (
    <ChartTooltip top={tooltip.top} left={tooltip.left}>
      <Stack>
        {tooltip.data.kind === "actual" ? (
          <>
            {tooltip.data.cycle.outlier ? (
              <div>Outlier Cycle for {tooltip.data.cycle.station}</div>
            ) : (
              <div>{tooltip.data.cycle.station}</div>
            )}
            <div>Start: {tooltip.data.cycle.startTime.toLocaleString()}</div>
            <div>End: {tooltip.data.cycle.endOccupied.toLocaleString()}</div>
            {tooltip.data.cycle.endActive !== undefined ? (
              <div>
                Active Minutes:{" "}
                {differenceInMinutes(tooltip.data.cycle.endActive, tooltip.data.cycle.startTime)}
              </div>
            ) : undefined}
            <div>
              Occupied Minutes:{" "}
              {differenceInMinutes(tooltip.data.cycle.endOccupied, tooltip.data.cycle.startTime)}
            </div>
            {tooltip.data.cycle.parts.map((p, idx) => (
              <div key={idx}>
                Part: {p.part} {p.oper}
              </div>
            ))}
          </>
        ) : tooltip.data.kind === "sim" ? (
          <>
            <div>Simulation of {tooltip.data.cycle.station}</div>
            {tooltip.data.cycle.parts.map((p, idx) => (
              <div key={idx}>Part: {p}</div>
            ))}
            <div>Predicted Start: {tooltip.data.cycle.start.toLocaleString()}</div>
            <div>Predicted End: {tooltip.data.cycle.end.toLocaleString()}</div>
            {tooltip.data.cycle.utilizationTime > 0 ? (
              <div>Utilization Minutes: {tooltip.data.cycle.utilizationTime}</div>
            ) : undefined}
            {tooltip.data.cycle.plannedDownTime > 0 ? (
              <div>Planned Downtime Minutes: {tooltip.data.cycle.plannedDownTime}</div>
            ) : undefined}
          </>
        ) : (
          <>
            {tooltip.data.cycle.isOutlier ? (
              <div>Current Outlier Cycle for {tooltip.data.cycle.station}</div>
            ) : (
              <div>Current {tooltip.data.cycle.station}</div>
            )}
            <div>Start: {tooltip.data.cycle.start.toLocaleString()}</div>
            <div>Expected End: {tooltip.data.cycle.expectedEnd.toLocaleString()}</div>
            {tooltip.data.cycle.expectedEnd < tooltip.data.now ? (
              <div>
                Cycle Exceeding Expected By{" "}
                {differenceInMinutes(tooltip.data.now, tooltip.data.cycle.expectedEnd)} Minutes
              </div>
            ) : (
              <div>
                Expected Remaining Minutes:{" "}
                {differenceInMinutes(tooltip.data.cycle.expectedEnd, tooltip.data.now)}
              </div>
            )}
            <div>Occupied Minutes: {differenceInMinutes(tooltip.data.now, tooltip.data.cycle.start)}</div>
            {tooltip.data.cycle.parts.map((p, idx) => (
              <div key={idx}>
                Part: {p.part} {p.oper}
              </div>
            ))}
          </>
        )}
      </Stack>
    </ChartTooltip>
  );
});

function NowLine({
  now,
  xScale,
  yScale,
}: Pick<ChartScales, "xScale" | "yScale"> & { now: Date }): JSX.Element {
  const x = xScale(now);
  const fontSize = 11;
  return (
    <g>
      <line
        x1={x}
        x2={x}
        y1={yScale.range()[0]}
        y2={yScale.range()[1] + chartTheme.axisStyles.x.bottom.tickLength}
        stroke="black"
      />
      <text
        x={x}
        y={yScale.range()[1] + chartTheme.axisStyles.x.bottom.tickLength + fontSize}
        textAnchor="middle"
        fontSize={fontSize}
      >
        Now
      </text>
    </g>
  );
}

export function RecentCycleChart({ height, width }: { height: number; width: number }) {
  const last30Cycles = useAtomValue(last30StationCycles);
  const estimated = useAtomValue(last30EstimatedCycleTimes);
  const sim = useSimCycles();
  const currentSt = useAtomValue(currentStatus);

  const cycles = React.useMemo(() => {
    const cutoff = addHours(new Date(), -12);
    return recentCycles(
      last30Cycles.valuesToLazySeq().filter((e) => e.x >= cutoff),
      estimated,
    );
  }, [last30Cycles, estimated]);

  const current = React.useMemo(() => {
    return currentCycles(currentSt, estimated);
  }, [currentSt, estimated]);

  // ensure a re-render at least every 5 minutes, but reset the timer if the data changes
  const now = new Date();
  const [, forceRerender] = React.useState<number>(0);
  const refreshRef = React.useRef<NodeJS.Timeout | null>(null);
  React.useEffect(() => {
    if (refreshRef.current !== null) clearTimeout(refreshRef.current);
    refreshRef.current = setTimeout(
      () => {
        forceRerender((x) => x + 1);
      },
      5 * 60 * 1000,
    );
  });

  const { xScale, yScale, actualPlannedScale } = useScales(cycles, current, now, width, height);
  const hideTooltipRef = React.useRef<NodeJS.Timeout | null>(null);

  if (height <= 0 || width <= 0) return null;

  return (
    <div style={{ position: "relative" }}>
      <svg height={height} width={width}>
        <Group left={marginLeft} top={marginTop}>
          <clipPath id="recent-cycle-clip-body">
            <rect
              x={0}
              y={0}
              width={width - marginRight - marginLeft}
              height={height - marginBottom - marginTop}
            />
          </clipPath>
          <AxisAndGrid xScale={xScale} yScale={yScale} />
          <g clipPath="url(#recent-cycle-clip-body)">
            <RecentSeries
              cycles={cycles}
              xScale={xScale}
              yScale={yScale}
              hideTooltipRef={hideTooltipRef}
              actualPlannedScale={actualPlannedScale}
            />
            <CurrentSeries
              now={now}
              cycles={current}
              xScale={xScale}
              yScale={yScale}
              hideTooltipRef={hideTooltipRef}
              actualPlannedScale={actualPlannedScale}
            />
            <SimSeries
              sim={sim}
              xScale={xScale}
              yScale={yScale}
              actualPlannedScale={actualPlannedScale}
              hideTooltipRef={hideTooltipRef}
            />
          </g>
          <NowLine now={now} xScale={xScale} yScale={yScale} />
        </Group>
      </svg>
      <Tooltip />
    </div>
  );
}
