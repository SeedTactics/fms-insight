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
import * as React from "react";
import { Grid } from "@mui/material";
import { Tooltip } from "@mui/material";
import TimeAgo from "react-timeago";

import * as api from "../../network/api";
import { addSeconds, addDays } from "date-fns";
import { PalletData, buildPallets } from "../../data/load-station";
import { stationMinutes } from "../../data/results.cycles";
import { useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status";
import { durationToSeconds } from "../../util/parseISODuration";
import { last30StationCycles } from "../../cell-status/station-cycles";

interface StationOEEProps {
  readonly dateOfCurrentStatus: Date | undefined;
  readonly station: string;
  readonly oee: number;
  readonly pallet?: PalletData;
  readonly queuedPallet?: PalletData;
}

function polarToCartesian(centerX: number, centerY: number, radius: number, angleInRadians: number) {
  return {
    x: centerX + radius * Math.cos(angleInRadians),
    y: centerY + radius * Math.sin(angleInRadians),
  };
}

function describeArc(cx: number, cy: number, radius: number, startAngle: number, endAngle: number) {
  const start = polarToCartesian(cx, cy, radius, endAngle);
  const end = polarToCartesian(cx, cy, radius, startAngle);

  const largeArcFlag = endAngle - startAngle <= Math.PI ? "0" : "1";

  const d = ["M", start.x, start.y, "A", radius, radius, Math.PI / 2, largeArcFlag, "0", end.x, end.y].join(" ");

  return d;
}

function computeCircle(oee: number): JSX.Element {
  if (oee < 0.02) {
    return <circle cx={200} cy={200} r={150} stroke="#E0E0E0" strokeWidth={10} fill="transparent" />;
  } else {
    const arcPoint = -Math.PI / 2 + 2 * Math.PI * oee;
    return (
      <>
        <path
          d={describeArc(200, 200, 150, -Math.PI / 2, arcPoint)}
          fill="transparent"
          stroke="#795548"
          strokeWidth={10}
        />
        <path
          d={describeArc(200, 200, 150, arcPoint, 1.5 * Math.PI)}
          fill="transparent"
          stroke="#E0E0E0"
          strokeWidth={10}
        />
      </>
    );
  }
}

function palletMaterial(
  dateOfCurrentStatus: Date | undefined,
  material: Iterable<Readonly<api.IInProcessMaterial>>
): { title: string; value: JSX.Element }[] {
  const entries: { title: string; value: JSX.Element }[] = [];

  for (const mat of material) {
    const name = mat.partName + "-" + mat.process.toString();

    let matStatus = "";
    let matTime: JSX.Element | undefined;
    switch (mat.action.type) {
      case api.ActionType.Loading:
        if (
          (mat.action.loadOntoPallet !== undefined && mat.action.loadOntoPallet !== mat.location.pallet) ||
          (mat.action.loadOntoFace !== undefined && mat.action.loadOntoFace !== mat.location.face)
        ) {
          matStatus = " (loading)";
        }
        break;
      case api.ActionType.UnloadToCompletedMaterial:
      case api.ActionType.UnloadToInProcess:
        matStatus = " (unloading)";
        break;
      case api.ActionType.Machining:
        matStatus = " (machining)";
        if (mat.action.expectedRemainingMachiningTime && dateOfCurrentStatus) {
          matStatus += " completing ";
          const seconds = durationToSeconds(mat.action.expectedRemainingMachiningTime);
          matTime = <TimeAgo date={addSeconds(dateOfCurrentStatus, seconds)} />;
        }
        break;
    }

    entries.push({
      title: "Part",
      value: (
        <>
          <span>{name + matStatus}</span>
          {matTime}
        </>
      ),
    });
  }

  return entries;
}

function computeTooltip(p: StationOEEProps): JSX.Element {
  const entries: { title: string; value: JSX.Element }[] = [];

  entries.push({
    title: "OEE",
    value: <span>{(p.oee * 100).toFixed(1) + "%"}</span>,
  });

  if (p.pallet === undefined) {
    entries.push({ title: "Pallet", value: <span>none</span> });
  } else {
    entries.push({
      title: "Pallet",
      value: <span>{p.pallet.pallet.pallet}</span>,
    });

    entries.push(...palletMaterial(p.dateOfCurrentStatus, p.pallet.material));
  }

  if (p.queuedPallet !== undefined) {
    entries.push({
      title: "Queued Pallet",
      value: <span>{p.queuedPallet.pallet.pallet}</span>,
    });
    entries.push(...palletMaterial(p.dateOfCurrentStatus, p.queuedPallet.material));
  }

  return (
    <>
      {entries.map((e, idx) => (
        <div key={idx}>
          <span>{e.title}: </span>
          {e.value}
        </div>
      ))}
    </>
  );
}

function isMaterialOverdue(dateOfCurrentStatus: Date | undefined, p: PalletData): boolean {
  if (!dateOfCurrentStatus) {
    return false;
  }
  for (const mat of p.material) {
    if (mat.action.expectedRemainingMachiningTime) {
      const seconds = durationToSeconds(mat.action.expectedRemainingMachiningTime);
      if (seconds < 0) {
        return true;
      }
    }
  }
  return false;
}

function StationOEEWithStyles(p: StationOEEProps) {
  let pallet: JSX.Element = <tspan>Empty</tspan>;
  if (p.pallet !== undefined) {
    if (isNaN(parseFloat(p.pallet.pallet.pallet))) {
      pallet = (
        <tspan fill={isMaterialOverdue(p.dateOfCurrentStatus, p.pallet) ? "#C62828" : "#1B5E20"}>
          {p.pallet.pallet.pallet}
        </tspan>
      );
    } else {
      pallet = (
        <tspan fill={isMaterialOverdue(p.dateOfCurrentStatus, p.pallet) ? "#C62828" : "#1B5E20"}>
          Pal {p.pallet.pallet.pallet}
        </tspan>
      );
    }
  }
  let queued: JSX.Element | undefined;
  if (p.queuedPallet !== undefined) {
    if (isNaN(parseFloat(p.queuedPallet.pallet.pallet))) {
      queued = <tspan fill="#F57F17">{p.queuedPallet.pallet.pallet}</tspan>;
    } else {
      queued = <tspan fill="#F57F17">Pal {p.queuedPallet.pallet.pallet}</tspan>;
    }
  }

  return (
    <Tooltip title={computeTooltip(p)}>
      <svg viewBox="0 0 400 400">
        {computeCircle(p.oee)}
        <text x={200} y={190} textAnchor="middle" style={{ fontSize: 45 }}>
          {p.station}
        </text>
        <text x={200} y={250} textAnchor="middle" style={{ fontSize: 30 }}>
          {pallet}
        </text>
        {queued ? (
          <text x={200} y={300} textAnchor="middle" style={{ fontSize: 30 }}>
            {queued}
          </text>
        ) : undefined}
      </svg>
    </Tooltip>
  );
}

// decorate doesn't work well with classes yet.
// https://github.com/Microsoft/TypeScript/issues/4881
class StationOEE extends React.PureComponent<StationOEEProps> {
  render() {
    return <StationOEEWithStyles {...this.props} />;
  }
}

export default React.memo(function StationOEEs() {
  const currentSt = useRecoilValue(currentStatus);
  const pallets = React.useMemo(() => buildPallets(currentSt), [currentSt]);
  const cycles = useRecoilValue(last30StationCycles);
  const stationMins = React.useMemo(
    () => stationMinutes(cycles.valuesToLazySeq(), addDays(currentSt.timeOfCurrentStatusUTC, -7)),
    [cycles, currentSt.timeOfCurrentStatusUTC]
  );

  const stats = pallets
    .toLazySeq()
    .map((p) => p[0])
    .concat(stationMins.toLazySeq().map((s) => s[0]))
    .distinct()
    .toSortedArray(
      (s) => s.startsWith("L/U"),
      (s) => s
    );
  return (
    <Grid data-testid="stationoee-container" container justifyContent="space-around">
      {stats.map((stat, idx) => (
        <Grid item xs={12} sm={6} md={4} lg={3} key={idx}>
          <StationOEE
            dateOfCurrentStatus={currentSt.timeOfCurrentStatusUTC}
            station={stat}
            oee={(stationMins.get(stat) ?? 0) / (60 * 24 * 7)}
            pallet={pallets.get(stat)?.pal}
            queuedPallet={pallets.get(stat)?.queued}
          />
        </Grid>
      ))}
    </Grid>
  );
});
