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
import { PointerEvent, useRef, useCallback, useMemo } from "react";
import { IActiveWorkorder } from "../../network/api";
import { atom, useAtomValue, useSetAtom } from "jotai";
import { green } from "@mui/material/colors";
import { LazySeq } from "@seedtactics/immutable-collections";
import { addDays, differenceInDays } from "date-fns";
import { currentStatus } from "../../cell-status/current-status";
import { Box, Stack } from "@mui/material";
import { PartIdenticon } from "../station-monitor/Material";
import { localPoint } from "../../util/chart-helpers.js";
import { AxisTop, GridCols } from "../AxisAndGrid";
import { scaleBand, ScaleBand, scaleTime, ScaleTime } from "d3-scale";
import { Tooltip } from "../ChartTooltip";

interface TooltipData {
  readonly left: number;
  readonly top: number;
  readonly data: Readonly<IActiveWorkorder>;
}

const tooltipData = atom<TooltipData | null>(null);

interface ChartScales {
  readonly xScale: ScaleTime<number, number>;
  readonly yScale: ScaleBand<string>;
}

const namesWidth = 150;
const marginTop = 40;
const marginBottom = 10;
const marginLeft = 20;
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
    .flatMap((w) => {
      const filled = utcDateOnlyToLocal(w.simulatedFilled);
      return [utcDateOnlyToLocal(w.simulatedStart), filled ? addDays(filled, 2) : null];
    })
    .collect((t) => t);
  const start = dates.minBy((t) => t) ?? new Date();
  const end = dates.maxBy((t) => t) ?? addDays(new Date(), 7);

  const xMax = Math.max(differenceInDays(end, start) * 50, 30);
  const yMax = Math.max(workKeys.length * rowSize, rowSize);

  const xScale = scaleTime().domain([start, end]).range([0, xMax]);
  const yScale = scaleBand().domain(workKeys).range([0, yMax]);
  return { xScale, yScale };
}

function WorkorderTooltip({ tooltip }: { tooltip: TooltipData }) {
  return (
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
  );
}

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
          <Box>{work.workorderId}</Box>
          <Box display="flex" alignItems="center">
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
      <AxisTop scale={xScale} top={0} />
      <GridCols scale={xScale} height={yScale.range()[1] - yScale.range()[0]} />
    </>
  );
}

function Series({
  workorders,
  xScale,
  yScale,
}: { workorders: ReadonlyArray<Readonly<IActiveWorkorder>> } & ChartScales) {
  const setTooltip = useSetAtom(tooltipData);
  const hideTooltipRef = useRef<NodeJS.Timeout | null>(null);

  function showTooltip(w: Readonly<IActiveWorkorder>): (e: PointerEvent<SVGGElement>) => void {
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
  const hideTooltip = useCallback(() => {
    hideTooltipRef.current = setTimeout(() => {
      setTooltip(null);
    }, 300);
  }, [hideTooltipRef, setTooltip]);

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
  const sortedWorkorders = useMemo(
    () =>
      LazySeq.of(currentSt.workorders ?? []).toSortedArray(
        (w) => w.simulatedFilled?.getTime() ?? null,
        (w) => w.simulatedStart?.getTime() ?? null,
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
        <Tooltip
          atom={tooltipData}
          TooltipContent={WorkorderTooltip}
          chartHeight={yScale.range()[1] + marginTop + marginBottom}
          chartWidth={xScale.range()[1] + marginRight + marginLeft}
        />
      </Box>
    </Box>
  );
}
