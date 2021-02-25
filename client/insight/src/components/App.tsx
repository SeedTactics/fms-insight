/* Copyright (c) 2020, John Lenz

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
import ProgramIcon from "@material-ui/icons/Receipt";
import ToolIcon from "@material-ui/icons/Dns";
import { useRecoilCallback, useRecoilValue, useRecoilValueLoadable } from "recoil";

import OperationDashboard from "./operations/Dashboard";
import { OperationLoadUnload, OperationMachines } from "./operations/DailyStationOverview";
import CostPerPiece from "./analysis/CostPerPiece";
import Efficiency from "./analysis/Efficiency";
import StationToolbar from "./station-monitor/StationToolbar";
import DataExport from "./analysis/DataExport";
import ChooseMode from "./ChooseMode";
import LoadingIcon from "./LoadingIcon";
import * as routes from "../data/routes";
import { Store, connect } from "../store/store";
import * as api from "../data/api";
import * as serverSettings from "../data/server-settings";
import logo from "../seedtactics-logo.svg";
import BackupViewer from "./BackupViewer";
import { SerialScannerButton } from "./QRScan";
import { ManualScanButton } from "./ManualScan";
import { OperatorSelect } from "./ChooseOperator";
import { BasicMaterialDialog } from "./station-monitor/Material";
import { CompletedParts } from "./operations/CompletedParts";
import { AllMaterial } from "./operations/AllMaterial";
import { FailedPartLookup } from "./quality/FailedPartLookup";
import { QualityPaths } from "./quality/QualityPaths";
import { QualityDashboard } from "./quality/RecentFailedInspections";
import LoadStation from "./station-monitor/LoadStation";
import Inspection from "./station-monitor/Inspection";
import Wash from "./station-monitor/Wash";
import Queues from "./station-monitor/Queues";
import { ToolReportPage } from "./operations/ToolReport";
import { ProgramReportPage } from "./operations/Programs";
import { WebsocketConnection } from "../store/websocket";
import { currentStatus } from "../data/current-status";
import { JobsBackend } from "../data/backend";
import { BarcodeListener } from "../store/barcode";

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
      <Tab label="Schedules" value={routes.RouteLocation.Operations_CompletedParts} />
      <Tab label="Material" value={routes.RouteLocation.Operations_AllMaterial} />
      <Tab label="Tools" value={routes.RouteLocation.Operations_Tools} />
      <Tab label="Programs" value={routes.RouteLocation.Operations_Programs} />
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

function ToolsTabs(p: HeaderNavProps) {
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
      <Tab label="Tools" value={routes.RouteLocation.Tools_Dashboard} />
      <Tab label="Programs" value={routes.RouteLocation.Tools_Programs} />
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
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Operations_Tools}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Operations_Tools, curSt: p.routeState })}
      >
        <ListItemIcon>
          <ToolIcon />
        </ListItemIcon>
        <ListItemText>Tools</ListItemText>
      </ListItem>
      <ListItem
        button
        selected={p.routeState.current === routes.RouteLocation.Operations_Programs}
        onClick={() => p.setRoute({ ty: routes.RouteLocation.Operations_Programs, curSt: p.routeState })}
      >
        <ListItemIcon>
          <ProgramIcon />
        </ListItemIcon>
        <ListItemText>Programs</ListItemText>
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
      <Tab label="Efficiency" value={routes.RouteLocation.Backup_Efficiency} />
      <Tab label="Part Lookup" value={routes.RouteLocation.Backup_PartLookup} />
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

    case routes.RouteLocation.Operations_Tools:
    case routes.RouteLocation.Operations_Programs:
      return "https://fms-insight.seedtactics.com/docs/client-tools-programs.html";

    case routes.RouteLocation.Engineering:
      return "https://fms-insight.seedtactics.com/docs/client-engineering.html";

    case routes.RouteLocation.Quality_Dashboard:
    case routes.RouteLocation.Quality_Serials:
    case routes.RouteLocation.Quality_Paths:
    case routes.RouteLocation.Quality_Quarantine:
    case routes.RouteLocation.Backup_PartLookup:
      return "https://fms-insight.seedtactics.com/docs/client-tools-programs.html";

    case routes.RouteLocation.Tools_Dashboard:
    case routes.RouteLocation.Tools_Programs:
      return "https://fms-insight.seedtactics.com/docs/client-operations.html";

    case routes.RouteLocation.Analysis_Efficiency:
    case routes.RouteLocation.Analysis_DataExport:
    case routes.RouteLocation.Backup_Efficiency:
      return "https://fms-insight.seedtactics.com/docs/client-flexibility-analysis.html";

    case routes.RouteLocation.Analysis_CostPerPiece:
      return "https://fms-insight.seedtactics.com/docs/client-cost-per-piece.html";

    case routes.RouteLocation.Backup_InitialOpen:
      return "https://fms-insight.seedtactics.com/docs/client-backup-viewer.html";
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
  setRoute: (arg: { ty: routes.RouteLocation; curSt: routes.State }) => void;
}

function Header(p: HeaderProps) {
  const [drawerOpen, setDrawerOpen] = React.useState(false);
  const alarms = useRecoilValue(currentStatus).alarms;
  const hasAlarms = alarms && alarms.length > 0;

  const alarmTooltip = hasAlarms ? alarms.join(". ") : "No Alarms";
  const Alarms = () => (
    <Tooltip title={alarmTooltip}>
      <Badge badgeContent={hasAlarms ? alarms.length : 0}>
        <Notifications color={hasAlarms ? "error" : undefined} />
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
      <IconButton aria-label="Logout" onClick={serverSettings.logout}>
        <ExitToApp />
      </IconButton>
    </Tooltip>
  );

  const SearchButtons = () => (
    <>
      {window.location.protocol === "https:" || window.location.hostname === "localhost" ? (
        <SerialScannerButton />
      ) : undefined}
      <ManualScanButton />
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
            {p.showOperator ? <OperatorSelect /> : undefined}
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

function LoadDemoData() {
  const load = useRecoilCallback(({ set }) => () => {
    JobsBackend.currentStatus().then((st) => set(currentStatus, st));
  });
  React.useEffect(() => {
    load();
  }, []);

  return null;
}

export interface AppProps {
  demo: boolean;
  backupViewerOnRequestOpenFile?: () => void;
}

interface AppConnectedProps extends AppProps {
  route: routes.State;
  setRoute: (arg: { ty: routes.RouteLocation; curSt: routes.State }) => void;
}

const App = React.memo(function App(props: AppConnectedProps) {
  const fmsInfoLoadable = useRecoilValueLoadable(serverSettings.fmsInformation);

  // BaseLoadable<T>.valueMaybe() has the wrong type so hard to use :(
  const fmsInfo = fmsInfoLoadable.state === "hasValue" ? fmsInfoLoadable.valueOrThrow() : null;

  let page: JSX.Element;
  let navigation: ((p: HeaderNavProps) => JSX.Element) | undefined = undefined;
  let showAlarms = true;
  const showLogout = fmsInfo !== null && fmsInfo.user !== null && fmsInfo.user !== undefined;
  let showSearch = true;
  let showOperator = false;
  let addBasicMaterialDialog = true;
  if (fmsInfo && (!serverSettings.requireLogin(fmsInfo) || fmsInfo.user)) {
    switch (props.route.current) {
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
      case routes.RouteLocation.Operations_Tools:
        page = <ToolReportPage />;
        navigation = OperationsTabs;
        break;
      case routes.RouteLocation.Operations_Programs:
        page = <ProgramReportPage />;
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
        page = <ToolReportPage />;
        navigation = ToolsTabs;
        break;
      case routes.RouteLocation.Tools_Programs:
        page = <ProgramReportPage />;
        navigation = ToolsTabs;
        break;

      case routes.RouteLocation.Backup_InitialOpen:
        navigation = BackupTabs;
        page = <BackupViewer onRequestOpenFile={props.backupViewerOnRequestOpenFile} />;
        showAlarms = false;
        break;
      case routes.RouteLocation.Backup_Efficiency:
        navigation = BackupTabs;
        page = <Efficiency allowSetType={false} />;
        showAlarms = false;
        break;
      case routes.RouteLocation.Backup_PartLookup:
        navigation = BackupTabs;
        page = <FailedPartLookup />;
        addBasicMaterialDialog = false;
        showAlarms = false;
        break;

      case routes.RouteLocation.ChooseMode:
      default:
        if (props.demo) {
          page = <OperationDashboard />;
        } else {
          page = <ChooseMode />;
          showSearch = false;
          showAlarms = false;
        }
    }
  } else if (fmsInfo && serverSettings.requireLogin(fmsInfo)) {
    page = (
      <div style={{ textAlign: "center", marginTop: "4em" }}>
        <h3>Please Login</h3>
        <Button variant="contained" color="primary" onClick={() => serverSettings.login(fmsInfo)}>
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
        routeState={props.route}
        fmsInfo={fmsInfo}
        demo={props.demo}
        showAlarms={showAlarms}
        showSearch={showSearch}
        showLogout={showLogout}
        showOperator={showOperator}
        setRoute={props.setRoute}
      >
        {navigation}
      </Header>
      {props.demo ? (
        <div style={{ display: "flex" }}>
          <Hidden smDown>
            <div style={{ borderRight: "1px solid" }}>
              <DemoNav full={false} setRoute={props.setRoute} demo={true} routeState={props.route} />
            </div>
          </Hidden>
          <div style={{ width: "100%" }}>{page}</div>
        </div>
      ) : (
        page
      )}
      {addBasicMaterialDialog ? <BasicMaterialDialog /> : undefined}
      {props.demo ? <LoadDemoData /> : <WebsocketConnection />}
      <BarcodeListener />
    </div>
  );
});

export default connect(
  (s: Store) => ({
    route: s.Route,
  }),
  {
    setRoute: ({ ty, curSt }: { ty: routes.RouteLocation; curSt: routes.State }) => routes.displayPage(ty, curSt),
  }
)(App);
