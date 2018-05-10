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
import AppBar from 'material-ui/AppBar/AppBar';
import Tabs, { Tab } from 'material-ui/Tabs';
import Typography from 'material-ui/Typography/Typography';
import Toolbar from 'material-ui/Toolbar/Toolbar';
import Hidden from 'material-ui/Hidden/Hidden';

import Dashboard from './components/dashboard/Dashboard';
import CostPerPiece from './components/cost-per-piece/CostPerPiece';
import Efficiency from './components/efficiency/Efficiency';
import StationMonitor from './components/station-monitor/StationMonitor';
import DataExport from './components/data-export/DataExport';
import LoadingIcon from './components/LoadingIcon';
import * as routes from './data/routes';
import { Store, connect } from './store/store';

const tabsStyle = {
  'alignSelf': 'flex-end' as 'flex-end',
  'flexGrow': 1
};

enum TabType {
  Dashboard,
  StationMonitor,
  Efficiency,
  CostPerPiece,
  DataExport,
}

interface HeaderProps {
  routeState: routes.State;
  setRoute: (arg: {ty: TabType, curSt: routes.State}) => void;
}

function Header(p: HeaderProps) {
    let tabType: TabType = TabType.Dashboard;
    switch (p.routeState.current) {
      case routes.RouteLocation.Dashboard:
        tabType = TabType.Dashboard;
        break;
      case routes.RouteLocation.LoadMonitor:
      case routes.RouteLocation.InspectionMonitor:
      case routes.RouteLocation.WashMonitor:
      case routes.RouteLocation.Queues:
      case routes.RouteLocation.AllMaterial:
        tabType = TabType.StationMonitor;
        break;
      case routes.RouteLocation.Efficiency:
        tabType = TabType.Efficiency;
        break;
      case routes.RouteLocation.CostPerPiece:
        tabType = TabType.CostPerPiece;
        break;
      case routes.RouteLocation.DataExport:
        tabType = TabType.DataExport;
    }
    const tabs = (full: boolean) => (
      <Tabs
        fullWidth={full}
        style={full ? {} : tabsStyle}
        value={tabType}
        onChange={(e, v) => p.setRoute({ty: v, curSt: p.routeState})}
      >
        <Tab label="Dashboard" value={TabType.Dashboard}/>
        <Tab label="Station Monitor" value={TabType.StationMonitor}/>
        <Tab label="Efficiency" value={TabType.Efficiency}/>
        <Tab label="Cost/Piece" value={TabType.CostPerPiece}/>
        <Tab label="Data Export" value={TabType.DataExport}/>
      </Tabs>
    );

    const largeAppBar = (
      <AppBar position="static">
        <Toolbar>
          <img
            src="/seedtactics-logo.svg"
            alt="Logo"
            style={{height: '30px', marginRight: '1em'}}
          />
          <Typography variant="title" style={{'marginRight': '2em'}}>Insight</Typography>
          {tabs(false)}
          <LoadingIcon/>
        </Toolbar>
      </AppBar>
    );

    const smallAppBar = (
      <AppBar position="static">
        <Toolbar>
          <img
            src="/seedtactics-logo.svg"
            alt="Logo"
            style={{height: '25px', marginRight: '4px'}}
          />
          <Typography variant="title">Insight</Typography>
          <div style={{'flexGrow': 1}}/>
          <LoadingIcon/>
        </Toolbar>
        {tabs(true)}
      </AppBar>
    );

    return (
      <nav id="navHeader">
        <Hidden smDown>
          {largeAppBar}
        </Hidden>
        <Hidden mdUp>
          {smallAppBar}
        </Hidden>
      </nav>
    );
}

export interface AppProps {
  route: routes.State;
  setRoute: (arg: {ty: TabType, curSt: routes.State}) => void;
}

export class App extends React.PureComponent<AppProps> {
  render() {
    let page: JSX.Element;
    switch (this.props.route.current) {
      case routes.RouteLocation.CostPerPiece:
        page = <CostPerPiece/>;
        break;
      case routes.RouteLocation.Efficiency:
        page = <Efficiency/>;
        break;
      case routes.RouteLocation.LoadMonitor:
        page = <StationMonitor monitor_type={routes.StationMonitorType.LoadUnload}/>;
        break;
      case routes.RouteLocation.InspectionMonitor:
        page = <StationMonitor monitor_type={routes.StationMonitorType.Inspection}/>;
        break;
      case routes.RouteLocation.WashMonitor:
        page = <StationMonitor monitor_type={routes.StationMonitorType.Wash}/>;
        break;
      case routes.RouteLocation.Queues:
        page = <StationMonitor monitor_type={routes.StationMonitorType.Queues}/>;
        break;
      case routes.RouteLocation.AllMaterial:
        page = <StationMonitor monitor_type={routes.StationMonitorType.AllMaterial}/>;
        break;
      case routes.RouteLocation.DataExport:
        page = <DataExport/>;
        break;
      case routes.RouteLocation.Dashboard:
      default:
        page = <Dashboard/>;
        break;
    }
    return (
      <div id="App">
        <Header routeState={this.props.route} setRoute={this.props.setRoute}/>
        {page}
      </div>
    );
  }
}

export default connect(
  (s: Store) => ({
    route: s.Route
  }),
  {
    setRoute: ({ty, curSt}: {ty: TabType, curSt: routes.State}): routes.Action => {
      switch (ty) {
        case TabType.Dashboard:
          return { type: routes.RouteLocation.Dashboard };
        case TabType.Efficiency:
          return { type: routes.RouteLocation.Efficiency };
        case TabType.CostPerPiece:
          return { type: routes.RouteLocation.CostPerPiece };
        case TabType.StationMonitor:
          return routes.switchToStationMonitorPage(curSt);
        case TabType.DataExport:
          return { type: routes.RouteLocation.DataExport };
      }
    }
  }
)(App);