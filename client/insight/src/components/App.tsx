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
import List from "@material-ui/core/List";
import ListItem from "@material-ui/core/ListItem";
import ListItemIcon from "@material-ui/core/ListItemIcon";
import ListSubheader from "@material-ui/core/ListSubheader";
import ListItemText from "@material-ui/core/ListItemText";
import CircularProgress from "@material-ui/core/CircularProgress";
import Button from "@material-ui/core/Button";
import Badge from "@material-ui/core/Badge";
import Notifications from "@material-ui/icons/Notifications";
import Drawer from "@material-ui/core/Drawer";
import CameraAlt from "@material-ui/icons/CameraAlt";
import SearchIcon from "@material-ui/icons/Search";
import ShoppingBasket from "@material-ui/icons/ShoppingBasket";
import DirectionsIcon from "@material-ui/icons/Directions";
import StarIcon from "@material-ui/icons/StarRate";
import ChartIcon from "@material-ui/icons/InsertChart";
import ExtensionIcon from "@material-ui/icons/Extension";
import BugIcon from "@material-ui/icons/BugReport";
import InfoIcon from "@material-ui/icons/Info";
import OpacityIcon from "@material-ui/icons/Opacity";
import MemoryIcon from "@material-ui/icons/Memory";
import MoneyIcon from "@material-ui/icons/AttachMoney";
import MenuIcon from "@material-ui/icons/Menu";
import CheckIcon from "@material-ui/icons/CheckCircle";
import PersonIcon from "@material-ui/icons/Person";
import { User } from "oidc-client";

import OperationDashboard from "./operations/Dashboard";
import { OperationLoadUnload, OperationMachines } from "./operations/DailyStationOverview";
import CostPerPiece from "./analysis/CostPerPiece";
import Efficiency from "./analysis/Efficiency";
import StationToolbar from "./station-monitor/StationToolbar";
import DataExport from "./analysis/DataExport";
import ChooseMode from "./ChooseMode";
import LoadingIcon from "./LoadingIcon";
import * as routes from "../data/routes";
import { Store, connect, mkAC } from "../store/store";
import * as api from "../data/api";
import * as serverSettings from "../data/server-settings";
import * as guiState from "../data/gui-state";
import * as pathLookup from "../data/path-lookup";
import * as matDetails from "../data/material-details";
import logo from "../seedtactics-logo.svg";
import BackupViewer from "./BackupViewer";
import SerialScanner from "./QRScan";
import ManualScan from "./ManualScan";
import ChooseOperator from "./ChooseOperator";
import { BasicMaterialDialog } from "./station-monitor/Material";
import { CompletedParts } from "./operations/CompletedParts";
import AllMaterial from "./operations/AllMaterial";
import { FailedPartLookup } from "./quality/FailedPartLookup";
import { QualityPaths } from "./quality/QualityPaths";
import { QualityDashboard } from "./quality/RecentFailedInspections";
import LoadStation from "./station-monitor/LoadStation";
import Inspection from "./station-monitor/Inspection";
import Wash from "./station-monitor/Wash";
import Queues from "./station-monitor/Queues";

const tabsStyle = {
  alignSelf: "flex-end" as "flex-end",
  flexGrow: 1,
};

interface HeaderNavProps {
  readonly demo: boolean;
  readonly full: boolean;
  readonly setRoute: (arg: { ty: routes.RouteLocation; curSt: routes.State }) => void;
  readonly routeState: routes.State;
}

function OperationsTabs(p: HeaderNavProps) {
  if (p.demo) {
    return <div style={{ flexGrow: 1 }} />;
  }
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.current}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Operations" value={routes.RouteLocation.Operations_Dashboard} />
      <Tab label="Load/Unload" value={routes.RouteLocation.Operations_LoadStation} />
      <Tab label="Machines" value={routes.RouteLocation.Operations_Machines} />
      <Tab label="Completed Parts" value={routes.RouteLocation.Operations_CompletedParts} />
      <Tab label="Material" value={routes.RouteLocation.Operations_AllMaterial} />
    </Tabs>
  );
}

function QualityTabs(p: HeaderNavProps) {
  if (p.demo) {
    return <div style={{ flexGrow: 1 }} />;
  }
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.current}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Quality" value={routes.RouteLocation.Quality_Dashboard} />
      <Tab label="Failed Part Lookup" value={routes.RouteLocation.Quality_Serials} />
      <Tab label="Paths" value={routes.RouteLocation.Quality_Paths} />
      <Tab label="Quarantine Material" value={routes.RouteLocation.Quality_Quarantine} />
    </Tabs>
  );
}

function AnalysisTabs(p: HeaderNavProps) {
  if (p.demo) {
    return <div style={{ flexGrow: 1 }} />;
  }
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.current}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Efficiency" value={routes.RouteLocation.Analysis_Efficiency} />
      <Tab label="Cost/Piece" value={routes.RouteLocation.Analysis_CostPerPiece} />
      <Tab label="Data Export" value={routes.RouteLocation.Analysis_DataExport} />
    </Tabs>
  );
}

function DemoNav(p: HeaderNavProps) {
  return (
    <List component="nav">
      <ListSubheader>Shop Floor</ListSubheader>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Station_LoadMonitor}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Station_LoadMonitor, curSt: p.routeState })}
      >
        <ListItemIcon>
          <DirectionsIcon />
        </ListItemIcon>
        <ListItemText>Load Station</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Station_Queues}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Station_Queues, curSt: p.routeState })}
      >
        <ListItemIcon>
          <ExtensionIcon />
        </ListItemIcon>
        <ListItemText>Queues</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Station_InspectionMonitor}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Station_InspectionMonitor, curSt: p.routeState })}
      >
        <ListItemIcon>
          <InfoIcon />
        </ListItemIcon>
        <ListItemText>Inspection</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Station_WashMonitor}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Station_WashMonitor, curSt: p.routeState })}
      >
        <ListItemIcon>
          <OpacityIcon />
        </ListItemIcon>
        <ListItemText>Wash</ListItemText>
      </ListItem>
      <ListSubheader>Daily Monitoring</ListSubheader>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Operations_Dashboard}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Operations_Dashboard, curSt: p.routeState })}
      >
        <ListItemIcon>
          <ShoppingBasket />
        </ListItemIcon>
        <ListItemText>Operations</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Operations_LoadStation}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Operations_LoadStation, curSt: p.routeState })}
      >
        <ListItemIcon>
          <PersonIcon />
        </ListItemIcon>
        <ListItemText>Load Station</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Operations_Machines}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Operations_Machines, curSt: p.routeState })}
      >
        <ListItemIcon>
          <MemoryIcon />
        </ListItemIcon>
        <ListItemText>Engineering</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Operations_CompletedParts}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Operations_CompletedParts, curSt: p.routeState })}
      >
        <ListItemIcon>
          <CheckIcon />
        </ListItemIcon>
        <ListItemText>Scheduling</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Quality_Dashboard}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Quality_Dashboard, curSt: p.routeState })}
      >
        <ListItemIcon>
          <StarIcon />
        </ListItemIcon>
        <ListItemText>Quality</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Quality_Serials}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Quality_Serials, curSt: p.routeState })}
      >
        <ListItemIcon>
          <BugIcon />
        </ListItemIcon>
        <ListItemText>Part Lookup</ListItemText>
      </ListItem>
      <ListSubheader>Monthly Review</ListSubheader>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Analysis_Efficiency}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Analysis_Efficiency, curSt: p.routeState })}
      >
        <ListItemIcon>
          <ChartIcon />
        </ListItemIcon>
        <ListItemText>OEE Improvement</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Analysis_CostPerPiece}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Analysis_CostPerPiece, curSt: p.routeState })}
      >
        <ListItemIcon>
          <MoneyIcon />
        </ListItemIcon>
        <ListItemText>Cost Per Piece</ListItemText>
      </ListItem>
    </List>
  );
}

function BackupTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.current}
      onChange={(e, v) => p.setRoute({ ty: v, curSt: p.routeState })}
    >
      <Tab label="Efficiency" value={routes.RouteLocation.Analysis_Efficiency} />
      <Tab label="Failed Part Lookup" value={routes.RouteLocation.Quality_Serials} />
    </Tabs>
  );
}

function helpUrl(r: routes.RouteLocation): string {
  switch (r) {
    case routes.RouteLocation.ChooseMode:
      return "https://fms-insight.seedtactics.com/docs/client-launch.html";

    case routes.RouteLocation.Station_LoadMonitor:
    case routes.RouteLocation.Station_InspectionMonitor:
    case routes.RouteLocation.Station_WashMonitor:
    case routes.RouteLocation.Station_Queues:
      return "https://fms-insight.seedtactics.com/docs/client-station-monitor.html";

    case routes.RouteLocation.Operations_Dashboard:
    case routes.RouteLocation.Operations_LoadStation:
    case routes.RouteLocation.Operations_Machines:
    case routes.RouteLocation.Operations_AllMaterial:
    case routes.RouteLocation.Operations_CompletedParts:
      return "https://fms-insight.seedtactics.com/docs/client-operations.html";

    case routes.RouteLocation.Engineering:
      return "https://fms-insight.seedtactics.com/docs/client-engineering.html";

    case routes.RouteLocation.Quality_Dashboard:
    case routes.RouteLocation.Quality_Serials:
    case routes.RouteLocation.Quality_Paths:
    case routes.RouteLocation.Quality_Quarantine:
      return "https://fms-insight.seedtactics.com/docs/client-quality.html";

    case routes.RouteLocation.Tools_Dashboard:
      return "https://fms-insight.seedtactics.com/docs/client-launch.html";

    case routes.RouteLocation.Analysis_Efficiency:
    case routes.RouteLocation.Analysis_CostPerPiece:
    case routes.RouteLocation.Analysis_DataExport:
      return "https://fms-insight.seedtactics.com/docs/client-flexibility-analysis.html";
  }
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
  alarms: ReadonlyArray<string> | null;
  setRoute: (arg: { ty: routes.RouteLocation; curSt: routes.State }) => void;
  onLogout: () => void;
  readonly openQrCodeScan: () => void;
  readonly openManualSerial: () => void;
}

function Header(p: HeaderProps) {
  const [drawerOpen, setDrawerOpen] = React.useState(false);

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
      <IconButton aria-label="Help" href={helpUrl(p.routeState.current)} target="_help">
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
      ) : undefined}
      <Tooltip title="Enter Serial">
        <IconButton onClick={p.openManualSerial}>
          <SearchIcon />
        </IconButton>
      </Tooltip>
    </>
  );

  let tooltip: JSX.Element | string = "";
  if (p.fmsInfo) {
    tooltip = (p.fmsInfo.name || "") + " " + (p.fmsInfo.version || "");
  }

  return (
    <>
      <Hidden smDown>
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
              p.children({ full: false, setRoute: p.setRoute, demo: p.demo, routeState: p.routeState })
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
      </Hidden>
      <Hidden mdUp>
        <AppBar position="static">
          <Toolbar>
            {p.demo ? (
              <IconButton onClick={() => setDrawerOpen(!drawerOpen)}>
                <MenuIcon />
              </IconButton>
            ) : undefined}
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
          {p.children
            ? p.children({ full: true, setRoute: p.setRoute, demo: p.demo, routeState: p.routeState })
            : undefined}
        </AppBar>
        {p.demo ? (
          <Drawer variant="temporary" open={drawerOpen} onClose={() => setDrawerOpen(false)}>
            <DemoNav
              full={false}
              setRoute={(r) => {
                p.setRoute(r);
                setDrawerOpen(false);
              }}
              demo={true}
              routeState={p.routeState}
            />
          </Drawer>
        ) : undefined}
      </Hidden>
    </>
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
  backupFileOpened: boolean;
  alarms: ReadonlyArray<string> | null;
  setRoute: (arg: { ty: routes.RouteLocation; curSt: routes.State }) => void;
  onLogin: () => void;
  onLogout: () => void;
  readonly openQrCodeScan: () => void;
  readonly openManualSerial: () => void;
}

class App extends React.PureComponent<AppConnectedProps> {
  render() {
    let page: JSX.Element;
    let navigation: ((p: HeaderNavProps) => JSX.Element) | undefined = undefined;
    let showAlarms = true;
    const showLogout = !!this.props.user;
    let showSearch = true;
    let showOperator = false;
    let addBasicMaterialDialog = true;
    if (this.props.backupViewerOnRequestOpenFile) {
      if (this.props.backupFileOpened) {
        if (this.props.route.current === routes.RouteLocation.Quality_Serials) {
          page = <FailedPartLookup />;
          addBasicMaterialDialog = false;
        } else {
          page = <Efficiency allowSetType={false} />;
        }
        navigation = BackupTabs;
      } else {
        page = <BackupViewer onRequestOpenFile={this.props.backupViewerOnRequestOpenFile} />;
      }
      showAlarms = false;
    } else if (this.props.fmsInfo && (!serverSettings.requireLogin(this.props.fmsInfo) || this.props.user)) {
      switch (this.props.route.current) {
        case routes.RouteLocation.Station_LoadMonitor:
          page = <LoadStation />;
          navigation = (p) => <StationToolbar full={p.full} />;
          showOperator = true;
          addBasicMaterialDialog = false;
          break;
        case routes.RouteLocation.Station_InspectionMonitor:
          page = <Inspection />;
          navigation = (p) => <StationToolbar full={p.full} />;
          showOperator = true;
          addBasicMaterialDialog = false;
          break;
        case routes.RouteLocation.Station_WashMonitor:
          page = <Wash />;
          navigation = (p) => <StationToolbar full={p.full} />;
          showOperator = true;
          addBasicMaterialDialog = false;
          break;
        case routes.RouteLocation.Station_Queues:
          page = <Queues />;
          navigation = (p) => <StationToolbar full={p.full} />;
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
        case routes.RouteLocation.Operations_LoadStation:
          page = <OperationLoadUnload />;
          navigation = OperationsTabs;
          break;
        case routes.RouteLocation.Operations_Machines:
          page = <OperationMachines />;
          navigation = OperationsTabs;
          break;
        case routes.RouteLocation.Operations_AllMaterial:
          page = <AllMaterial displaySystemBins />;
          navigation = OperationsTabs;
          addBasicMaterialDialog = false;
          break;
        case routes.RouteLocation.Operations_CompletedParts:
          page = <CompletedParts />;
          navigation = OperationsTabs;
          break;

        case routes.RouteLocation.Engineering:
          page = <OperationMachines />;
          showAlarms = false;
          break;

        case routes.RouteLocation.Quality_Dashboard:
          page = <QualityDashboard />;
          navigation = QualityTabs;
          showAlarms = false;
          break;
        case routes.RouteLocation.Quality_Serials:
          page = <FailedPartLookup />;
          navigation = QualityTabs;
          addBasicMaterialDialog = false;
          showAlarms = false;
          break;
        case routes.RouteLocation.Quality_Paths:
          page = <QualityPaths />;
          navigation = QualityTabs;
          showAlarms = false;
          break;

        case routes.RouteLocation.Quality_Quarantine:
          page = <AllMaterial displaySystemBins={false} />;
          navigation = QualityTabs;
          showAlarms = false;
          addBasicMaterialDialog = false;
          break;

        case routes.RouteLocation.Tools_Dashboard:
          page = <p>Tools Dashboard</p>;
          break;

        case routes.RouteLocation.ChooseMode:
        default:
          if (this.props.demo) {
            page = <OperationDashboard />;
          } else {
            page = <ChooseMode />;
            showSearch = false;
            showAlarms = false;
          }
      }
    } else if (this.props.fmsInfo && serverSettings.requireLogin(this.props.fmsInfo)) {
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
    return (
      <div id="App">
        <Header
          routeState={this.props.route}
          fmsInfo={this.props.fmsInfo}
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
        {this.props.demo ? (
          <div style={{ display: "flex" }}>
            <Hidden smDown>
              <div style={{ borderRight: "1px solid" }}>
                <DemoNav full={false} setRoute={this.props.setRoute} demo={true} routeState={this.props.route} />
              </div>
            </Hidden>
            <div style={{ width: "100%" }}>{page}</div>
          </div>
        ) : (
          page
        )}
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
    backupFileOpened: s.Gui.backup_file_opened,
    alarms: emptyToNull(s.Current.current_status.alarms),
  }),
  {
    setRoute: ({ ty, curSt }: { ty: routes.RouteLocation; curSt: routes.State }) => [
      routes.displayPage(ty, curSt),
      { type: matDetails.ActionType.CloseMaterialDialog },
      { type: pathLookup.ActionType.Clear },
    ],
    onLogin: mkAC(serverSettings.ActionType.Login),
    onLogout: mkAC(serverSettings.ActionType.Logout),
    openQrCodeScan: () => ({
      type: guiState.ActionType.SetScanQrCodeDialog,
      open: true,
    }),
    openManualSerial: () => ({
      type: guiState.ActionType.SetManualSerialEntryDialog,
      open: true,
    }),
  }
)(App);
