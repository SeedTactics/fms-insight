/* Copyright (c) 2019, John Lenz

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
import AppBar from "@material-ui/core/AppBar";
import Tabs from "@material-ui/core/Tabs";
import Tab from "@material-ui/core/Tab";
import Typography from "@material-ui/core/Typography";
import Toolbar from "@material-ui/core/Toolbar";
import Hidden from "@material-ui/core/Hidden";
import HelpOutline from "@material-ui/icons/HelpOutline";
import ExitToApp from "@material-ui/icons/ExitToApp";
import IconButton from "@material-ui/core/IconButton";
import Tooltip from "@material-ui/core/Tooltip";
import CircularProgress from "@material-ui/core/CircularProgress";
import Button from "@material-ui/core/Button";
import Badge from "@material-ui/core/Badge";
import Notifications from "@material-ui/icons/Notifications";
import { User } from "oidc-client";

import Dashboard from "./dashboard/Dashboard";
import CostPerPiece from "./cost-per-piece/CostPerPiece";
import Efficiency from "./efficiency/Efficiency";
import StationMonitor from "./station-monitor/StationMonitor";
import DataExport from "./data-export/DataExport";
import ChooseMode from "./ChooseMode";
import LoadingIcon from "./LoadingIcon";
import * as routes from "../data/routes";
import { Store, connect, mkAC } from "../store/store";
import * as api from "../data/api";
import * as serverSettings from "../data/server-settings";
import logo from "../seedtactics-logo.svg";
import BackupViewer from "./BackupViewer";

const tabsStyle = {
  alignSelf: "flex-end" as "flex-end",
  flexGrow: 1
};

enum TabType {
  Operations_Dashboard,
  Operations_Cycles,

  Quality_Dashboard,
  Quality_Serials,

  Analysis_Efficiency,
  Analysis_CostPerPiece,
  Analysis_DataExport
}

interface HeaderTabsProps {
  readonly demo: boolean;
  readonly full: boolean;
  readonly setRoute: (arg: { ty: TabType; curSt: routes.State }) => void;
  readonly routeState: routes.State;
}

function HeaderTabs(p: HeaderTabsProps) {
  let tabType: TabType = TabType.Operations_Dashboard;
  const loc = p.routeState.current;

  switch (loc) {
    case routes.RouteLocation.Operations_Dashboard:
      tabType = TabType.Operations_Dashboard;
      break;
    case routes.RouteLocation.Operations_Cycles:
      tabType = TabType.Operations_Cycles;
      break;
    case routes.RouteLocation.Quality_Dashboard:
      tabType = TabType.Quality_Dashboard;
      break;
    case routes.RouteLocation.Quality_Serials:
      tabType = TabType.Quality_Serials;
      break;
    case routes.RouteLocation.Analysis_Efficiency:
      tabType = TabType.Analysis_Efficiency;
      break;
    case routes.RouteLocation.Analysis_CostPerPiece:
      tabType = TabType.Analysis_CostPerPiece;
      break;
    case routes.RouteLocation.Analysis_DataExport:
      tabType = TabType.Analysis_DataExport;
      break;
  }

  let tabs: JSX.Element[] = [];
  if (p.demo || loc === routes.RouteLocation.Operations_Dashboard || loc === routes.RouteLocation.Operations_Cycles) {
    tabs.push(<Tab label="Operations" value={TabType.Operations_Dashboard} />);
    tabs.push(<Tab label="Cycles" value={TabType.Operations_Cycles} />);
  }
  if (p.demo || loc === routes.RouteLocation.Quality_Dashboard || loc === routes.RouteLocation.Quality_Serials) {
    tabs.push(<Tab label="Quality" value={TabType.Quality_Dashboard} />);
    tabs.push(<Tab label="Serials" value={TabType.Quality_Serials} />);
  }
  if (
    p.demo ||
    loc === routes.RouteLocation.Analysis_Efficiency ||
    loc === routes.RouteLocation.Analysis_CostPerPiece ||
    loc === routes.RouteLocation.Analysis_DataExport
  ) {
    tabs.push(<Tab label="Efficiency" value={TabType.Analysis_Efficiency} />);
    tabs.push(<Tab label="Cost/Piece" value={TabType.Analysis_CostPerPiece} />);
  }
  if (
    loc === routes.RouteLocation.Analysis_Efficiency ||
    loc === routes.RouteLocation.Analysis_CostPerPiece ||
    loc === routes.RouteLocation.Analysis_DataExport
  ) {
    tabs.push(<Tab label="Data Export" value={TabType.Analysis_DataExport} />);
  }

  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={tabType}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      {tabs}
    </Tabs>
  );
}

interface HeaderProps {
  demo: boolean;
  showTabs: boolean;
  showAlarms: boolean;
  showLogout: boolean;

  routeState: routes.State;
  fmsInfo: Readonly<api.IFMSInfo> | null;
  latestVersion: serverSettings.LatestInstaller | null;
  alarms: ReadonlyArray<string> | null;
  setRoute: (arg: { ty: TabType; curSt: routes.State }) => void;
  onLogout: () => void;
}

function Header(p: HeaderProps) {
  let helpUrl = "https://fms-insight.seedtactics.com/docs/client-dashboard.html";
  switch (p.routeState.current) {
    case routes.RouteLocation.Station_LoadMonitor:
    case routes.RouteLocation.Station_InspectionMonitor:
    case routes.RouteLocation.Station_WashMonitor:
    case routes.RouteLocation.Station_Queues:
    case routes.RouteLocation.Station_AllMaterial:
      helpUrl = "https://fms-insight.seedtactics.com/docs/client-station-monitor.html";
      break;
    case routes.RouteLocation.Analysis_Efficiency:
      helpUrl = "https://fms-insight.seedtactics.com/docs/client-efficiency.html";
      break;
    case routes.RouteLocation.Analysis_CostPerPiece:
      helpUrl = "https://fms-insight.seedtactics.com/docs/client-cost-per-piece.html";
      break;
  }

  const alarmTooltip = p.alarms ? p.alarms.join(". ") : "No Alarms";
  const Alarms = () => (
    <Tooltip title={alarmTooltip}>
      <Badge badgeContent={p.alarms ? p.alarms.length : 0}>
        <Notifications color={p.alarms ? "error" : undefined} />
      </Badge>
    </Tooltip>
  );

  const HelpButton = () => (
    <Tooltip title="Help">
      <IconButton aria-label="Help" href={helpUrl} target="_help">
        <HelpOutline />
      </IconButton>
    </Tooltip>
  );

  const LogoutButton = () => (
    <Tooltip title="Logout">
      <IconButton aria-label="Logout" onClick={p.onLogout}>
        <ExitToApp />
      </IconButton>
    </Tooltip>
  );

  let tooltip: JSX.Element | string = "";
  if (p.fmsInfo && p.latestVersion) {
    tooltip = (
      <div>
        {(p.fmsInfo.name || "") + " " + (p.fmsInfo.version || "")}
        <br />
        Latest version {p.latestVersion.version} ({p.latestVersion.date.toDateString()})
      </div>
    );
  } else if (p.fmsInfo) {
    tooltip = (p.fmsInfo.name || "") + " " + (p.fmsInfo.version || "");
  }

  const largeAppBar = (
    <AppBar position="static">
      <Toolbar>
        {p.demo ? (
          <a href="/">
            <img src={logo} alt="Logo" style={{ height: "30px", marginRight: "1em" }} />
          </a>
        ) : (
          <Tooltip title={tooltip}>
            <img src={logo} alt="Logo" style={{ height: "30px", marginRight: "1em" }} />
          </Tooltip>
        )}
        <Typography variant="h6" style={{ marginRight: "2em" }}>
          Insight
        </Typography>
        {p.showTabs ? (
          <HeaderTabs demo={p.demo} full={false} setRoute={p.setRoute} routeState={p.routeState} />
        ) : (
          <div style={{ flexGrow: 1 }} />
        )}
        <LoadingIcon />
        {p.showAlarms ? <Alarms /> : undefined}
        <HelpButton />
        {p.showLogout ? <LogoutButton /> : undefined}
      </Toolbar>
    </AppBar>
  );

  const smallAppBar = (
    <AppBar position="static">
      <Toolbar>
        <Tooltip title={tooltip}>
          <img src="/seedtactics-logo.svg" alt="Logo" style={{ height: "25px", marginRight: "4px" }} />
        </Tooltip>
        <Typography variant="h6">Insight</Typography>
        <div style={{ flexGrow: 1 }} />
        <LoadingIcon />
        {p.showAlarms ? <Alarms /> : undefined}
        <HelpButton />
        {p.showLogout ? <LogoutButton /> : undefined}
      </Toolbar>
      {p.showTabs ? (
        <HeaderTabs demo={p.demo} full={true} setRoute={p.setRoute} routeState={p.routeState} />
      ) : (
        undefined
      )}
    </AppBar>
  );

  return (
    <nav id="navHeader">
      <Hidden smDown>{largeAppBar}</Hidden>
      <Hidden mdUp>{smallAppBar}</Hidden>
    </nav>
  );
}

export interface AppProps {
  demo: boolean;
  backupViewerOnRequestOpenFile?: () => void;
}

interface AppConnectedProps extends AppProps {
  route: routes.State;
  fmsInfo: Readonly<api.IFMSInfo> | null;
  user: User | null;
  latestVersion: serverSettings.LatestInstaller | null;
  alarms: ReadonlyArray<string> | null;
  setRoute: (arg: { ty: TabType; curSt: routes.State }) => void;
  onLogin: () => void;
  onLogout: () => void;
}

class App extends React.PureComponent<AppConnectedProps> {
  render() {
    let page: JSX.Element;
    let showTabs: boolean = true;
    let showAlarms: boolean = true;
    let showLogout: boolean = !!this.props.user;
    if (this.props.backupViewerOnRequestOpenFile) {
      page = <BackupViewer onRequestOpenFile={this.props.backupViewerOnRequestOpenFile} />;
      showTabs = false;
      showAlarms = false;
    } else if (this.props.fmsInfo && (!this.props.fmsInfo.openIDConnectAuthority || this.props.user)) {
      switch (this.props.route.current) {
        case routes.RouteLocation.Station_LoadMonitor:
          page = <StationMonitor monitor_type={routes.StationMonitorType.LoadUnload} />;
          break;
        case routes.RouteLocation.Station_InspectionMonitor:
          page = <StationMonitor monitor_type={routes.StationMonitorType.Inspection} />;
          break;
        case routes.RouteLocation.Station_WashMonitor:
          page = <StationMonitor monitor_type={routes.StationMonitorType.Wash} />;
          break;
        case routes.RouteLocation.Station_Queues:
          page = <StationMonitor monitor_type={routes.StationMonitorType.Queues} />;
          break;
        case routes.RouteLocation.Station_AllMaterial:
          page = <StationMonitor monitor_type={routes.StationMonitorType.AllMaterial} />;
          break;

        case routes.RouteLocation.Analysis_CostPerPiece:
          page = <CostPerPiece />;
          showAlarms = false;
          break;
        case routes.RouteLocation.Analysis_Efficiency:
          page = <Efficiency allowSetType={true} />;
          showAlarms = false;
          break;
        case routes.RouteLocation.Analysis_DataExport:
          page = <DataExport />;
          showAlarms = false;
          break;

        case routes.RouteLocation.Operations_Dashboard:
          page = <Dashboard />;
          break;
        case routes.RouteLocation.Operations_Cycles:
          page = <p>Operations Cycles</p>;
          break;

        case routes.RouteLocation.Quality_Dashboard:
          page = <p>Quality Dashboard</p>;
          showAlarms = false;
          break;
        case routes.RouteLocation.Quality_Serials:
          page = <p>Quality Serials</p>;
          showAlarms = false;
          break;

        case routes.RouteLocation.Tools_Dashboard:
          page = <p>Tools Dashboard</p>;
          break;

        case routes.RouteLocation.ChooseMode:
        default:
          page = <ChooseMode />;
          showAlarms = false;
      }
    } else if (this.props.fmsInfo && this.props.fmsInfo.openIDConnectAuthority) {
      page = (
        <div style={{ textAlign: "center", marginTop: "4em" }}>
          <h3>Please Login</h3>
          <Button variant="contained" color="primary" onClick={this.props.onLogin}>
            Login
          </Button>
        </div>
      );
      showTabs = false;
    } else {
      page = (
        <div style={{ textAlign: "center", marginTop: "4em" }}>
          <CircularProgress />
          <p>Loading</p>
        </div>
      );
      showTabs = false;
    }
    return (
      <div id="App">
        <Header
          routeState={this.props.route}
          fmsInfo={this.props.fmsInfo}
          latestVersion={this.props.latestVersion}
          demo={this.props.demo}
          showTabs={showTabs}
          showAlarms={showAlarms}
          showLogout={showLogout}
          setRoute={this.props.setRoute}
          onLogout={this.props.onLogout}
          alarms={this.props.alarms}
        />
        {page}
      </div>
    );
  }
}

function emptyToNull<T>(s: ReadonlyArray<T> | null): ReadonlyArray<T> | null {
  if (!s || s.length === 0) {
    return null;
  } else {
    return s;
  }
}

export default connect(
  (s: Store) => ({
    route: s.Route,
    fmsInfo: s.ServerSettings.fmsInfo || null,
    user: s.ServerSettings.user || null,
    latestVersion: s.ServerSettings.latestInstaller || null,
    alarms: emptyToNull(s.Current.current_status.alarms)
  }),
  {
    setRoute: ({ ty, curSt }: { ty: TabType; curSt: routes.State }): routes.Action => {
      switch (ty) {
        case TabType.Operations_Dashboard:
          return { type: routes.RouteLocation.Operations_Dashboard };
        case TabType.Operations_Cycles:
          return { type: routes.RouteLocation.Operations_Cycles };
        case TabType.Quality_Dashboard:
          return { type: routes.RouteLocation.Quality_Dashboard };
        case TabType.Quality_Serials:
          return { type: routes.RouteLocation.Quality_Serials };
        case TabType.Analysis_Efficiency:
          return { type: routes.RouteLocation.Analysis_Efficiency };
        case TabType.Analysis_CostPerPiece:
          return { type: routes.RouteLocation.Analysis_CostPerPiece };
        case TabType.Analysis_DataExport:
          return { type: routes.RouteLocation.Analysis_DataExport };
      }
    },
    onLogin: mkAC(serverSettings.ActionType.Login),
    onLogout: mkAC(serverSettings.ActionType.Logout)
  }
)(App);
