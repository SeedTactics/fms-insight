/* Copyright (c) 2023, John Lenz

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
import { AppBar, Box, useMediaQuery, useTheme } from "@mui/material";
import { Tabs } from "@mui/material";
import { Tab } from "@mui/material";
import { Typography } from "@mui/material";
import { Toolbar } from "@mui/material";
import { IconButton } from "@mui/material";
import { Tooltip } from "@mui/material";
import { CircularProgress } from "@mui/material";
import { Button } from "@mui/material";
import { Badge } from "@mui/material";
import { useRecoilValue, useRecoilValueLoadable } from "recoil";

import { Notifications, HelpOutline, ExitToApp } from "@mui/icons-material";

import OperationDashboard from "./operations/Dashboard.js";
import { OperationLoadUnload, OperationMachines } from "./operations/DailyStationOverview.js";
import CostPerPiece from "./analysis/CostPerPiece.js";
import Efficiency from "./analysis/EfficiencyPage.js";
import DataExport from "./analysis/DataExport.js";
import ChooseMode, { ChooseModeItem } from "./ChooseMode.js";
import { LoadingIcon } from "./LoadingIcon.js";
import * as routes from "./routes.js";
import * as serverSettings from "../network/server-settings.js";
import { SeedtacticLogo } from "../seedtactics-logo.js";
import { BackupViewer } from "./BackupViewer.js";
import { BarcodeListener, SerialScannerButton } from "./BarcodeScanning.js";
import { ManualScanButton } from "./ManualScan.js";
import { OperatorSelect } from "./ChooseOperator.js";
import { MaterialDialog } from "./station-monitor/Material.js";
import { RecentSchedulesPage } from "./operations/RecentSchedules.js";
import { AllMaterial } from "./operations/AllMaterial.js";
import { FailedPartLookup } from "./quality/FailedPartLookup.js";
import { QualityPaths } from "./quality/QualityPaths.js";
import { QualityDashboard } from "./quality/RecentFailedInspections.js";
import LoadStation from "./station-monitor/LoadStation.js";
import Inspection from "./station-monitor/Inspection.js";
import Wash from "./station-monitor/Wash.js";
import Queues from "./station-monitor/Queues.js";
import { ToolReportPage } from "./operations/ToolReport.js";
import { ProgramReportPage } from "./operations/Programs.js";
import { WebsocketConnection } from "../network/websocket.js";
import { currentStatus } from "../cell-status/current-status.js";
import { ScheduleHistory } from "./analysis/ScheduleHistory.js";
import { differenceInDays, startOfToday } from "date-fns";
import { CustomStationMonitorDialog } from "./station-monitor/CustomStationMonitorDialog.js";
import { AnalysisCyclePage } from "./analysis/AnalysisCyclesPage.js";
import { QualityPage } from "./analysis/QualityPage.js";
import { SystemOverviewPage } from "./station-monitor/SystemOverview.js";
import { StationToolbar, StationToolbarOverviewButton } from "./station-monitor/StationToolbar.js";

export function NavTabs({ children }: { children?: React.ReactNode }) {
  const [route, setRoute] = routes.useCurrentRoute();
  const theme = useTheme();
  const full = useMediaQuery(theme.breakpoints.down("md"));

  return (
    <Tabs
      variant={full && (!Array.isArray(children) || children.length < 5) ? "fullWidth" : "scrollable"}
      value={route.route}
      onChange={(e, v) => setRoute({ route: v as routes.RouteLocation } as routes.RouteState)}
      textColor="inherit"
      scrollButtons
      allowScrollButtonsMobile
      indicatorColor="secondary"
    >
      {children}
    </Tabs>
  );
}

function OperationsTabs() {
  return (
    <NavTabs>
      <Tab label="Operations" value={routes.RouteLocation.Operations_Dashboard} />
      <Tab label="Load/Unload" value={routes.RouteLocation.Operations_LoadStation} />
      <Tab label="Machines" value={routes.RouteLocation.Operations_Machines} />
      <Tab label="Schedules" value={routes.RouteLocation.Operations_RecentSchedules} />
      <Tab label="Cell" value={routes.RouteLocation.Operations_SystemOverview} />
      <Tab label="Material" value={routes.RouteLocation.Operations_AllMaterial} />
      <Tab label="Tools" value={routes.RouteLocation.Operations_Tools} />
      <Tab label="Programs" value={routes.RouteLocation.Operations_Programs} />
    </NavTabs>
  );
}

function QualityTabs() {
  return (
    <NavTabs>
      <Tab label="Quality" value={routes.RouteLocation.Quality_Dashboard} />
      <Tab label="Failed Part Lookup" value={routes.RouteLocation.Quality_Serials} />
      <Tab label="Paths" value={routes.RouteLocation.Quality_Paths} />
      <Tab label="Quarantine Material" value={routes.RouteLocation.Quality_Quarantine} />
    </NavTabs>
  );
}

function ToolsTabs() {
  return (
    <NavTabs>
      <Tab label="Tools" value={routes.RouteLocation.Tools_Dashboard} />
      <Tab label="Programs" value={routes.RouteLocation.Tools_Programs} />
    </NavTabs>
  );
}

function AnalysisTabs() {
  return (
    <NavTabs>
      <Tab label="Cycles" value={routes.RouteLocation.Analysis_Cycles} />
      <Tab label="Efficiency" value={routes.RouteLocation.Analysis_Efficiency} />
      <Tab label="Quality" value={routes.RouteLocation.Analysis_Quality} />
      <Tab label="Cost/Piece" value={routes.RouteLocation.Analysis_CostPerPiece} />
      <Tab label="Schedules" value={routes.RouteLocation.Analysis_Schedules} />
      <Tab label="Data Export" value={routes.RouteLocation.Analysis_DataExport} />
    </NavTabs>
  );
}

function BackupTabs() {
  return (
    <NavTabs>
      <Tab label="Efficiency" value={routes.RouteLocation.Backup_Efficiency} />
      <Tab label="Part Lookup" value={routes.RouteLocation.Backup_PartLookup} />
      <Tab label="Schedules" value={routes.RouteLocation.Backup_Schedules} />
    </NavTabs>
  );
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

function Alarms() {
  const alarms = useRecoilValue(currentStatus).alarms;
  const hasAlarms = alarms && alarms.length > 0;

  const alarmTooltip = hasAlarms ? alarms.join(". ") : "No Alarms";
  return (
    <Tooltip title={alarmTooltip}>
      <Badge badgeContent={hasAlarms ? alarms.length : 0}>
        <Notifications color={hasAlarms ? "error" : undefined} />
      </Badge>
    </Tooltip>
  );
}

function HelpButton() {
  const [route] = routes.useCurrentRoute();
  return (
    <Tooltip title="Help">
      <IconButton aria-label="Help" href={routes.helpUrl(route)} target="_help" size="large">
        <HelpOutline />
      </IconButton>
    </Tooltip>
  );
}

function LogoutButton() {
  return (
    <Tooltip title="Logout">
      <IconButton aria-label="Logout" onClick={serverSettings.logout} size="large">
        <ExitToApp />
      </IconButton>
    </Tooltip>
  );
}

function SearchButtons() {
  return (
    <>
      {window.location.protocol === "https:" || window.location.hostname === "localhost" ? (
        <SerialScannerButton />
      ) : undefined}
      <ManualScanButton />
    </>
  );
}

function Header({
  showAlarms,
  showLogout,
  showSearch,
  showOperator,
  Nav,
  NavCenter,
}: {
  showAlarms: boolean;
  showLogout: boolean;
  showSearch: boolean;
  showOperator: boolean;
  Nav: React.ComponentType | undefined;
  NavCenter: React.ComponentType | undefined;
}) {
  const fmsInfoM = useRecoilValueLoadable(serverSettings.fmsInformation);
  const fmsInfo = fmsInfoM.valueMaybe();

  let tooltip: JSX.Element | string = "";
  if (fmsInfo) {
    tooltip = (fmsInfo.name || "") + " " + (fmsInfo.version || "");
  }

  return (
    <>
      <AppBar position="static" sx={{ display: { xs: "none", md: "block" } }}>
        <Toolbar>
          <Box
            display="grid"
            gridTemplateColumns={NavCenter ? "1fr auto 1fr" : "minmax(200px, 1fr) auto"}
            width="100vw"
            gridTemplateAreas={NavCenter ? '"nav navCenter tools"' : '"nav tools"'}
          >
            <Box gridArea="nav" display="flex" alignItems="center">
              <Tooltip title={tooltip}>
                <div>
                  <SeedtacticLogo />
                </div>
              </Tooltip>
              <Typography variant="h6" style={{ marginLeft: "1em", marginRight: "2em" }}>
                Insight
              </Typography>
              {fmsInfo?.licenseExpires ? <ShowLicense d={fmsInfo.licenseExpires} /> : undefined}
              {Nav ? <Nav /> : undefined}
            </Box>
            {NavCenter ? (
              <Box gridArea="navCenter">
                <NavCenter />
              </Box>
            ) : undefined}
            <Box gridArea="tools" display="flex" alignItems="center" justifyContent="flex-end">
              <LoadingIcon />
              {showOperator ? <OperatorSelect /> : undefined}
              {showOperator ? <CustomStationMonitorDialog /> : undefined}
              {showSearch ? <SearchButtons /> : undefined}
              <HelpButton />
              {showLogout ? <LogoutButton /> : undefined}
              {showAlarms ? <Alarms /> : undefined}
            </Box>
          </Box>
        </Toolbar>
      </AppBar>
      <AppBar position="static" sx={{ display: { xs: "block", md: "none" } }}>
        <Toolbar>
          <Tooltip title={tooltip}>
            <div>
              <SeedtacticLogo />
            </div>
          </Tooltip>
          <Typography variant="h6" style={{ marginLeft: "4px" }}>
            Insight
          </Typography>
          {fmsInfo?.licenseExpires ? <ShowLicense d={fmsInfo.licenseExpires} /> : undefined}
          <div style={{ flexGrow: 1 }} />
          <LoadingIcon />
          {showOperator ? <CustomStationMonitorDialog /> : undefined}
          {showSearch ? <SearchButtons /> : undefined}
          <HelpButton />
          {showLogout ? <LogoutButton /> : undefined}
          {showAlarms ? <Alarms /> : undefined}
        </Toolbar>
        <Box display="grid" gridTemplateColumns="minmax(200px, 1fr) auto">
          <Box gridColumn="1">{Nav ? <Nav /> : undefined}</Box>
          <Box gridColumn="2">{NavCenter ? <NavCenter /> : undefined}</Box>
        </Box>
      </AppBar>
    </>
  );
}

export interface AppProps {
  readonly renderCustomPage?: (custom: ReadonlyArray<string>) => {
    readonly nav: React.ComponentType | undefined;
    readonly page: JSX.Element;
  };
  readonly chooseModes?: ReadonlyArray<ChooseModeItem>;
}

const App = React.memo(function App(props: AppProps) {
  routes.useWatchHistory();
  const fmsInfoLoadable = useRecoilValueLoadable(serverSettings.fmsInformation);
  const [route, setRoute] = routes.useCurrentRoute();

  // BaseLoadable<T>.valueMaybe() has the wrong type so hard to use :(
  const fmsInfo = fmsInfoLoadable.state === "hasValue" ? fmsInfoLoadable.valueOrThrow() : null;

  let page: JSX.Element;
  let navigation: React.ComponentType | undefined = undefined;
  let navigationCenter: React.ComponentType | undefined = undefined;
  let showAlarms = true;
  const showLogout = fmsInfo !== null && fmsInfo.user !== null && fmsInfo.user !== undefined;
  let showSearch = true;
  let showOperator = false;
  let addBasicMaterialDialog = true;
  if (fmsInfo && (!serverSettings.requireLogin(fmsInfo) || fmsInfo.user)) {
    switch (route.route) {
      case routes.RouteLocation.Station_LoadMonitor:
        page = <LoadStation loadNum={route.loadNum} queues={route.queues} />;
        navigation = StationToolbar;
        navigationCenter = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_InspectionMonitor:
        page = <Inspection focusInspectionType={null} />;
        navigation = StationToolbar;
        navigationCenter = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_InspectionMonitorWithType:
        page = <Inspection focusInspectionType={route.inspType} />;
        navigation = StationToolbar;
        navigationCenter = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_WashMonitor:
        page = <Wash />;
        navigation = StationToolbar;
        navigationCenter = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_Queues:
        page = <Queues queues={route.queues} />;
        navigation = StationToolbar;
        navigationCenter = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_Overview:
        page = <SystemOverviewPage />;
        addBasicMaterialDialog = false;
        break;

      case routes.RouteLocation.Analysis_CostPerPiece:
        page = <CostPerPiece />;
        navigation = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Cycles:
        page = <AnalysisCyclePage />;
        navigation = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Efficiency:
        page = <Efficiency />;
        navigation = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Quality:
        page = <QualityPage />;
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
      case routes.RouteLocation.Operations_SystemOverview:
        page = <SystemOverviewPage ignoreOperator />;
        navigation = OperationsTabs;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Operations_AllMaterial:
        page = <AllMaterial displaySystemBins />;
        navigation = OperationsTabs;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Operations_RecentSchedules:
        page = <RecentSchedulesPage />;
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
        showAlarms={showAlarms}
        showSearch={showSearch}
        showLogout={showLogout}
        showOperator={showOperator}
        Nav={navigation}
        NavCenter={navigationCenter}
      />
      {page}
      {addBasicMaterialDialog ? <MaterialDialog /> : undefined}
      <WebsocketConnection />
      <BarcodeListener />
    </div>
  );
});

export default App;
