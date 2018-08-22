/* Copyright (c) 2018, John Lenz

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
import Grid from "@material-ui/core/Grid";
import * as im from "immutable";
import { createSelector } from "reselect";
import Tooltip from "@material-ui/core/Tooltip";
import TimeAgo from "react-timeago";

import { connect, Store } from "../../store/store";
import { stationMinutes } from "../../data/events";
import * as api from "../../data/api";
import { duration } from "moment";
import { addSeconds, addDays } from "date-fns";
import { PalletData, buildPallets } from "../../data/load-station";

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
    y: centerY + radius * Math.sin(angleInRadians)
  };
}

function describeArc(cx: number, cy: number, radius: number, startAngle: number, endAngle: number) {
  const start = polarToCartesian(cx, cy, radius, endAngle);
  const end = polarToCartesian(cx, cy, radius, startAngle);

  const largeArcFlag = endAngle - startAngle <= Math.PI ? "0" : "1";

  var d = ["M", start.x, start.y, "A", radius, radius, Math.PI / 2, largeArcFlag, "0", end.x, end.y].join(" ");

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

function computeTooltip(p: StationOEEProps): JSX.Element {
  let entries: { title: string; value: JSX.Element }[] = [];

  entries.push({
    title: "OEE",
    value: <span>{(p.oee * 100).toFixed(1) + "%"}</span>
  });

  if (p.pallet === undefined) {
    entries.push({ title: "Pallet", value: <span>none</span> });
  } else {
    entries.push({
      title: "Pallet",
      value: <span>{p.pallet.pallet.pallet}</span>
    });

    for (let mat of p.pallet.material) {
      const name = mat.partName + "-" + mat.process.toString();

      let matStatus = "";
      let matTime: JSX.Element | undefined;
      switch (mat.action.type) {
        case api.ActionType.Loading:
          matStatus = " (loading)";
          break;
        case api.ActionType.UnloadToCompletedMaterial:
        case api.ActionType.UnloadToInProcess:
          matStatus = " (unloading)";
          break;
        case api.ActionType.Machining:
          matStatus = " (machining)";
          if (mat.action.expectedRemainingMachiningTime && p.dateOfCurrentStatus) {
            matStatus += " completing ";
            const seconds = duration(mat.action.expectedRemainingMachiningTime).asSeconds();
            matTime = <TimeAgo date={addSeconds(p.dateOfCurrentStatus, seconds)} />;
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
        )
      });
    }
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

function StationOEEWithStyles(p: StationOEEProps) {
  let pallet: string = "Empty";
  if (p.pallet !== undefined) {
    pallet = p.pallet.pallet.pallet;
    if (!isNaN(parseFloat(pallet))) {
      pallet = "Pal " + pallet;
    }
  }

  // TODO: add back tooltip
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

interface Props {
  dateOfCurrentStatus: Date | undefined;
  station_active_minutes_past_week: im.Map<string, number>;
  pallets: im.Map<string, { pal?: PalletData; queued?: PalletData }>;
}

function StationOEEs(p: Props) {
  const stats = im
    .Set(p.station_active_minutes_past_week.keySeq())
    .union(im.Set(p.pallets.keySeq()))
    .toSeq()
    .sortBy(s => [s.startsWith("L/U"), s]) // put machines first
    .cacheResult();
  return (
    <Grid data-testid="stationoee-container" container justify="space-around">
      {stats.map((stat, idx) => (
        <Grid item xs={12} sm={6} md={4} lg={3} key={idx}>
          <StationOEE
            dateOfCurrentStatus={p.dateOfCurrentStatus}
            station={stat}
            oee={p.station_active_minutes_past_week.get(stat, 0) / (60 * 24 * 7)}
            pallet={p.pallets.get(stat, { pal: undefined }).pal}
            queuedPallet={p.pallets.get(stat, { queued: undefined }).queued}
          />
        </Grid>
      ))}
    </Grid>
  );
  // TODO: buffer and cart
}

const oeeSelector = createSelector(
  (s: Store) => s.Events.last30.cycles.by_part_then_stat,
  (s: Store) => s.Current.current_status.timeOfCurrentStatusUTC,
  (byPartThenStat, lastStTime) => stationMinutes(byPartThenStat, addDays(lastStTime, -7))
);

const palSelector = createSelector((s: Store) => s.Current.current_status, buildPallets);

export default connect(s => ({
  dateOfCurrentStatus: s.Current.current_status.timeOfCurrentStatusUTC,
  station_active_minutes_past_week: oeeSelector(s),
  pallets: palSelector(s)
}))(StationOEEs);
