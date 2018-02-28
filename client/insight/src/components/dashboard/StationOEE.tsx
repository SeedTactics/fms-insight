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
import * as React from 'react';
import { connect } from 'react-redux';
import { withStyles } from 'material-ui';
import Grid from 'material-ui/Grid';
import * as im from 'immutable';
// import * as numerable from 'numeral';
import { createSelector } from 'reselect';
import Tooltip from 'material-ui/Tooltip';

import { Store } from '../../data/store';
import { StationInUse } from '../../data/events';
import * as api from '../../data/api';

interface PalletData {
  pallet: api.IPalletStatus;
  material: api.IInProcessMaterial[];
}

export interface StationOEEProps {
  station: string;
  oee: number;
  pallet?: PalletData;
  queuedPallet?: PalletData;
}

function polarToCartesian(centerX: number, centerY: number, radius: number, angleInRadians: number) {
  return {
    x: centerX + (radius * Math.cos(angleInRadians)),
    y: centerY + (radius * Math.sin(angleInRadians))
  };
}

function describeArc(cx: number, cy: number, radius: number, startAngle: number, endAngle: number) {

    const start = polarToCartesian(cx, cy, radius, endAngle);
    const end = polarToCartesian(cx, cy, radius, startAngle);

    const largeArcFlag = endAngle - startAngle <= Math.PI ? '0' : '1';

    var d = [
        'M', start.x, start.y,
        'A', radius, radius, Math.PI / 2, largeArcFlag, '0', end.x, end.y
    ].join(' ');

    return d;
}

function computeCircle(oee: number): JSX.Element {
  if (oee < 0.02) {
    return (
      <circle
        cx={200}
        cy={200}
        r={150}
        stroke="#E0E0E0"
        strokeWidth={10}
        fill="transparent"
      />
    );
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

  let entries: {title: string, value: string}[] = [];

  if (p.pallet === undefined) {
    entries.push({title: "Pallet", value: "None"});
  } else {
    entries.push({title: "Pallet", value: p.pallet.pallet.pallet});

    for (let mat of p.pallet.material) {
      const name = mat.partName + "-" + mat.process.toString();

      let matStatus = "";
      switch (mat.action.type) {
        case api.ActionType.Loading:
          matStatus = " (loading)";
          break;
        case api.ActionType.Unloading:
          matStatus = " (unloading)";
          break;
        case api.ActionType.Machining:
          matStatus = " (machining)";
          break;
      }

      entries.push({
        title: "Part",
        value: name + matStatus
      });
    }
  }

  return (
    <>
      {
        entries.map((e, idx) =>
          <div key={idx}>
            <span>{e.title}: </span>
            <span>{e.value}</span>
          </div>
        )
      }
    </>
  );
}

// style values set to match react-vis tooltips
const tooltipStyle = withStyles(() => ({
  tooltip: {
    'backgroundColor': '#3a3a48',
    'color': '#fff',
    'fontSize': '12px'
  }
}));

export const StationOEE = tooltipStyle<StationOEEProps>(p => {
  let pallet: string = "Empty";
  if (p.pallet !== undefined) {
    pallet = p.pallet.pallet.pallet;
    if (!isNaN(parseFloat(pallet))) {
      pallet = "Pal " + pallet;
    }
  }

  return (
    <Tooltip title={computeTooltip(p)} classes={p.classes}>
      <svg viewBox="0 0 400 400">
        {computeCircle(p.oee)}
        <text x={200} y={190} textAnchor="middle" style={{fontSize: 45}}>
          {p.station}
        </text>
        <text x={200} y={250} textAnchor="middle" style={{fontSize: 30}}>
          {pallet}
        </text>
      </svg>
    </Tooltip>
  );
});

export interface StationHours {
    readonly station: string;
    readonly hours: number;
}

export interface Props {
  station_active_hours_past_week: im.Map<string, number>;
  pallets: im.Map<string, {pal?: PalletData, queued?: PalletData}>;
  system_active_hours_per_week: number;
}

export function StationOEEs(p: Props) {
  const stats =
    im.Set(p.station_active_hours_past_week.keySeq())
    .union(im.Set(p.pallets.keySeq()))
    .toSeq()
    .sortBy(s => s)
    .cacheResult();
  return (
    <Grid container justify="space-around">
      {
        stats.map((stat, idx) => (
          <Grid item xs={12} sm={6} md={4} lg={3} key={idx}>
            <StationOEE
              station={stat}
              oee={p.station_active_hours_past_week.get(stat, 0) / p.system_active_hours_per_week}
              pallet={p.pallets.get(stat, {pal: undefined}).pal}
              queuedPallet={p.pallets.get(stat, {queued: undefined}).queued}
            />
          </Grid>
        ))
      }
    </Grid>
  );
  // TODO: buffer and cart
}

export function stationHoursInLastWeek(use: im.List<StationInUse>): im.Map<string, number> {
    const m = new Map<string, number>();
    use.forEach(s => {
        m.set(s.station, (m.get(s.station) || 0) + s.hours);
    });
    return im.Map(m);
}

export function buildPallets(st: api.ICurrentStatus): im.Map<string, {pal?: PalletData, queued?: PalletData}> {

  const matByPallet = new Map<string, api.IInProcessMaterial[]>();
  for (let mat of st.material) {
    if (mat.location.type === api.LocType.OnPallet && mat.location.pallet !== undefined) {
      const mats = matByPallet.get(mat.location.pallet) || [];
      mats.push(mat);
      matByPallet.set(mat.location.pallet, mats);
    }
  }

  const m = new Map<string, {pal?: PalletData, queued?: PalletData}>();
  for (let pal of Object.values(st.pallets)) {
    switch (pal.currentPalletLocation.loc) {
      case api.PalletLocationEnum.LoadUnload:
      case api.PalletLocationEnum.Machine:
        const stat = pal.currentPalletLocation.group + " #" + pal.currentPalletLocation.num.toString();
        m.set(
          stat,
          {...(m.get(stat) ||  {}),
            pal: {
              pallet: pal,
              material: matByPallet.get(pal.pallet) || []
            }
          }
        );
        break;

      case api.PalletLocationEnum.MachineQueue:
        const stat2 = pal.currentPalletLocation.group + " #" + pal.currentPalletLocation.num.toString();
        m.set(
          stat2,
          {...(m.get(stat2) ||  {}),
            queued: {
              pallet: pal,
              material: matByPallet.get(pal.pallet) || []
            }
          }
        );
        break;

      // TODO: buffer and cart
    }
  }

  return im.Map(m);
}

const oeeSelector = createSelector(
  (s: Store) => s.Events.last30.oee.last_week_of_hours,
  stationHoursInLastWeek
);

const palSelector = createSelector(
  (s: Store) => s.Current.current_status,
  buildPallets
);

export default connect(
  (s: Store) => ({
    station_active_hours_past_week: oeeSelector(s),
    system_active_hours_per_week: s.Events.last30.oee.system_active_hours_per_week,
    pallets: palSelector(s),
  })
)(StationOEEs);