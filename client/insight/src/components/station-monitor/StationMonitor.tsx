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
import Hidden from '@material-ui/core/Hidden';

import * as routes from '../../data/routes';

import StationToolbar from './StationToolbar';
import LoadStation from './LoadStation';
import Inspection from './Inspection';
import Wash from './Wash';
import Queues from './Queues';
import AllMaterial from './AllMaterial';

export interface StationMonitorProps {
  readonly monitor_type: routes.StationMonitorType;
}

function monitorElement(
    type: routes.StationMonitorType,
    fillViewport: boolean,
  ): JSX.Element {
  switch (type) {
    case routes.StationMonitorType.LoadUnload:
      return <LoadStation fillViewPort={fillViewport}/>;
    case routes.StationMonitorType.Inspection:
      return <Inspection/>;
    case routes.StationMonitorType.Wash:
      return <Wash/>;
    case routes.StationMonitorType.Queues:
      return <Queues/>;
    case routes.StationMonitorType.AllMaterial:
      return <AllMaterial/>;
  }
}

export default function StationMonitor(props: StationMonitorProps) {
  return (
    <div>
      <StationToolbar/>
      <Hidden mdDown>
        {monitorElement(props.monitor_type, true)}
      </Hidden>
      <Hidden lgUp>
        {monitorElement(props.monitor_type, false)}
      </Hidden>
    </div>
  );
}