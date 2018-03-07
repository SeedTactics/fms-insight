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
import Hidden from 'material-ui/Hidden';

import * as api from '../../data/api';
import * as routes from '../../data/routes';
import { Store } from '../../data/store';
import * as matDetails from '../../data/material-details';

import StationToolbar from './StationToolbar';
import LoadStation from './LoadStation';
import { ConnectedMaterialDialog } from './Material';
import { LoadStationData, selectLoadStationProps } from '../../data/load-station';

export type StationMonitorData =
  | { type: routes.SelectedStationType.LoadStation, data: LoadStationData }
  | { type: routes.SelectedStationType.Inspection }
  | { type: routes.SelectedStationType.Wash }
  ;

export interface StationMonitorProps {
  readonly monitor_data: StationMonitorData;
  // tslint:disable-next-line:no-any
  openMat: (m: Readonly<api.IInProcessMaterial>) => any;
}

function monitorElement(
    data: StationMonitorData,
    fillViewport: boolean,
    // tslint:disable-next-line:no-any
    openMat: (m: Readonly<api.IInProcessMaterial>) => any
  ): JSX.Element {
  switch (data.type) {
    case routes.SelectedStationType.LoadStation:
      return <LoadStation fillViewPort={fillViewport} {...data.data} openMat={openMat}/>;
    case routes.SelectedStationType.Inspection:
      return <p>Inspection</p>;
    case routes.SelectedStationType.Wash:
      return <p>Wash</p>;
  }
}

export function FillViewportMonitor(props: StationMonitorProps) {
  return (
    <main style={{'height': 'calc(100vh - 64px - 2.5em)', 'display': 'flex', 'flexDirection': 'column'}}>
      {monitorElement(props.monitor_data, true, props.openMat)}
    </main>
  );
}

export function ScrollableStationMonitor(props: StationMonitorProps) {
  return (
    <main style={{padding: '8px'}}>
      {monitorElement(props.monitor_data, false, props.openMat)}
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
      <ConnectedMaterialDialog/>
    </div>
  );
}

export const buildMonitorData = createSelector(
  (st: Store) => st.Current.current_status,
  (st: Store) => st.Route,
  (curStatus: Readonly<api.ICurrentStatus>, route: routes.State): StationMonitorData => {
    switch (route.selected_station_type) {
      case routes.SelectedStationType.LoadStation:
        return {
          type: routes.SelectedStationType.LoadStation,
          data: selectLoadStationProps(
            route.selected_station_id,
            route.station_queues,
            route.station_free_material,
            curStatus)
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
    monitor_data: buildMonitorData(st),
  }),
  {
    openMat: matDetails.openMaterialDialog,
  }
)(StationMonitor);