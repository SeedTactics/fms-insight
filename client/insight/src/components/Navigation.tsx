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

import { ReactNode, ComponentType } from "react";
import { Notifications, HelpOutline, ExitToApp } from "@mui/icons-material";
import {
  Typography,
  Tooltip,
  Badge,
  IconButton,
  AppBar,
  Toolbar,
  Box,
  List,
  ListItem,
  ListItemIcon,
  ListItemButton,
  ListItemText,
  ListSubheader,
  Paper,
  Select,
  MenuItem,
  FormControl,
} from "@mui/material";
import { startOfToday, differenceInDays } from "date-fns";
import { currentStatus } from "../cell-status/current-status";
import { SeedtacticLogo } from "../seedtactics-logo";
import { OperatorSelect } from "./ChooseOperator";
import { LoadingIcon } from "./LoadingIcon";
import { ManualScanButton } from "./ManualScan";
import { CustomStationMonitorDialog } from "./station-monitor/CustomStationMonitorDialog";
import { RouteLocation, RouteState, currentRoute, helpUrl } from "./routes";
import { fmsInformation, logout } from "../network/server-settings";
import { useAtom, useAtomValue } from "jotai";
import { QRScanButton } from "./BarcodeScanning";

export type MenuNavItem =
  | {
      readonly name: string;
      readonly icon: ReactNode;
      readonly route: RouteState;
    }
  | { readonly separator: string };

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

function Brand() {
  const fmsInfo = useAtomValue(fmsInformation);

  let tooltip: JSX.Element | string = "";
  if (fmsInfo.name) {
    tooltip = (fmsInfo.name || "") + " " + (fmsInfo.version || "");
  } else {
    tooltip = "SeedTactic FMS Insight";
  }

  return (
    <>
      <Tooltip title={tooltip}>
        <div>
          <SeedtacticLogo />
        </div>
      </Tooltip>
      <Typography variant="h6" style={{ marginLeft: "1em", marginRight: "2em" }}>
        Insight
      </Typography>
      {fmsInfo?.licenseExpires ? <ShowLicense d={fmsInfo.licenseExpires} /> : undefined}
    </>
  );
}

function Alarms() {
  const alarms = useAtomValue(currentStatus).alarms;
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
  const route = useAtomValue(currentRoute);
  return (
    <Tooltip title="Help">
      <IconButton aria-label="Help" href={helpUrl(route)} target="_help" size="large">
        <HelpOutline />
      </IconButton>
    </Tooltip>
  );
}

function LogoutButton() {
  return (
    <Tooltip title="Logout">
      <IconButton aria-label="Logout" onClick={logout} size="large">
        <ExitToApp />
      </IconButton>
    </Tooltip>
  );
}

function ToolButtons({
  showAlarms,
  showLogout,
  showSearch,
}: {
  showAlarms: boolean;
  showLogout: boolean;
  showSearch: boolean;
}) {
  return (
    <>
      <LoadingIcon />
      <CustomStationMonitorDialog />
      {showSearch ? <QRScanButton /> : undefined}
      {showSearch ? <ManualScanButton /> : undefined}
      <HelpButton />
      {showLogout ? <LogoutButton /> : undefined}
      {showAlarms ? <Alarms /> : undefined}
    </>
  );
}

function MenuNavSelect({ menuNavs }: { menuNavs: ReadonlyArray<MenuNavItem> }) {
  const [curRoute, setCurrentRoute] = useAtom(currentRoute);
  return (
    <FormControl size="small">
      <Select
        value={curRoute.route}
        sx={{ width: "16.5em" }}
        onChange={(e) => {
          const item = menuNavs.find((i) =>
            "separator" in i ? false : i.route.route === (e.target.value as RouteLocation),
          );
          if (!item || "separator" in item) return;
          setCurrentRoute(item.route);
        }}
        renderValue={() => {
          const item = menuNavs.find((i) => ("separator" in i ? false : i.route.route === curRoute.route));
          if (!item || "separator" in item) return null;
          return (
            <Box display="flex" alignItems="center">
              {item.icon}
              <Typography variant="h6" style={{ marginLeft: "1em" }}>
                {item.name}
              </Typography>
            </Box>
          );
        }}
      >
        {menuNavs.map((item) =>
          "separator" in item ? (
            <ListSubheader key={item.separator}>{item.separator}</ListSubheader>
          ) : (
            <MenuItem key={item.name} value={item.route.route}>
              <ListItemIcon>{item.icon}</ListItemIcon>
              <ListItemText primary={item.name} />
            </MenuItem>
          ),
        )}
      </Select>
    </FormControl>
  );
}

export function Header({
  showAlarms,
  showLogout,
  showSearch,
  showOperator,
  Nav1,
  Nav2,
  menuNavs,
}: {
  showAlarms: boolean;
  showLogout: boolean;
  showSearch: boolean;
  showOperator: boolean;
  Nav1: ComponentType | undefined;
  Nav2: ComponentType | undefined;
  menuNavs?: ReadonlyArray<MenuNavItem>;
}) {
  return (
    <>
      <AppBar position="static" sx={{ display: { xs: "none", md: "block" } }}>
        <Toolbar>
          <Box
            display="grid"
            gridTemplateColumns={Nav2 ? "1fr auto 1fr" : "minmax(200px, 1fr) auto"}
            width="100vw"
            gridTemplateAreas={Nav2 ? '"nav navCenter tools"' : '"nav tools"'}
          >
            <Box gridArea="nav" display="flex" alignItems="center">
              <Brand />
              {Nav1 ? <Nav1 /> : undefined}
            </Box>
            {Nav2 ? (
              <Box gridArea="navCenter">
                <Nav2 />
              </Box>
            ) : undefined}
            <Box gridArea="tools" display="flex" alignItems="center" justifyContent="flex-end">
              {showOperator ? <OperatorSelect /> : undefined}
              <ToolButtons {...{ showAlarms, showLogout, showSearch }} />
            </Box>
          </Box>
        </Toolbar>
      </AppBar>
      <AppBar position="static" sx={{ display: { xs: "block", md: "none" } }}>
        <Toolbar>
          <Brand />
          <div style={{ flexGrow: 1 }} />
          <ToolButtons {...{ showAlarms, showLogout, showSearch }} />
        </Toolbar>
        <Box display="grid" gridTemplateColumns="minmax(200px, 1fr) auto">
          <Box gridColumn="1">{Nav1 ? <Nav1 /> : undefined}</Box>
          <Box gridColumn="2">{Nav2 ? <Nav2 /> : undefined}</Box>
        </Box>
      </AppBar>
      {menuNavs && menuNavs.length > 0 ? (
        <Paper
          elevation={4}
          square
          component="nav"
          sx={{ display: { xs: "block", xl: "none" }, padding: "2px" }}
        >
          <MenuNavSelect menuNavs={menuNavs} />
        </Paper>
      ) : undefined}
    </>
  );
}

export function SideMenu({ menuItems }: { menuItems?: ReadonlyArray<MenuNavItem> }) {
  const [curRoute, setCurrentRoute] = useAtom(currentRoute);

  if (!menuItems || menuItems.length === 0) {
    return null;
  }

  return (
    <Paper
      elevation={3}
      square
      sx={{
        flexShrink: 0,
        display: { xs: "none", xl: "block" },
        minHeight: "calc(100vh - 64px)",
        zIndex: 1,
      }}
    >
      <List dense>
        {menuItems?.map((item) =>
          "separator" in item ? (
            <ListSubheader key={item.separator}>{item.separator}</ListSubheader>
          ) : (
            <ListItem key={item.name}>
              <ListItemButton
                selected={curRoute.route === item.route.route}
                onClick={() => setCurrentRoute(item.route)}
              >
                <ListItemIcon>{item.icon}</ListItemIcon>
                <ListItemText primary={item.name} />
              </ListItemButton>
            </ListItem>
          ),
        )}
      </List>
    </Paper>
  );
}
