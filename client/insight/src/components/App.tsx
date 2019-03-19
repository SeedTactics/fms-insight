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
import CameraAlt from "@material-ui/icons/CameraAlt";
import SearchIcon from "@material-ui/icons/Search";
import { User } from "oidc-client";

import OperationDashboard from "./operations/Dashboard";
import CostPerPiece from "./analysis/CostPerPiece";
import Efficiency from "./analysis/Efficiency";
import StationMonitor from "./station-monitor/StationMonitor";
import StationToolbar from "./station-monitor/StationToolbar";
import DataExport from "./analysis/DataExport";
import ChooseMode from "./ChooseMode";
import LoadingIcon from "./LoadingIcon";
import * as routes from "../data/routes";
import { Store, connect, mkAC } from "../store/store";
import * as api from "../data/api";
import * as serverSettings from "../data/server-settings";
import * as guiState from "../data/gui-state";
import logo from "../seedtactics-logo.svg";
import BackupViewer from "./BackupViewer";
import SerialScanner from "./QRScan";
import ManualScan from "./ManualScan";
import ChooseOperator from "./ChooseOperator";
import { BasicMaterialDialog } from "./station-monitor/Material";

const tabsStyle = {
  alignSelf: "flex-end" as "flex-end",
  flexGrow: 1
};

enum TabType {
  Operations_Dashboard,
  Operations_Cycles,
  Operations_Material,

  StationMontior,

  Quality_Dashboard,
  Quality_Serials,

  Analysis_Efficiency,
  Analysis_CostPerPiece,
  Analysis_DataExport
}

interface HeaderNavProps {
  readonly full: boolean;
  readonly setRoute: (arg: { ty: TabType; curSt: routes.State }) => void;
  readonly routeState: routes.State;
}

function routeToTabType(loc: routes.RouteLocation): TabType {
  switch (loc) {
    case routes.RouteLocation.Operations_Dashboard:
      return TabType.Operations_Dashboard;
    case routes.RouteLocation.Operations_Cycles:
      return TabType.Operations_Cycles;
    case routes.RouteLocation.Operations_AllMaterial:
      return TabType.Operations_Material;
    case routes.RouteLocation.Quality_Dashboard:
      return TabType.Quality_Dashboard;
    case routes.RouteLocation.Quality_Serials:
      return TabType.Quality_Serials;
    case routes.RouteLocation.Analysis_Efficiency:
      return TabType.Analysis_Efficiency;
    case routes.RouteLocation.Analysis_CostPerPiece:
      return TabType.Analysis_CostPerPiece;
    case routes.RouteLocation.Analysis_DataExport:
      return TabType.Analysis_DataExport;
    case routes.RouteLocation.Station_LoadMonitor:
    case routes.RouteLocation.Station_InspectionMonitor:
    case routes.RouteLocation.Station_Queues:
    case routes.RouteLocation.Station_WashMonitor:
      return TabType.StationMontior;
    default:
      return TabType.Operations_Dashboard;
  }
}

function DemoTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={routeToTabType(p.routeState.current)}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Operations" value={TabType.Operations_Dashboard} />
      <Tab label="Station Monitor" value={TabType.StationMontior} />
      <Tab label="Cycles" value={TabType.Operations_Cycles} />
      <Tab label="Quality" value={TabType.Quality_Dashboard} />
      <Tab label="Serials" value={TabType.Quality_Serials} />
      <Tab label="Efficiency" value={TabType.Analysis_Efficiency} />
      <Tab label="Cost/Piece" value={TabType.Analysis_CostPerPiece} />
    </Tabs>
  );
}

function OperationsTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={routeToTabType(p.routeState.current)}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Operations" value={TabType.Operations_Dashboard} />
      <Tab label="Cycles" value={TabType.Operations_Cycles} />
      <Tab label="Material" value={TabType.Operations_Material} />
    </Tabs>
  );
}

function QualityTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={routeToTabType(p.routeState.current)}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Quality" value={TabType.Quality_Dashboard} />
      <Tab label="Serials" value={TabType.Quality_Serials} />
    </Tabs>
  );
}

function AnalysisTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={routeToTabType(p.routeState.current)}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Efficiency" value={TabType.Analysis_Efficiency} />
      <Tab label="Cost/Piece" value={TabType.Analysis_CostPerPiece} />
      <Tab label="Data Export" value={TabType.Analysis_DataExport} />
    </Tabs>
  );
}

interface HeaderProps {
  demo: boolean;
  showAlarms: boolean;
  showLogout: boolean;
  showSearch: boolean;
  showOperator: boolean;
  children?: (p: HeaderNavProps) => React.ReactNode;

  routeState: routes.State;
  fmsInfo: Readonly<api.IFMSInfo> | null;
  latestVersion: serverSettings.LatestInstaller | null;
  alarms: ReadonlyArray<string> | null;
  setRoute: (arg: { ty: TabType; curSt: routes.State }) => void;
  onLogout: () => void;
  readonly openQrCodeScan: () => void;
  readonly openManualSerial: () => void;
}

function Header(p: HeaderProps) {
  let helpUrl = "https://fms-insight.seedtactics.com/docs/client-dashboard.html";
  switch (p.routeState.current) {
    case routes.RouteLocation.Station_LoadMonitor:
    case routes.RouteLocation.Station_InspectionMonitor:
    case routes.RouteLocation.Station_WashMonitor:
    case routes.RouteLocation.Station_Queues:
    case routes.RouteLocation.Operations_AllMaterial:
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

  const SearchButtons = () => (
    <>
      {window.location.protocol === "https:" || window.location.hostname === "localhost" ? (
        <Tooltip title="Scan QR Code">
          <IconButton onClick={p.openQrCodeScan}>
            <CameraAlt />
          </IconButton>
        </Tooltip>
      ) : (
        undefined
      )}
      <Tooltip title="Enter Serial">
        <IconButton onClick={p.openManualSerial}>
          <SearchIcon />
        </IconButton>
      </Tooltip>
    </>
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
        {p.children ? (
          p.children({ full: false, setRoute: p.setRoute, routeState: p.routeState })
        ) : (
          <div style={{ flexGrow: 1 }} />
        )}
        <LoadingIcon />
        {p.showOperator ? <ChooseOperator /> : undefined}
        {p.showSearch ? <SearchButtons /> : undefined}
        <HelpButton />
        {p.showLogout ? <LogoutButton /> : undefined}
        {p.showAlarms ? <Alarms /> : undefined}
      </Toolbar>
    </AppBar>
  );

  const smallAppBar = (
    <AppBar position="static">
      <Toolbar>
        <Tooltip title={tooltip}>
          <img src={logo} alt="Logo" style={{ height: "25px", marginRight: "4px" }} />
        </Tooltip>
        <Typography variant="h6">Insight</Typography>
        <div style={{ flexGrow: 1 }} />
        <LoadingIcon />
        {p.showSearch ? <SearchButtons /> : undefined}
        <HelpButton />
        {p.showLogout ? <LogoutButton /> : undefined}
        {p.showAlarms ? <Alarms /> : undefined}
      </Toolbar>
      {p.children ? p.children({ full: true, setRoute: p.setRoute, routeState: p.routeState }) : undefined}
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
  readonly openQrCodeScan: () => void;
  readonly openManualSerial: () => void;
}

class App extends React.PureComponent<AppConnectedProps> {
  render() {
    let page: JSX.Element;
    let navigation: ((p: HeaderNavProps) => JSX.Element) | undefined = undefined;
    let showAlarms: boolean = true;
    let showLogout: boolean = !!this.props.user;
    let showSearch: boolean = true;
    let showOperator: boolean = false;
    let addBasicMaterialDialog: boolean = true;
    if (this.props.backupViewerOnRequestOpenFile) {
      page = <BackupViewer onRequestOpenFile={this.props.backupViewerOnRequestOpenFile} />;
      showAlarms = false;
      showSearch = false;
    } else if (this.props.fmsInfo && (!this.props.fmsInfo.openIDConnectAuthority || this.props.user)) {
      switch (this.props.route.current) {
        case routes.RouteLocation.Station_LoadMonitor:
        case routes.RouteLocation.Station_InspectionMonitor:
        case routes.RouteLocation.Station_WashMonitor:
        case routes.RouteLocation.Station_Queues:
          page = <StationMonitor route_loc={this.props.route.current} showToolbar={this.props.demo} />;
          navigation = p => <StationToolbar full={p.full} allowChangeType={false} />;
          showOperator = true;
          addBasicMaterialDialog = false;
          break;

        case routes.RouteLocation.Analysis_CostPerPiece:
          page = <CostPerPiece />;
          navigation = AnalysisTabs;
          showAlarms = false;
          break;
        case routes.RouteLocation.Analysis_Efficiency:
          page = <Efficiency allowSetType={true} />;
          navigation = AnalysisTabs;
          showAlarms = false;
          break;
        case routes.RouteLocation.Analysis_DataExport:
          page = <DataExport />;
          navigation = AnalysisTabs;
          showAlarms = false;
          break;

        case routes.RouteLocation.Operations_Dashboard:
          page = <OperationDashboard />;
          navigation = OperationsTabs;
          break;
        case routes.RouteLocation.Operations_Cycles:
          page = <p>Operations Cycles</p>;
          navigation = OperationsTabs;
          break;
        case routes.RouteLocation.Operations_AllMaterial:
          page = <StationMonitor route_loc={this.props.route.current} showToolbar={this.props.demo} />;
          navigation = OperationsTabs;
          break;

        case routes.RouteLocation.Quality_Dashboard:
          page = <p>Quality Dashboard</p>;
          navigation = QualityTabs;
          showAlarms = false;
          break;
        case routes.RouteLocation.Quality_Serials:
          page = <p>Quality Serials</p>;
          navigation = QualityTabs;
          showAlarms = false;
          break;

        case routes.RouteLocation.Tools_Dashboard:
          page = <p>Tools Dashboard</p>;
          break;

        case routes.RouteLocation.ChooseMode:
        default:
          page = <ChooseMode />;
          showAlarms = false;
          showSearch = false;
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
      showAlarms = false;
      showSearch = false;
    } else {
      page = (
        <div style={{ textAlign: "center", marginTop: "4em" }}>
          <CircularProgress />
          <p>Loading</p>
        </div>
      );
      showAlarms = false;
      showSearch = false;
    }
    if (this.props.demo) {
      navigation = DemoTabs;
    }
    return (
      <div id="App">
        <Header
          routeState={this.props.route}
          fmsInfo={this.props.fmsInfo}
          latestVersion={this.props.latestVersion}
          demo={this.props.demo}
          showAlarms={showAlarms}
          showSearch={showSearch}
          showLogout={showLogout}
          showOperator={showOperator}
          setRoute={this.props.setRoute}
          onLogout={this.props.onLogout}
          alarms={this.props.alarms}
          openManualSerial={this.props.openManualSerial}
          openQrCodeScan={this.props.openQrCodeScan}
        >
          {navigation}
        </Header>
        {page}
        <SerialScanner />
        <ManualScan />
        {addBasicMaterialDialog ? <BasicMaterialDialog /> : undefined}
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
        case TabType.Operations_Material:
          return { type: routes.RouteLocation.Operations_AllMaterial };
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
        case TabType.StationMontior:
          return routes.displayLoadStation(curSt.selected_load_id, curSt.load_queues, curSt.load_free_material);
      }
    },
    onLogin: mkAC(serverSettings.ActionType.Login),
    onLogout: mkAC(serverSettings.ActionType.Logout),
    openQrCodeScan: () => ({
      type: guiState.ActionType.SetScanQrCodeDialog,
      open: true
    }),
    openManualSerial: () => ({
      type: guiState.ActionType.SetManualSerialEntryDialog,
      open: true
    })
  }
)(App);
