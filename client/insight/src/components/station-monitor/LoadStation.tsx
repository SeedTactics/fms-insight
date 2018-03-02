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
import * as im from 'immutable';
import Divider from 'material-ui/Divider';

import * as api from '../../data/api';
import Material from './Material';

function RegionLabel({label}: {label: string}) {
  const labelStyle = {
    color: 'rgba(0,0,0,0.5)',
    fontSize: 'small',
  };
  return (
    <div style={{float: 'left'}}>
      <span style={labelStyle}>
        {label}
      </span>
    </div>
  );
}

export interface FaceData {
  readonly currentMaterial: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
}

export interface FaceProps extends FaceData {
  readonly face: number;
}

export function PalletFace(props: FaceProps) {
  const matContainerStyle = {
    marginTop: '8px',
    marginBottom: '8px',
    display: 'flex' as 'flex',
    flexWrap: 'wrap' as 'wrap',
    justifyContent: 'space-around' as 'space-around',
  };
  return (
    <div style={{minHeight: '3em'}}>
      <RegionLabel label={"Face " + props.face.toString()}/>
      <div style={matContainerStyle}>
        {
          props.currentMaterial.map((m, idx) =>
            <Material key={idx} mat={m}/>
          )
        }
      </div>
    </div>
  );
}

export interface PalletProps {
  readonly byFace: im.Map<number, FaceData>;
}

export function Pallet(props: PalletProps) {
  const maxFace = props.byFace.keySeq().max();
  return (
    <div style={{marginLeft: '4em', marginRight: '4em'}}>
      {
        props.byFace.toSeq().sortBy((data, face) => face).map((data, face) =>
          <div key={face}>
            <PalletFace key={0} face={face} currentMaterial={data.currentMaterial}/>
            {face === maxFace ? undefined : <Divider key={1}/>}
          </div>
        ).valueSeq()
      }
    </div>
  );
}

export interface LoadStationData extends PalletProps {
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly material: ReadonlyArray<Readonly<api.IInProcessMaterial>>;
}

export interface LoadStationProps extends LoadStationData {
  readonly fillViewPort: boolean;
}

export function selectLoadStationProps(loadNum: number, curSt: Readonly<api.ICurrentStatus>): LoadStationData {
  let pal: Readonly<api.IPalletStatus> | undefined;
  for (let p of Object.values(curSt.pallets)) {
    if (p.currentPalletLocation.loc === api.PalletLocationEnum.LoadUnload && p.currentPalletLocation.num === loadNum) {
      pal = p;
      break;
    }
  }
  if (pal === undefined) {
    return {
      pallet: undefined,
      material: [],
      byFace: im.Map(),
    };
  }

  const byFace = new Map<number, Readonly<api.InProcessMaterial>[]>();
  for (let mat of curSt.material) {
    if (mat.location.type === api.LocType.OnPallet && mat.location.pallet === pal.pallet) {
      if (byFace.has(mat.location.face)) {
        (byFace.get(mat.location.face) || []).push(mat);
      } else {
        byFace.set(mat.location.face, [mat]);
      }
    }
  }

  return {
    pallet: pal,
    material: [],
    byFace: im.Map(byFace).map(ms => ({currentMaterial: ms}))
  };
}

export default function LoadStation(props: LoadStationProps) {
  let palletStyle: React.CSSProperties = {
    'width': '100%',
  };
  if (props.fillViewPort) {
    palletStyle.flexGrow = 1;
  } else {
    palletStyle.minHeight = '12em';
  }
  return (
    <>
      <div style={{width: '100%', minHeight: '4em'}}>
        <RegionLabel label="Castings"/>
      </div>
      <Divider/>
      <div style={palletStyle}>
        <RegionLabel label="Pallet"/>
        <Pallet byFace={props.byFace}/>
      </div>
      <Divider/>
      <div style={{width: '100%', minHeight: '4em'}}>
        <RegionLabel label="Completed Material"/>
      </div>
    </>
  );
}