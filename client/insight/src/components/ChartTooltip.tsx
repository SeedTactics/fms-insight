/* Copyright (c) 2025, John Lenz

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

import { styled, SxProps } from "@mui/material/styles";
import { grey } from "@mui/material/colors";
import { useResizeDetector } from "react-resize-detector";
import { Atom, useAtomValue } from "jotai";
import { ComponentType } from "react";
import { Box } from "@mui/material";

const ChartTooltipContainer = styled("div")({
  backgroundColor: grey[700],
  color: "white",
  padding: "0.3rem 0.5rem",
  borderRadius: "3px",
  boxShadow: "0 1px 2px rgba(33, 33, 33, 0.2)",
  pointerEvents: "none",
});

export type TooltipAtom<T> = Atom<
  ({ readonly left: number; readonly top: number; readonly zIndex?: number } & T) | null
>;

export function Tooltip<T>({
  chartHeight,
  chartWidth,
  atom,
  TooltipContent,
}: {
  chartHeight: number;
  chartWidth: number;
  atom: TooltipAtom<T>;
  TooltipContent: ComponentType<{ tooltip: T }>;
}) {
  const pos = useAtomValue(atom);
  const {
    ref: tooltipRef,
    width: tooltipWidth,
    height: tooltipHeight,
  } = useResizeDetector<HTMLDivElement>({
    refreshMode: "debounce",
    refreshRate: 100,
  });

  if (!pos) return null;

  let tLeft = pos.left;
  let tTop = pos.top;

  if (tooltipWidth !== undefined) {
    // Adjust tooltip position if it overflows the chart boundaries
    // Check if the right edge (tLeft + 10 + tooltipWidth) is beyond the chart width
    // and also that we are past halfway across
    if (tLeft + 10 + tooltipWidth > chartWidth && tLeft + 10 > chartWidth / 2) {
      tLeft = tLeft - tooltipWidth - 30;
    } else {
      tLeft = tLeft + 10;
    }
  }

  if (tooltipHeight !== undefined) {
    // Adjust tooltip position if it overflows the chart boundaries
    // Check if the bottom edge (tTop + 10 + tooltipHeight) is beyond the chart height
    // and also that we are past halfway down
    if (tTop + 10 + tooltipHeight > chartHeight && tTop + 10 > chartHeight / 2) {
      tTop = tTop - tooltipHeight - 10;
    } else {
      tTop = tTop + 10;
    }
  }

  return (
    <div
      style={{
        visibility: tooltipHeight !== undefined && tooltipWidth !== undefined ? "visible" : "hidden",
        position: "absolute",
        left: tLeft,
        top: tTop,
        zIndex: 10,
      }}
    >
      <ChartTooltipContainer ref={tooltipRef} style={pos?.zIndex ? { zIndex: pos.zIndex } : undefined}>
        <TooltipContent tooltip={pos} />
      </ChartTooltipContainer>
    </div>
  );
}

export function ChartWithTooltip<T>({
  chart,
  tooltipAtom,
  TooltipContent,
  sx,
  autoHeight,
}: {
  chart: ({ width, height }: { width: number; height: number }) => React.ReactNode;
  tooltipAtom: TooltipAtom<T>;
  TooltipContent: ComponentType<{ tooltip: T }>;
  sx?: SxProps;
  autoHeight?: boolean;
}) {
  const {
    ref: chartRef,
    width: chartWidth,
    height: chartHeight,
  } = useResizeDetector<HTMLDivElement>({
    refreshMode: "debounce",
    refreshRate: 100,
  });

  return (
    <Box ref={chartRef} sx={sx}>
      <Box sx={{ position: "relative", overflow: "hidden" }}>
        {chartWidth && (chartHeight || autoHeight)
          ? chart({ width: chartWidth, height: chartHeight ?? 0 })
          : undefined}
        {chartHeight && chartWidth ? (
          <Tooltip
            chartHeight={chartHeight}
            chartWidth={chartWidth}
            atom={tooltipAtom}
            TooltipContent={TooltipContent}
          />
        ) : undefined}
      </Box>
    </Box>
  );
}
