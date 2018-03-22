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
import { withStyles } from 'material-ui';
import Grid from 'material-ui/Grid';
import Card, { CardContent, CardHeader } from 'material-ui/Card';

/*
import { connect } from 'react-redux';
import Divider from 'material-ui/Divider';
import * as im from 'immutable';
import { createSelector } from 'reselect';

import { MaterialList, LoadStationData, selectLoadStationProps } from '../../data/load-station';
import { Material, ConnectedMaterialDialog } from './Material';
import * as api from '../../data/api';
import * as routes from '../../data/routes';
import { Store } from '../../data/store';
import * as matDetails from '../../data/material-details';
*/

export interface InspectionProps {
  readonly fillViewPort: boolean;
}

const inspStyles = withStyles(() => ({
  mainFillViewport: {
    'height': 'calc(100vh - 64px - 2.5em)',
    'padding': '8px',
    'width': '100%',
    'display': 'flex',
    'flex-direction': 'column' as 'column',
  },
  stretchCard: {
    'height': '100%',
    'display': 'flex',
    'flex-direction': 'column' as 'column',
  },
  stretchCardContent: {
    'overflow-y': 'auto',
    'flex-grow': 1,
  },
  mainScrollable: {
    'padding': '8px',
    'width': '100%',
  },
}));

function longText(): string {
  let ret: string = "";
  for (let i = 0; i < 300; i++) {
    ret += "Hello world ";
  }
  return ret;
}

export default inspStyles<InspectionProps>(props => {
  return (
    <main className={props.fillViewPort ? props.classes.mainFillViewport : props.classes.mainScrollable}>
      <Grid container style={{flexGrow: 1}}>
        <Grid item xs={12} md={6}>
          <Card className={props.fillViewPort ? props.classes.stretchCard : undefined}>
            <CardHeader title="Recent Inspections"/>
            <CardContent className={props.fillViewPort ? props.classes.stretchCardContent : undefined}>
              <p>{longText()}</p>
            </CardContent>
          </Card>
        </Grid>
        <Grid item xs={12} md={6}>
          <Card className={props.fillViewPort ? props.classes.stretchCard : undefined}>
            <CardHeader title="Selected Material"/>
            <CardContent className={props.fillViewPort ? props.classes.stretchCardContent : undefined}>
              <p>Selected</p>
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </main>
  );
});