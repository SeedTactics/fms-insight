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
import * as jdenticon from 'jdenticon';
import Paper from 'material-ui/Paper';
import Typography from 'material-ui/Typography';
import { ListItem, ListItemAvatar, ListItemText } from 'material-ui/List';
import ButtonBase from 'material-ui/ButtonBase';

import * as api from '../../data/api';

export interface MaterialProps {
  readonly mat: Readonly<api.IInProcessMaterial>; // TODO: deep readonly
}

export default function ManualMaterial(props: MaterialProps) {
  const iconSize = 50;
  // tslint:disable-next-line:no-any
  const icon = (jdenticon as any).toSvg(props.mat.partName, iconSize);
  return (
    <Paper elevation={4} style={{minWidth: '10em', padding: '8px'}}>
      <ButtonBase focusRipple>
        <div style={{display: 'flex', textAlign: 'left'}}>
          <div
            style={{width: iconSize, height: iconSize}}
            dangerouslySetInnerHTML={{__html: icon}}
          />
          <div style={{marginLeft: '8px', flexGrow: 1}}>
            <Typography variant="title">
              {props.mat.partName}
            </Typography>
            <div>
              <small>Serial: 01525AABC7</small>
            </div>
            <div>
              <small>{props.mat.action.type}</small>
            </div>
          </div>
        </div>
      </ButtonBase>
    </Paper>
  );
}

export function ListItemMaterial(props: MaterialProps) {
  const iconSize = 50;
  // tslint:disable-next-line:no-any
  const icon = (jdenticon as any).toSvg(props.mat.partName, iconSize);

  const details: JSX.Element = (
    <>
      <div>
        <small>Serial: 01525AABC7</small>
      </div>
      <div>
        <small>{props.mat.action.type}</small>
      </div>
    </>
  );

  return (
    <Paper elevation={4} style={{minWidth: '10em', padding: '8px'}}>
      <ListItem button>
        <ListItemAvatar>
          <div
            style={{width: iconSize, height: iconSize}}
            dangerouslySetInnerHTML={{__html: icon}}
          />
        </ListItemAvatar>
        <ListItemText primary={props.mat.partName} secondary={details}/>
      </ListItem>
    </Paper>
  );
}