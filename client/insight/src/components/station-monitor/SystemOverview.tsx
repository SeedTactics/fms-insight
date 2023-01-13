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
import { Paper, ButtonBase, Box, Typography, Collapse, Stack } from "@mui/material";
import { materialAction, PartIdenticon } from "./Material.js";
import { IInProcessMaterial } from "../../network/api.js";
import { useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status.js";

const CollapsedIconSize = 50;

function MaterialIcon({ mat }: { mat: Readonly<IInProcessMaterial> }) {
  const [open, setOpen] = React.useState<boolean>(false);
  const closeTimeout = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  function enter() {
    if (closeTimeout.current !== null) {
      clearTimeout(closeTimeout.current);
      closeTimeout.current = null;
    }
    setOpen(true);
  }

  function leave() {
    closeTimeout.current = setTimeout(() => {
      setOpen(false);
      closeTimeout.current = null;
    }, 200);
  }

  return (
    <Box sx={{ width: CollapsedIconSize, height: CollapsedIconSize, overflow: "visible" }}>
      <Paper
        elevation={4}
        onPointerEnter={enter}
        onPointerLeave={leave}
        sx={{ position: "relative", zIndex: open ? 10 : 0, width: "max-content", height: "max-content" }}
      >
        <ButtonBase focusRipple>
          <Collapse orientation="horizontal" in={open} collapsedSize={CollapsedIconSize}>
            <Box sx={{ display: "flex" }}>
              <PartIdenticon part={mat.partName} size={CollapsedIconSize} />
              <Box sx={{ marginLeft: "10px", marginRight: "10px", whiteSpace: "nowrap", textAlign: "left" }}>
                <Collapse in={open} collapsedSize={CollapsedIconSize}>
                  <Typography variant="h6">{mat.partName}</Typography>
                  {mat.serial ? (
                    <div>
                      <small>Serial: {mat.serial}</small>
                    </div>
                  ) : undefined}
                  <Box sx={{ marginBottom: "0.2em" }}>
                    <small>{materialAction(mat)}</small>
                  </Box>
                </Collapse>
              </Box>
            </Box>
          </Collapse>
        </ButtonBase>
      </Paper>
    </Box>
  );
}

function Machine() {
  const curSt = useRecoilValue(currentStatus);

  const maxNumFaces = 2;

  // each material row has 5px above and 5px below for padding
  const rowSize = CollapsedIconSize + 10;
  // each material column is at least CollapsedIconSize * (maxNumFaces) for the icon + 8px * (maxNumFaces + 1) for the margins
  const minColSize = CollapsedIconSize * maxNumFaces + 8 * (maxNumFaces + 1);

  return (
    <Box
      sx={{
        display: "grid",
        border: "1px solid black",
        margin: "5px",
        gridTemplateRows: `auto ${rowSize}px ${rowSize}px ${rowSize}px`,
        gridTemplateColumns: `60px minmax(${minColSize}px, auto)`,
        gridTemplateAreas: `"machname machname" "inboundpal inboundmat" "worktablepal worktablemat" "outboundpal outboundmat"`,
      }}
    >
      <Typography
        variant="h5"
        sx={{
          gridArea: "machname",
          padding: "0.2em",
          borderBottom: "1px solid black",
        }}
      >
        Machine 3: Idle
      </Typography>
      <Typography
        variant="body1"
        sx={{
          gridArea: "inboundpal",
          borderBottom: "1px solid black",
          borderRight: "1px solid black",
          padding: "2px",
        }}
      >
        In
      </Typography>
      <Box sx={{ gridArea: "inboundmat", borderBottom: "1px solid black", paddingTop: "5px" }} />
      <Typography
        variant="body1"
        sx={{
          gridArea: "worktablepal",
          borderBottom: "1px solid black",
          borderRight: "1px solid black",
          padding: "2px",
        }}
      >
        Work
      </Typography>
      <Box
        sx={{
          gridArea: "worktablemat",
          borderBottom: "1px solid black",
          paddingTop: "5px",
        }}
      >
        <Stack direction="row" spacing={1} marginLeft="8px" marginRight="8px">
          <MaterialIcon mat={curSt.material[0]} />
        </Stack>
      </Box>
      <Typography
        variant="body1"
        sx={{ gridArea: "outboundpal", borderRight: "1px solid black", padding: "2px" }}
      >
        Out
      </Typography>
      <Box sx={{ gridArea: "outboundmat", paddingTop: "5px" }} />
    </Box>
  );
}

function LoadStation() {
  const curSt = useRecoilValue(currentStatus);

  const maxNumFaces = 2;

  // each material row has 5px above and 5px below for padding
  const rowSize = CollapsedIconSize + 10;
  // each material column is at least CollapsedIconSize * (maxNumFaces) for the icon + 8px * (maxNumFaces + 1) for the margins
  const minColSize = CollapsedIconSize * maxNumFaces + 8 * (maxNumFaces + 1);

  return (
    <Box
      sx={{
        display: "grid",
        border: "1px solid black",
        margin: "5px",
        gridTemplateRows: `auto ${rowSize}px ${rowSize}px`,
        gridTemplateColumns: `60px minmax(${minColSize}px, auto)`,
        gridTemplateAreas: `"lulname lulname" "loadingpal loadingmat" "unloadingpal unloadingmat"`,
      }}
    >
      <Typography
        variant="h5"
        sx={{
          gridArea: "lulname",
          padding: "0.2em",
          borderBottom: "1px solid black",
        }}
      >
        L/U 3: Idle
      </Typography>
      <Typography
        variant="body1"
        sx={{
          gridArea: "loadingpal",
          borderBottom: "1px solid black",
          borderRight: "1px solid black",
          padding: "2px",
        }}
      >
        Load
      </Typography>
      <Box sx={{ gridArea: "loadingmat", borderBottom: "1px solid black", paddingTop: "5px" }} />
      <Typography
        variant="body1"
        sx={{
          gridArea: "unloadingpal",
          borderRight: "1px solid black",
          padding: "2px",
        }}
      >
        Unload
      </Typography>
      <Box
        sx={{
          gridArea: "unloadingmat",
          paddingTop: "5px",
        }}
      >
        <Stack direction="row" spacing={1} marginLeft="8px" marginRight="8px">
          <MaterialIcon mat={curSt.material[0]} />
        </Stack>
      </Box>
    </Box>
  );
}

export function SystemOverview() {
  const curSt = useRecoilValue(currentStatus);
  return (
    <div>
      <div>
        <div style={{ display: "flex", flexWrap: "wrap" }}>
          {curSt.material.map((m) => (
            <MaterialIcon key={m.materialID} mat={m} />
          ))}
        </div>
      </div>
      <div style={{ marginTop: "4em" }}>
        <Box display="flex" flexWrap="wrap">
          <Machine />
          <Machine />
          <Machine />
          <Machine />
          <Machine />
          <Machine />
          <Machine />
          <Machine />
          <Machine />
          <Machine />
          <Machine />
        </Box>
      </div>
      <div style={{ marginTop: "4em" }}>
        <Box display="flex" flexWrap="wrap">
          <LoadStation />
          <LoadStation />
          <LoadStation />
          <LoadStation />
        </Box>
      </div>
    </div>
  );
}

export function SystemOverviewPage() {
  React.useEffect(() => {
    document.title = "System Overview - FMS Insight";
  }, []);

  return (
    <main
      style={{
        padding: "24px",
        minHeight: "calc(100vh - 64px)",
        backgroundColor: "#EEEEEE",
      }}
    >
      <SystemOverview />
    </main>
  );
}
