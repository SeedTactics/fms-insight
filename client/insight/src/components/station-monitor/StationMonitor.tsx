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
import { createSelector } from 'reselect';
import * as im from 'immutable';
import Grid from 'material-ui/Grid';
import Card from 'material-ui/Card';
import Hidden from 'material-ui/Hidden';

import * as api from '../../data/api';
import * as routes from '../../data/routes';
import { Store } from '../../data/store';

import StationToolbar from './StationToolbar';
import LoadStation, { LoadStationData, selectLoadStationProps } from './LoadStation';

export type StationMonitorData =
  | { type: routes.SelectedStationType.LoadStation, data: LoadStationData }
  | { type: routes.SelectedStationType.Inspection }
  | { type: routes.SelectedStationType.Wash }
  ;

export interface StationMonitorProps {
  readonly material_in_queues: im.Map<string, im.Collection.Indexed<Readonly<api.IInProcessMaterial>>>;
  readonly monitor_data: StationMonitorData;
  readonly display_queues: boolean;
}

function monitorElement(data: StationMonitorData, fillViewport: boolean): JSX.Element {
  switch (data.type) {
    case routes.SelectedStationType.LoadStation:
      return <LoadStation fillViewPort={fillViewport} {...data.data}/>;
    case routes.SelectedStationType.Inspection:
      return <p>Inspection</p>;
    case routes.SelectedStationType.Wash:
      return <p>Wash</p>;
  }
}

export function FillViewportMonitor(props: StationMonitorProps) {
  if (props.display_queues) {
    return (
      <main style={{'height': 'calc(100vh - 64px - 2.5em)', 'display': 'flex'}}>
        <div style={{'flexBasis': '50%', 'padding': '8px', 'display': 'flex', 'flexDirection': 'column'}}>
          {monitorElement(props.monitor_data, true)}
        </div>
        <div style={{'flexBasis': '50%', 'padding': '8px', 'display': 'flex', 'flexDirection': 'column'}}>
          <Card style={{'flexGrow': 1, 'display': 'flex', 'flexDirection': 'column'}}>
            Queues
          </Card>
        </div>
      </main>
    );
  } else {
    return (
      <main style={{'height': 'calc(100vh - 64px - 2.5em)', 'display': 'flex'}}>
        <div style={{'width': '100%', 'padding': '8px', 'display': 'flex', 'flexDirection': 'column'}}>
          {monitorElement(props.monitor_data, true)}
        </div>
      </main>
    );
  }
}

export function ScrollableStationMonitor(props: StationMonitorProps) {
  let main: JSX.Element;
  if (props.display_queues) {
    main = (
      <Grid container>
        <Grid item xs={12} md={6}>
          {monitorElement(props.monitor_data, false)}
        </Grid>
        <Grid item xs={12} md={6}>
          <p>Queues</p>
        </Grid>
      </Grid>
    );
  } else {
    main = monitorElement(props.monitor_data, false);
  }

  return (
    <main style={{'padding': '8px'}}>
      {main}
    </main>
  );
}

export function StationMonitor(props: StationMonitorProps) {
  return (
    <div>
      <StationToolbar/>
      <Hidden mdDown>
        <FillViewportMonitor {...props}/>
      </Hidden>
      <Hidden lgUp>
        <ScrollableStationMonitor {...props}/>
      </Hidden>
    </div>
  );
}

export const findMaterialInQueues = createSelector(
  (st: Store) => st.Current.current_status.material,
  (st: Store) => st.Route,
  (material: ReadonlyArray<Readonly<api.IInProcessMaterial>>, route: routes.State) => {

    return im.Seq(material)
      .filter(m => m.location.type === api.LocType.InQueue)
      .groupBy(m => m.location.currentQueue || "")
      .toMap();
  }
);

export const buildMonitorData = createSelector(
  (st: Store) => st.Current.current_status,
  (st: Store) => st.Route,
  (curStatus: Readonly<api.ICurrentStatus>, route: routes.State): StationMonitorData => {
    switch (route.selected_station_type) {
      case routes.SelectedStationType.LoadStation:
        return {
          type: routes.SelectedStationType.LoadStation,
          data: selectLoadStationProps(route.selected_station_id, curStatus)
        };

      case routes.SelectedStationType.Wash:
        return {
          type: routes.SelectedStationType.Wash
        };

      case routes.SelectedStationType.Inspection:
        return {
          type: routes.SelectedStationType.Inspection
        };
    }

  }
);

export default connect(
  (st: Store) => ({
    material_in_queues: findMaterialInQueues(st),
    monitor_data: buildMonitorData(st),
    display_queues: st.Route.station_queues.length > 0,
  })
)(StationMonitor);