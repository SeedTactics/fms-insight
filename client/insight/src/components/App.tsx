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
import { AppBar } from "@material-ui/core";
import { Tabs } from "@material-ui/core";
import { Tab } from "@material-ui/core";
import { Typography } from "@material-ui/core";
import { Toolbar } from "@material-ui/core";
import { Hidden } from "@material-ui/core";
import HelpOutline from "@material-ui/icons/HelpOutline";
import ExitToApp from "@material-ui/icons/ExitToApp";
import { IconButton } from "@material-ui/core";
import { Tooltip } from "@material-ui/core";
import { CircularProgress } from "@material-ui/core";
import { Button } from "@material-ui/core";
import { Badge } from "@material-ui/core";
import Notifications from "@material-ui/icons/Notifications";
import { useRecoilValue, useRecoilValueLoadable } from "recoil";

import OperationDashboard from "./operations/Dashboard";
import { OperationLoadUnload, OperationMachines } from "./operations/DailyStationOverview";
import CostPerPiece from "./analysis/CostPerPiece";
import Efficiency from "./analysis/Efficiency";
import StationToolbar from "./station-monitor/StationToolbar";
import DataExport from "./analysis/DataExport";
import ChooseMode, { ChooseModeItem } from "./ChooseMode";
import LoadingIcon from "./LoadingIcon";
import * as routes from "../data/routes";
import * as api from "../data/api";
import * as serverSettings from "../data/server-settings";
import { SeedtacticLogo } from "../seedtactics-logo";
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
import { currentStatus } from "../cell-status/current-status";
import { BarcodeListener } from "../store/barcode";
import { ScheduleHistory } from "./analysis/ScheduleHistory";
import { differenceInDays, startOfToday } from "date-fns";
import { CustomStationMonitorDialog } from "./station-monitor/CustomStationMonitorDialog";

/* eslint-disable react/display-name */

const tabsStyle = {
  alignSelf: "flex-end" as const,
  flexGrow: 1,
};

export interface HeaderNavProps {
  readonly full: boolean;
  readonly setRoute: (r: routes.RouteState) => void;
  readonly routeState: routes.RouteState;
}

function OperationsTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.route}
      onChange={(e, v) => p.setRoute({ route: v as routes.RouteLocation } as routes.RouteState)}
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
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.route}
      onChange={(e, v) => p.setRoute({ route: v as routes.RouteLocation } as routes.RouteState)}
    >
      <Tab label="Quality" value={routes.RouteLocation.Quality_Dashboard} />
      <Tab label="Failed Part Lookup" value={routes.RouteLocation.Quality_Serials} />
      <Tab label="Paths" value={routes.RouteLocation.Quality_Paths} />
      <Tab label="Quarantine Material" value={routes.RouteLocation.Quality_Quarantine} />
    </Tabs>
  );
}

function ToolsTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.route}
      onChange={(e, v) => p.setRoute({ route: v as routes.RouteLocation } as routes.RouteState)}
    >
      <Tab label="Tools" value={routes.RouteLocation.Tools_Dashboard} />
      <Tab label="Programs" value={routes.RouteLocation.Tools_Programs} />
    </Tabs>
  );
}

function AnalysisTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.route}
      onChange={(e, v) => p.setRoute({ route: v as routes.RouteLocation } as routes.RouteState)}
    >
      <Tab label="Efficiency" value={routes.RouteLocation.Analysis_Efficiency} />
      <Tab label="Cost/Piece" value={routes.RouteLocation.Analysis_CostPerPiece} />
      <Tab label="Schedules" value={routes.RouteLocation.Analysis_Schedules} />
      <Tab label="Data Export" value={routes.RouteLocation.Analysis_DataExport} />
    </Tabs>
  );
}

function BackupTabs(p: HeaderNavProps) {
  return (
    <Tabs
      variant={p.full ? "fullWidth" : "standard"}
      style={p.full ? {} : tabsStyle}
      value={p.routeState.route}
      onChange={(e, v) => p.setRoute({ route: v as routes.RouteLocation } as routes.RouteState)}
    >
      <Tab label="Efficiency" value={routes.RouteLocation.Backup_Efficiency} />
      <Tab label="Part Lookup" value={routes.RouteLocation.Backup_PartLookup} />
      <Tab label="Schedules" value={routes.RouteLocation.Backup_Schedules} />
    </Tabs>
  );
}

function helpUrl(r: routes.RouteState): string {
  switch (r.route) {
    case routes.RouteLocation.Station_LoadMonitor:
    case routes.RouteLocation.Station_InspectionMonitor:
    case routes.RouteLocation.Station_InspectionMonitorWithType:
    case routes.RouteLocation.Station_WashMonitor:
    case routes.RouteLocation.Station_Queues:
      return "https://www.seedtactics.com/docs/fms-insight/client-station-monitor";

    case routes.RouteLocation.Operations_Dashboard:
    case routes.RouteLocation.Operations_LoadStation:
    case routes.RouteLocation.Operations_Machines:
    case routes.RouteLocation.Operations_AllMaterial:
    case routes.RouteLocation.Operations_CompletedParts:
      return "https://www.seedtactics.com/docs/fms-insight/client-operations";

    case routes.RouteLocation.Operations_Tools:
    case routes.RouteLocation.Operations_Programs:
      return "https://www.seedtactics.com/docs/fms-insight/client-tools-programs";

    case routes.RouteLocation.Engineering:
      return "https://www.seedtactics.com/docs/fms-insight/client-engineering";

    case routes.RouteLocation.Quality_Dashboard:
    case routes.RouteLocation.Quality_Serials:
    case routes.RouteLocation.Quality_Paths:
    case routes.RouteLocation.Quality_Quarantine:
    case routes.RouteLocation.Backup_PartLookup:
      return "https://www.seedtactics.com/docs/fms-insight/client-tools-programs";

    case routes.RouteLocation.Tools_Dashboard:
    case routes.RouteLocation.Tools_Programs:
      return "https://www.seedtactics.com/docs/fms-insight/client-operations";

    case routes.RouteLocation.Analysis_Efficiency:
    case routes.RouteLocation.Analysis_DataExport:
    case routes.RouteLocation.Backup_Efficiency:
      return "https://www.seedtactics.com/docs/fms-insight/client-flexibility-analysis";

    case routes.RouteLocation.Analysis_CostPerPiece:
      return "https://www.seedtactics.com/docs/fms-insight/client-cost-per-piece";

    case routes.RouteLocation.Backup_InitialOpen:
      return "https://www.seedtactics.com/docs/fms-insight/client-backup-viewer";

    case routes.RouteLocation.ChooseMode:
    case routes.RouteLocation.Client_Custom:
    default:
      return "https://www.seedtactics.com/docs/fms-insight/client-launch";
  }
}

function ShowLicense({ d }: { d: Date }) {
  const today = startOfToday();
  const diff = differenceInDays(d, today);

  if (diff > 7) {
    return null;
  } else if (diff >= 0) {
    return <Typography variant="subtitle1">License expires on {d.toDateString()}</Typography>;
  } else {
    return <Typography variant="h6">License expired on {d.toDateString()}</Typography>;
  }
}

interface HeaderProps {
  showAlarms: boolean;
  showLogout: boolean;
  showSearch: boolean;
  showOperator: boolean;
  fmsInfo: Readonly<api.IFMSInfo> | null;
  readonly setRoute: (r: routes.RouteState) => void;
  readonly routeState: routes.RouteState;

  children?: (p: HeaderNavProps) => React.ReactNode;
}

function Header(p: HeaderProps) {
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
      <IconButton aria-label="Help" href={helpUrl(p.routeState)} target="_help">
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
            <Tooltip title={tooltip}>
              <div>
                <SeedtacticLogo />
              </div>
            </Tooltip>
            <Typography variant="h6" style={{ marginLeft: "1em", marginRight: "2em" }}>
              Insight
            </Typography>
            {p.fmsInfo?.licenseExpires ? <ShowLicense d={p.fmsInfo.licenseExpires} /> : undefined}
            {p.children ? (
              p.children({ full: false, setRoute: p.setRoute, routeState: p.routeState })
            ) : (
              <div style={{ flexGrow: 1 }} />
            )}
            <LoadingIcon />
            {p.showOperator ? <OperatorSelect /> : undefined}
            {p.showOperator ? <CustomStationMonitorDialog /> : undefined}
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
            <Tooltip title={tooltip}>
              <div>
                <SeedtacticLogo />
              </div>
            </Tooltip>
            <Typography variant="h6" style={{ marginLeft: "4px" }}>
              Insight
            </Typography>
            {p.fmsInfo?.licenseExpires ? <ShowLicense d={p.fmsInfo.licenseExpires} /> : undefined}
            <div style={{ flexGrow: 1 }} />
            <LoadingIcon />
            {p.showOperator ? <CustomStationMonitorDialog /> : undefined}
            {p.showSearch ? <SearchButtons /> : undefined}
            <HelpButton />
            {p.showLogout ? <LogoutButton /> : undefined}
            {p.showAlarms ? <Alarms /> : undefined}
          </Toolbar>
          {p.children ? p.children({ full: true, setRoute: p.setRoute, routeState: p.routeState }) : undefined}
        </AppBar>
      </Hidden>
    </>
  );
}

export interface AppProps {
  readonly renderCustomPage?: (custom: ReadonlyArray<string>) => {
    readonly nav: (props: HeaderNavProps) => JSX.Element;
    readonly page: JSX.Element;
  };
  readonly chooseModes?: ReadonlyArray<ChooseModeItem>;
}

const App = React.memo(function App(props: AppProps) {
  const fmsInfoLoadable = useRecoilValueLoadable(serverSettings.fmsInformation);
  const [route, setRoute] = routes.useCurrentRoute();

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
    switch (route.route) {
      case routes.RouteLocation.Station_LoadMonitor:
        page = <LoadStation loadNum={route.loadNum} showFree={route.free} queues={route.queues} />;
        navigation = (p) => <StationToolbar full={p.full} />;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_InspectionMonitor:
        page = <Inspection focusInspectionType={null} />;
        navigation = (p) => <StationToolbar full={p.full} />;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_InspectionMonitorWithType:
        page = <Inspection focusInspectionType={route.inspType} />;
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
        page = <Queues showFree={route.free} queues={route.queues} />;
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
        page = <Efficiency />;
        navigation = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Schedules:
        page = <ScheduleHistory />;
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
        navigation = undefined;
        page = <BackupViewer />;
        showAlarms = false;
        break;
      case routes.RouteLocation.Backup_Efficiency:
        navigation = BackupTabs;
        page = <Efficiency />;
        showAlarms = false;
        break;
      case routes.RouteLocation.Backup_Schedules:
        navigation = BackupTabs;
        page = <ScheduleHistory />;
        showAlarms = false;
        break;
      case routes.RouteLocation.Backup_PartLookup:
        navigation = BackupTabs;
        page = <FailedPartLookup />;
        addBasicMaterialDialog = false;
        showAlarms = false;
        break;

      case routes.RouteLocation.Client_Custom: {
        const customPage = props.renderCustomPage?.(route.custom);
        navigation = customPage?.nav;
        page = customPage?.page ?? <ChooseMode setRoute={setRoute} modes={props.chooseModes} />;
        showAlarms = false;
        break;
      }

      case routes.RouteLocation.ChooseMode:
      default:
        page = <ChooseMode setRoute={setRoute} modes={props.chooseModes} />;
        showSearch = false;
        showAlarms = false;
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
        routeState={route}
        fmsInfo={fmsInfo}
        showAlarms={showAlarms}
        showSearch={showSearch}
        showLogout={showLogout}
        showOperator={showOperator}
        setRoute={setRoute}
      >
        {navigation}
      </Header>
      {page}
      {addBasicMaterialDialog ? <BasicMaterialDialog /> : undefined}
      <WebsocketConnection />
      <BarcodeListener />
    </div>
  );
});

export default App;
