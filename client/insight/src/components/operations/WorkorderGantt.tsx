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
import * as React from "react";
import { IActiveWorkorder } from "../../network/api";
import { PickD3Scale, scaleBand, scaleTime } from "@visx/scale";
import { atom, useAtomValue, useSetAtom } from "jotai";
import { chartTheme } from "../../util/chart-colors";
import { Axis } from "@visx/axis";
import { Grid } from "@visx/grid";
import { green } from "@mui/material/colors";
import { LazySeq } from "@seedtactics/immutable-collections";
import { addDays, differenceInDays } from "date-fns";
import { currentStatus } from "../../cell-status/current-status";
import { Box, Stack } from "@mui/material";
import { ChartTooltip } from "../ChartTooltip";
import { PartIdenticon } from "../station-monitor/Material";
import { localPoint } from "@visx/event";

interface TooltipData {
  readonly left: number;
  readonly top: number;
  readonly data: Readonly<IActiveWorkorder>;
}

const tooltipData = atom<TooltipData | null>(null);

interface ChartScales {
  readonly xScale: PickD3Scale<"time", number>;
  readonly yScale: PickD3Scale<"band", number, string>;
}

const namesWidth = 150;
const marginTop = 20;
const marginBottom = 10;
const marginLeft = 10;
const marginRight = 10;
const rowSize = 80;

function workorderKey(work: Readonly<IActiveWorkorder>): string {
  return `${work.workorderId}-${work.part}`;
}

function utcDateOnlyToLocal(d: Date): Date;
function utcDateOnlyToLocal(d: Date | null | undefined): Date | null;
function utcDateOnlyToLocal(d: Date | null | undefined): Date | null {
  if (d) {
    return new Date(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());
  } else {
    return null;
  }
}

function useScales(workorders: ReadonlyArray<Readonly<IActiveWorkorder>>): ChartScales {
  const workKeys = workorders.map(workorderKey);
  const dates = LazySeq.of(workorders)
    .flatMap((w) =>
      w.simulatedStart && w.simulatedFilled
        ? [utcDateOnlyToLocal(w.simulatedStart), addDays(utcDateOnlyToLocal(w.simulatedFilled), 2)]
        : [],
    )
    .collect((t) => t);
  const start = dates.minBy((t) => t) ?? new Date();
  const end = dates.maxBy((t) => t) ?? addDays(new Date(), 7);

  const xMax = Math.max(differenceInDays(end, start) * 50, 30);
  const yMax = Math.max(workKeys.length * rowSize, rowSize);

  const xScale = scaleTime({
    domain: [start, end],
    range: [0, xMax],
  });

  const yScale = scaleBand({
    domain: workKeys,
    range: [0, yMax],
  });

  return { xScale, yScale };
}

const Tooltip = React.memo(function Tooltip() {
  const tooltip = useAtomValue(tooltipData);

  if (tooltip === null) return null;

  return (
    <ChartTooltip top={tooltip.top} left={tooltip.left}>
      <Stack>
        <div>Workorder: {tooltip.data.workorderId}</div>
        <div>Part: {tooltip.data.part}</div>
        <div>Due Date: {tooltip.data.dueDate.toLocaleDateString()}</div>
        <div>Priority: {tooltip.data.priority}</div>
        <div>Planned Quantity: {tooltip.data.plannedQuantity}</div>
        <div>Completed Quantity: {tooltip.data.completedQuantity}</div>
        <div>Projected Start: {utcDateOnlyToLocal(tooltip.data.simulatedStart)?.toLocaleDateString()}</div>
        <div>Projected Filled: {utcDateOnlyToLocal(tooltip.data.simulatedFilled)?.toLocaleDateString()}</div>
      </Stack>
    </ChartTooltip>
  );
});

function YAxis({ workorders }: { workorders: ReadonlyArray<Readonly<IActiveWorkorder>> }) {
  return (
    <div>
      {workorders.map((work) => (
        <Stack
          key={workorderKey(work)}
          height={rowSize}
          direction="column"
          alignItems="flex-end"
          paddingRight="10px"
          justifyContent="center"
        >
          <Box color={chartTheme.axisStyles.y.left.axisLabel.fill}>{work.workorderId}</Box>
          <Box color={chartTheme.axisStyles.y.left.axisLabel.fill} display="flex" alignItems="center">
            <PartIdenticon part={work.part} size={18} />
            <Box ml="3px">{work.part}</Box>
          </Box>
        </Stack>
      ))}
    </div>
  );
}

function XAxis({ xScale, yScale }: Pick<ChartScales, "xScale" | "yScale">) {
  return (
    <>
      <Axis
        scale={xScale}
        top={0}
        orientation="top"
        labelProps={chartTheme.axisStyles.x.top.axisLabel}
        stroke={chartTheme.axisStyles.x.top.axisLine.stroke}
        strokeWidth={chartTheme.axisStyles.x.top.axisLine.strokeWidth}
        tickLength={chartTheme.axisStyles.x.top.tickLength}
        tickStroke={chartTheme.axisStyles.x.top.tickLine.stroke}
        tickLabelProps={() => chartTheme.axisStyles.x.top.tickLabel}
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

function Series({
  workorders,
  xScale,
  yScale,
}: { workorders: ReadonlyArray<Readonly<IActiveWorkorder>> } & ChartScales) {
  const setTooltip = useSetAtom(tooltipData);
  const hideTooltipRef = React.useRef<NodeJS.Timeout | null>(null);

  function showTooltip(w: Readonly<IActiveWorkorder>): (e: React.PointerEvent<SVGGElement>) => void {
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
        data: w,
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
      {workorders.map((work) => {
        const key = workorderKey(work);
        return (
          <g key={key} onMouseOver={showTooltip(work)} onMouseLeave={hideTooltip}>
            {work.simulatedStart && work.simulatedFilled ? (
              <rect
                x={xScale(utcDateOnlyToLocal(work.simulatedStart))}
                y={(yScale(key) ?? 0) + 20}
                width={
                  xScale(addDays(utcDateOnlyToLocal(work.simulatedFilled), 1)) -
                  xScale(utcDateOnlyToLocal(work.simulatedStart))
                }
                height={rowSize - 40}
                fill={green[600]}
              />
            ) : undefined}
          </g>
        );
      })}
    </g>
  );
}

export function WorkorderGantt() {
  const currentSt = useAtomValue(currentStatus);
  const sortedWorkorders = React.useMemo(
    () =>
      LazySeq.of(currentSt.workorders ?? []).toSortedArray(
        (w) => w.simulatedFilled ?? null,
        (w) => w.simulatedStart ?? null,
        (w) => w.workorderId,
        (w) => w.part,
      ),
    [currentSt.workorders],
  );
  const { xScale, yScale } = useScales(sortedWorkorders);

  return (
    <Box display="flex">
      <Box
        height={yScale.range()[1] + marginTop + marginBottom}
        width={namesWidth}
        paddingTop={`${marginTop}px`}
      >
        <YAxis workorders={sortedWorkorders} />
      </Box>
      <Box flexGrow={1} width={0} position="relative">
        <div style={{ overflowX: "visible" }}>
          <svg
            height={yScale.range()[1] + marginTop + marginBottom}
            width={xScale.range()[1] + marginRight + marginLeft}
          >
            <g transform={`translate(${marginLeft}, ${marginTop})`}>
              <XAxis yScale={yScale} xScale={xScale} />
              <Series workorders={sortedWorkorders} xScale={xScale} yScale={yScale} />
            </g>
          </svg>
        </div>
        <Tooltip />
      </Box>
    </Box>
  );
}
