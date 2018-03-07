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
import Divider from 'material-ui/Divider';
import { withStyles } from 'material-ui';

import { MaterialList, LoadStationData } from '../../data/load-station';
import Material from './Material';

const materialStyle = withStyles(() => ({
  container: {
    width: '100%',
    minHeight: '70px',
    position: 'relative' as 'relative',
  },
  labelContainer: {
    position: 'absolute' as 'absolute',
    top: '4px',
    left: '4px',
  },
  label: {
    color: 'rgba(0,0,0,0.5)',
    fontSize: 'small',
  },
  material: {
    marginTop: '8px',
    marginBottom: '8px',
    display: 'flex' as 'flex',
    flexWrap: 'wrap' as 'wrap',
    justifyContent: 'space-around' as 'space-around',
  }
}));

export interface MaterialDisplayProps {
  readonly material: MaterialList;
  readonly label: string;
}

export const MaterialDisplay = materialStyle<MaterialDisplayProps>(props => {
  return (
    <div className={props.classes.container}>
      <div className={props.classes.labelContainer}>
        <span className={props.classes.label}>
          {props.label}
        </span>
      </div>
      {
        props.material.length === 0 ? undefined :
          <div className={props.classes.material}>
            {
              props.material.map((m, idx) =>
                <Material key={idx} mat={m}/>
              )
            }
          </div>
      }
    </div>
  );
});

export interface LoadStationProps extends LoadStationData {
  readonly fillViewPort: boolean;
}

const palletStyles = withStyles(() => ({
  palletContainerFill: {
    width: '100%',
    position: 'relative' as 'relative',
    flexGrow: 1,
  },
  palletContainerScroll: {
    width: '100%',
    position: 'relative' as 'relative',
    minHeight: '12em',
  },
  labelContainer: {
    position: 'absolute' as 'absolute',
    top: '4px',
    left: '4px',
  },
  label: {
    color: 'rgba(0,0,0,0.5)',
    fontSize: 'small',
  },
  faceContainer: {
    marginLeft: '4em',
    marginRight: '4em',
  },
}));

export const PalletColumn = palletStyles<LoadStationProps>(props => {
  let palletClass: string;
  if (props.fillViewPort) {
    palletClass = props.classes.palletContainerFill;
  } else {
    palletClass = props.classes.palletContainerScroll;
  }

  const maxFace = props.face.map((m, face) => face).max();
  const palLabel = "Pallet " + (props.pallet ? props.pallet.pallet : "");

  let palDetails: JSX.Element;
  if (props.face.size === 1) {
    const mat = props.face.first();
    palDetails = <MaterialDisplay label={palLabel} material={mat ? mat : []}/>;
  } else {
    palDetails = (
      <>
        <div className={props.classes.labelContainer}>
          <span className={props.classes.label}>{palLabel}</span>
        </div>
        <div className={props.classes.faceContainer}>
          {
            props.face.toSeq().sortBy((data, face) => face).map((data, face) =>
              <div key={face}>
                <MaterialDisplay label={"Face " + face.toString()} material={data}/>
                {face === maxFace ? undefined : <Divider key={1}/>}
              </div>
            ).valueSeq()
          }
        </div>
      </>
    );
  }

  return (
    <>
      <MaterialDisplay label="Castings" material={props.castings}/>
      <Divider/>
      <div className={palletClass}>
        {palDetails}
      </div>
      <Divider/>
      <MaterialDisplay label="Completed Material" material={[]}/>
    </>
  );
});

export default function LoadStation(props: LoadStationProps) {
  const palColStyle: React.CSSProperties = {
    'flexBasis': '60%',
    'padding': '8px',
    'display': 'flex',
    'flexDirection': 'column',
  };
  const inProcColStyle: React.CSSProperties = {
    'flexBasis': '40%',
    'padding': '8px',
    'display': 'flex',
    'flexDirection': 'column',
    'borderLeft': '1px solid rgba(0, 0, 0, 0.12)',
  };
  const containerStyle: React.CSSProperties = {
    width: '100%',
    paddingLeft: '8px',
    paddingRight: '8px',
    display: 'flex',
    flexGrow: 1,
  };

  return (
    <div style={containerStyle}>
      <div style={palColStyle}>
        <PalletColumn {...props}/>
      </div>
      <div style={inProcColStyle}>
        <MaterialDisplay label="In Process Material" material={props.free}/>
      </div>
    </div>
  );
}