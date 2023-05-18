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
import { useMediaQuery, useTheme } from "@mui/material";
import { Tabs } from "@mui/material";
import { Tab } from "@mui/material";
import { CircularProgress } from "@mui/material";
import { Button } from "@mui/material";
import { useRecoilValueLoadable } from "recoil";
import {
  Dns as ToolIcon,
  Build as BuildIcon,
  Receipt as ProgramIcon,
  BugReport as BugIcon,
  HourglassFull as HourglassIcon,
  Timeline as WorkIcon,
  CalendarMonth as ScheduleIcon,
  CheckCircle as ProductionIcon,
  AltRoute as InspectionIcon,
} from "@mui/icons-material";

import OperationDashboard from "./operations/Dashboard.js";
import CostPerPiece from "./analysis/CostPerPiece.js";
import Efficiency from "./analysis/EfficiencyPage.js";
import DataExport from "./analysis/DataExport.js";
import ChooseMode, { ChooseModeItem } from "./ChooseMode.js";
import * as routes from "./routes.js";
import * as serverSettings from "../network/server-settings.js";
import { BarcodeListener } from "./BarcodeScanning.js";
import { MaterialDialog } from "./station-monitor/Material.js";
import { RecentSchedulesPage } from "./operations/RecentSchedules.js";
import { AllMaterial } from "./operations/AllMaterial.js";
import { QualityMaterialPage } from "./quality/QualityMaterial.js";
import { QualityPaths } from "./quality/QualityPaths.js";
import LoadStation from "./station-monitor/LoadStation.js";
import Inspection from "./station-monitor/Inspection.js";
import { CloseoutPage } from "./station-monitor/Closeout.js";
import Queues from "./station-monitor/Queues.js";
import { ToolReportPage } from "./operations/ToolReport.js";
import { ProgramReportPage } from "./operations/Programs.js";
import { WebsocketConnection } from "../network/websocket.js";
import { ScheduleHistory } from "./analysis/ScheduleHistory.js";
import { AnalysisCyclePage } from "./analysis/AnalysisCyclesPage.js";
import { QualityPage } from "./analysis/QualityPage.js";
import { SystemOverviewPage } from "./station-monitor/SystemOverview.js";
import { StationToolbar, StationToolbarOverviewButton } from "./station-monitor/StationToolbar.js";
import { RecentProductionPage } from "./operations/RecentProduction.js";
import { VerboseLoggingPage } from "./VerboseLogging.js";
import { Header, MenuNavItem, SideMenu } from "./Navigation.js";
import { OutlierCycles } from "./operations/Outliers.js";
import { StationOEEPage } from "./operations/OEEChart.js";
import { RecentStationCycleChart } from "./operations/RecentStationCycles.js";

const OperationsReportsTab = "bms-operations-reports-tab";

const operationsReports: ReadonlyArray<MenuNavItem> = [
  { separator: "Load/Unload" },
  {
    name: "L/U Outliers",
    route: { route: routes.RouteLocation.Operations_LoadOutliers },
    icon: <BugIcon />,
  },
  {
    name: "L/U Hours",
    route: { route: routes.RouteLocation.Operations_LoadHours },
    icon: <HourglassIcon />,
  },
  {
    name: "L/U Cycles",
    route: { route: routes.RouteLocation.Operations_LoadCycles },
    icon: <WorkIcon />,
  },
  { separator: "Machines" },
  {
    name: "MC Outliers",
    route: { route: routes.RouteLocation.Operations_MachineOutliers },
    icon: <BugIcon />,
  },
  {
    name: "MC Hours",
    route: { route: routes.RouteLocation.Operations_MachineHours },
    icon: <HourglassIcon />,
  },
  {
    name: "MC Cycles",
    route: { route: routes.RouteLocation.Operations_MachineCycles },
    icon: <WorkIcon />,
  },
  {
    name: "Tools",
    route: { route: routes.RouteLocation.Operations_Tools },
    icon: <ToolIcon />,
  },
  {
    name: "Programs",
    route: { route: routes.RouteLocation.Operations_Programs },
    icon: <ProgramIcon />,
  },
  { separator: "Material" },
  {
    name: "Quality",
    route: { route: routes.RouteLocation.Operations_Quality },
    icon: <BuildIcon />,
  },
  {
    name: "Inspections",
    route: { route: routes.RouteLocation.Operations_Inspections },
    icon: <InspectionIcon />,
  },
  { separator: "Cell" },
  {
    name: "Schedules",
    route: { route: routes.RouteLocation.Operations_RecentSchedules },
    icon: <ScheduleIcon />,
  },
  {
    name: "Production",
    route: { route: routes.RouteLocation.Operations_Production },
    icon: <ProductionIcon />,
  },
];

export function NavTabs({ children }: { children?: React.ReactNode }) {
  const [route, setRoute] = routes.useCurrentRoute();
  const theme = useTheme();
  const full = useMediaQuery(theme.breakpoints.down("md"));

  const isOperationReport = operationsReports.some((r) =>
    "separator" in r ? false : r.route.route === route.route
  );

  return (
    <Tabs
      variant={full && (!Array.isArray(children) || children.length < 5) ? "fullWidth" : "scrollable"}
      value={isOperationReport ? OperationsReportsTab : route.route}
      onChange={(e, v) => {
        if (v === OperationsReportsTab) {
          setRoute({ route: routes.RouteLocation.Operations_MachineCycles });
        } else {
          setRoute({ route: v as routes.RouteLocation } as routes.RouteState);
        }
      }}
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
      <Tab label="Cell" value={routes.RouteLocation.Operations_SystemOverview} />
      <Tab label="Material" value={routes.RouteLocation.Operations_AllMaterial} />
      <Tab label="Reports" value={OperationsReportsTab} />
    </NavTabs>
  );
}

function QualityTabs() {
  return (
    <NavTabs>
      <Tab label="Material" value={routes.RouteLocation.Quality_Dashboard} />
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

function EngineeringTabs() {
  return (
    <NavTabs>
      <Tab label="Cycles" value={routes.RouteLocation.Engineering_Cycles} />
      <Tab label="Hours" value={routes.RouteLocation.Engineering_Hours} />
      <Tab label="Outliers" value={routes.RouteLocation.Engineering_Outliers} />
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

export interface AppProps {
  readonly renderCustomPage?: (custom: ReadonlyArray<string>) => {
    readonly nav: React.ComponentType | undefined;
    readonly page: JSX.Element;
  };
  readonly chooseModes?: (i: serverSettings.FMSInfoAndUser) => ReadonlyArray<ChooseModeItem> | null;
}

const App = React.memo(function App(props: AppProps) {
  routes.useWatchHistory();
  const fmsInfoLoadable = useRecoilValueLoadable(serverSettings.fmsInformation);
  const [route, setRoute] = routes.useCurrentRoute();

  const fmsInfo = fmsInfoLoadable.valueMaybe();
  const showLogout = !!fmsInfo && fmsInfo.user !== null && fmsInfo.user !== undefined;

  let page: JSX.Element;
  let nav1: React.ComponentType | undefined = undefined;
  let nav2: React.ComponentType | undefined = undefined;
  let menuNavItems: ReadonlyArray<MenuNavItem> | undefined = undefined;
  let showAlarms = true;
  let showSearch = true;
  let showOperator = false;
  let addBasicMaterialDialog = true;
  if (fmsInfo && (!serverSettings.requireLogin(fmsInfo) || fmsInfo.user)) {
    switch (route.route) {
      case routes.RouteLocation.Station_LoadMonitor:
        page = <LoadStation loadNum={route.loadNum} queues={route.queues} completed={route.completed} />;
        nav1 = StationToolbar;
        nav2 = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_InspectionMonitor:
        page = <Inspection focusInspectionType={null} />;
        nav1 = StationToolbar;
        nav2 = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_InspectionMonitorWithType:
        page = <Inspection focusInspectionType={route.inspType} />;
        nav1 = StationToolbar;
        nav2 = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_Closeout:
        page = <CloseoutPage />;
        nav1 = StationToolbar;
        nav2 = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_Queues:
        page = <Queues queues={route.queues} />;
        nav1 = StationToolbar;
        nav2 = StationToolbarOverviewButton;
        showOperator = true;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Station_Overview:
        page = <SystemOverviewPage />;
        addBasicMaterialDialog = false;
        break;

      case routes.RouteLocation.Analysis_CostPerPiece:
        page = <CostPerPiece />;
        nav1 = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Cycles:
        page = <AnalysisCyclePage />;
        nav1 = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Efficiency:
        page = <Efficiency />;
        nav1 = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Quality:
        page = <QualityPage />;
        nav1 = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_Schedules:
        page = <ScheduleHistory />;
        nav1 = AnalysisTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Analysis_DataExport:
        page = <DataExport />;
        nav1 = AnalysisTabs;
        showAlarms = false;
        break;

      case routes.RouteLocation.Operations_Dashboard:
        page = <OperationDashboard />;
        nav1 = OperationsTabs;
        break;
      case routes.RouteLocation.Operations_SystemOverview:
        page = <SystemOverviewPage ignoreOperator />;
        nav1 = OperationsTabs;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Operations_AllMaterial:
        page = <AllMaterial displaySystemBins />;
        nav1 = OperationsTabs;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Operations_LoadOutliers:
        page = <OutlierCycles outlierTy="labor" />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_LoadHours:
        page = <StationOEEPage ty="labor" />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_LoadCycles:
        page = <RecentStationCycleChart ty="labor" />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_MachineOutliers:
        page = <OutlierCycles outlierTy="machine" />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_MachineHours:
        page = <StationOEEPage ty="machine" />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_MachineCycles:
        page = <RecentStationCycleChart ty="machine" />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_RecentSchedules:
        page = <RecentSchedulesPage />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_Production:
        page = <RecentProductionPage />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_Quality:
        page = <QualityMaterialPage />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Operations_Inspections:
        page = <QualityPaths />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_Tools:
        page = <ToolReportPage />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;
      case routes.RouteLocation.Operations_Programs:
        page = <ProgramReportPage />;
        nav1 = OperationsTabs;
        menuNavItems = operationsReports;
        break;

      case routes.RouteLocation.Engineering_Cycles:
        page = <RecentStationCycleChart ty="machine" />;
        showAlarms = false;
        nav1 = EngineeringTabs;
        break;
      case routes.RouteLocation.Engineering_Hours:
        page = <StationOEEPage ty="machine" />;
        showAlarms = false;
        nav1 = EngineeringTabs;
        break;
      case routes.RouteLocation.Engineering_Outliers:
        page = <OutlierCycles outlierTy="machine" />;
        showAlarms = false;
        nav1 = EngineeringTabs;
        break;

      case routes.RouteLocation.Quality_Dashboard:
        page = <QualityMaterialPage />;
        nav1 = QualityTabs;
        showAlarms = false;
        addBasicMaterialDialog = false;
        break;
      case routes.RouteLocation.Quality_Paths:
        page = <QualityPaths />;
        nav1 = QualityTabs;
        showAlarms = false;
        break;
      case routes.RouteLocation.Quality_Quarantine:
        page = <AllMaterial displaySystemBins={false} />;
        nav1 = QualityTabs;
        showAlarms = false;
        addBasicMaterialDialog = false;
        break;

      case routes.RouteLocation.Tools_Dashboard:
        page = <ToolReportPage />;
        nav1 = ToolsTabs;
        break;
      case routes.RouteLocation.Tools_Programs:
        page = <ProgramReportPage />;
        nav1 = ToolsTabs;
        break;

      case routes.RouteLocation.VerboseLogging:
        page = <VerboseLoggingPage />;
        showSearch = false;
        showAlarms = false;
        break;

      case routes.RouteLocation.Client_Custom: {
        const customPage = props.renderCustomPage?.(route.custom);
        nav1 = customPage?.nav;
        page = customPage?.page ?? <ChooseMode setRoute={setRoute} modes={props.chooseModes?.(fmsInfo)} />;
        showAlarms = false;
        break;
      }

      case routes.RouteLocation.ChooseMode:
      default:
        page = <ChooseMode setRoute={setRoute} modes={props.chooseModes?.(fmsInfo)} />;
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
        Nav1={nav1}
        Nav2={nav2}
        menuNavs={menuNavItems}
      />
      <div style={{ display: "flex" }}>
        <SideMenu menuItems={menuNavItems} />
        <div style={{ flexGrow: 1 }}>{page}</div>
      </div>
      {addBasicMaterialDialog ? <MaterialDialog /> : undefined}
      <WebsocketConnection />
      <BarcodeListener />
    </div>
  );
});

export default App;
