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

import { ScaleLinear, ScaleTime } from "d3-scale";

export function GridRows({
  scale,
  width,
  tickOverride,
}: {
  scale: ScaleLinear<unknown, number> | ScaleTime<unknown, number>;
  width: number;
  tickOverride?: number[];
}) {
  return (
    <g>
      {(tickOverride ?? scale.ticks()).map((tick, idx) => (
        <line
          key={idx}
          x1={0}
          y1={scale(tick)}
          x2={width}
          y2={scale(tick)}
          stroke={"#eaf0f6"}
          strokeWidth={1}
        />
      ))}
    </g>
  );
}

export function GridCols({
  scale,
  height,
  tickOverride,
}: {
  scale: ScaleLinear<unknown, number> | ScaleTime<unknown, number>;
  height: number;
  tickOverride?: number[];
}) {
  return (
    <g>
      {(tickOverride ?? scale.ticks()).map((tick, idx) => (
        <line
          key={idx}
          x1={scale(tick)}
          y1={0}
          x2={scale(tick)}
          y2={height}
          stroke={"#eaf0f6"}
          strokeWidth={1}
        />
      ))}
    </g>
  );
}

export function ChartGrid({
  xScale,
  yScale,
  width,
  height,
  rowTickOverride,
  colTickOverride,
}: {
  xScale: ScaleLinear<unknown, number> | ScaleTime<unknown, number>;
  yScale: ScaleLinear<unknown, number> | ScaleTime<unknown, number>;
  width: number;
  height: number;
  rowTickOverride?: number[];
  colTickOverride?: number[];
}) {
  return (
    <g>
      <GridRows scale={yScale} width={width} tickOverride={rowTickOverride} />
      <GridCols scale={xScale} height={height} tickOverride={colTickOverride} />
    </g>
  );
}

const tickLength = 8;

type AxisScale<T> = ((val: T) => number | undefined) & {
  domain: () => ReadonlyArray<T>;
  range: () => ReadonlyArray<number>;
  ticks?: () => ReadonlyArray<T>;
  tickFormat?: () => (val: T) => string;
  bandwidth?: () => number;
};

function axisTicks<T>(
  scale: AxisScale<T>,
  tickOverride?: ReadonlyArray<T>,
): ReadonlyArray<{ readonly tick: T; readonly pos: number }> {
  if (tickOverride) {
    return tickOverride.map((v) => ({ tick: v, pos: scale(v) ?? 0 }));
  } else if ("ticks" in scale && scale.ticks) {
    return scale.ticks().map((v) => ({ tick: v, pos: scale(v) ?? 0 }));
  } else if ("bandwidth" in scale && scale.bandwidth) {
    const b = scale.bandwidth();
    return scale.domain().map((v) => ({ tick: v, pos: (scale(v) ?? 0) + b / 2 }));
  } else {
    return scale.domain().map((v) => ({ tick: v, pos: scale(v) ?? 0 }));
  }
}

export function AxisBottom<T>({
  top,
  scale,
  label,
  fontSize,
  tickFormat,
  tickOverride,
}: {
  top: number;
  scale: AxisScale<T>;
  label?: string;
  fontSize?: number;
  tickFormat?: (val: T) => string;
  tickOverride?: ReadonlyArray<T>;
}) {
  fontSize ??= 10;

  tickFormat ??= "tickFormat" in scale && scale.tickFormat ? scale.tickFormat() : (d) => String(d);
  const ticks = axisTicks(scale, tickOverride);
  const range = scale.range();

  return (
    <g transform={`translate(0, ${top})`}>
      <line
        x1={range[0]}
        y1={0}
        x2={range[1]}
        y2={0}
        stroke="#222"
        strokeWidth={1}
        shapeRendering="crispEdges"
      />
      {ticks.map((tick, idx) => (
        <g key={idx} transform={`translate(${tick.pos}, 0)`}>
          <line y1={tickLength} y2={0} stroke="#222" strokeWidth={1} shapeRendering="crispEdges" />
          <text
            dy={tickLength + 5}
            textAnchor="middle"
            fontSize={fontSize}
            fill="#222"
            dominantBaseline="hanging"
          >
            {tickFormat(tick.tick)}
          </text>
        </g>
      ))}
      {label ? (
        <text
          x={(range[0] + range[1]) / 2}
          y={tickLength + fontSize + 20}
          textAnchor="middle"
          fontSize={fontSize + 2}
          fill="#222"
        >
          {label}
        </text>
      ) : undefined}
    </g>
  );
}

export function AxisLeft<T>({
  left,
  scale,
  label,
  fontSize,
  tickFormat,
  tickOverride,
}: {
  left: number;
  scale: AxisScale<T>;
  label?: string;
  fontSize?: number;
  tickFormat?: (val: T) => string;
  tickOverride?: ReadonlyArray<T>;
}) {
  fontSize ??= 10;

  tickFormat ??= "tickFormat" in scale && scale.tickFormat ? scale.tickFormat() : (d) => String(d);
  const ticks = axisTicks(scale, tickOverride);
  const range = scale.range();

  return (
    <g transform={`translate(${left}, 0)`}>
      <line
        x1={0}
        y1={range[0]}
        x2={0}
        y2={range[1]}
        stroke="#222"
        strokeWidth={1}
        shapeRendering="crispEdges"
      />
      {ticks.map((tick, idx) => (
        <g key={idx} transform={`translate(0, ${tick.pos})`}>
          <line
            x1={-tickLength}
            x2={0}
            y1={0}
            y2={0}
            stroke="#222"
            strokeWidth={1}
            shapeRendering="crispEdges"
          />
          <text
            dx={-tickLength - 5}
            textAnchor="end"
            fontSize={fontSize}
            fill="#222"
            dominantBaseline="middle"
          >
            {tickFormat(tick.tick)}
          </text>
        </g>
      ))}
      {label ? (
        <text
          x={-(range[0] + range[1]) / 2}
          y={-tickLength - fontSize - 20}
          textAnchor="middle"
          fontSize={fontSize + 2}
          fill="#222"
          transform="rotate(-90)"
        >
          {label}
        </text>
      ) : undefined}
    </g>
  );
}

export function AxisTop<T>({
  top,
  scale,
  label,
  fontSize,
  tickFormat,
  tickOverride,
}: {
  top: number;
  scale: AxisScale<T>;
  label?: string;
  fontSize?: number;
  tickFormat?: (val: T) => string;
  tickOverride?: ReadonlyArray<T>;
}) {
  fontSize ??= 10;

  tickFormat ??= "tickFormat" in scale && scale.tickFormat ? scale.tickFormat() : (d) => String(d);
  const ticks = axisTicks(scale, tickOverride);
  const range = scale.range();

  return (
    <g transform={`translate(0, ${top})`}>
      <line
        x1={range[0]}
        y1={0}
        x2={range[1]}
        y2={0}
        stroke="#222"
        strokeWidth={1}
        shapeRendering="crispEdges"
      />
      {ticks.map((tick, idx) => (
        <g key={idx} transform={`translate(${tick.pos}, 0)`}>
          <line y1={0} y2={-tickLength} stroke="#222" strokeWidth={1} shapeRendering="crispEdges" />
          <text dy={-tickLength - 5} textAnchor="middle" fontSize={fontSize} fill="#222">
            {tickFormat(tick.tick)}
          </text>
        </g>
      ))}
      {label ? (
        <text
          x={(range[0] + range[1]) / 2}
          y={-tickLength - fontSize - 20}
          textAnchor="middle"
          fontSize={fontSize + 2}
          fill="#222"
        >
          {label}
        </text>
      ) : undefined}
    </g>
  );
}
