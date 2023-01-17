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
import { Paper, ButtonBase, Box, Typography, Collapse, Stack, Avatar, Menu, MenuItem } from "@mui/material";
import { materialAction, PartIdenticon } from "./Material.js";
import { ActionType, IInProcessMaterial, LocType } from "../../network/api.js";
import { selector, useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { useSetMaterialToShowInDialog } from "../../cell-status/material-details.js";

const CollapsedIconSize = 45;
const rowSize = CollapsedIconSize + 10; // each material row has 5px above and 5px below for padding

function MaterialIcon({ mats }: { mats: ReadonlyArray<Readonly<IInProcessMaterial>> }) {
  const [open, setOpen] = React.useState<boolean>(false);
  const closeTimeout = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const btnRef = React.useRef<HTMLButtonElement | null>(null);
  const [menuOpen, setMenuOpen] = React.useState<boolean>(false);
  const setMatToShow = useSetMaterialToShowInDialog();

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

  function click() {
    if (mats.length === 1) {
      setMatToShow({ type: "MatDetails", details: mats[0] });
    } else {
      setMenuOpen(true);
    }
  }

  return (
    <Box sx={{ width: CollapsedIconSize, height: CollapsedIconSize, overflow: "visible" }}>
      <Paper
        elevation={4}
        onPointerEnter={enter}
        onPointerLeave={leave}
        sx={{ position: "relative", zIndex: open ? 10 : 0, width: "max-content", height: "max-content" }}
      >
        <ButtonBase focusRipple onClick={click} ref={btnRef}>
          <Collapse orientation="horizontal" in={open} collapsedSize={CollapsedIconSize}>
            <Box display="flex">
              <PartIdenticon part={mats[0].partName} size={CollapsedIconSize} />
              <Box marginLeft="10px" marginRight="10px" whiteSpace="nowrap" textAlign="left">
                <Collapse in={open} collapsedSize={CollapsedIconSize}>
                  <Stack direction="column" marginBottom="0.2em">
                    <Box display="flex" justifyContent="space-between" alignItems="baseline">
                      <Typography variant="h6">
                        {mats[0].partName}-{mats[0].process}
                      </Typography>
                      {mats.length > 1 ? (
                        <Avatar sx={{ width: "30px", height: "30px" }}>{mats.length}</Avatar>
                      ) : undefined}
                    </Box>
                    <div>
                      <small>Face: {mats[0].location.face ?? mats[0].action.loadOntoFace ?? ""}</small>
                    </div>
                    {LazySeq.of(mats).collect((mat) =>
                      mat.serial ? (
                        <div key={mat.materialID}>
                          <small>Serial: {mat.serial}</small>
                        </div>
                      ) : undefined
                    )}
                    <div>
                      <small>{materialAction(mats[0])}</small>
                    </div>
                  </Stack>
                </Collapse>
              </Box>
            </Box>
          </Collapse>
        </ButtonBase>
      </Paper>
      <Menu
        anchorEl={btnRef.current}
        open={menuOpen}
        onClose={() => setMenuOpen(false)}
        anchorOrigin={{ vertical: "top", horizontal: "left" }}
      >
        {LazySeq.of(mats).collect((mat) =>
          mat.serial ? (
            <MenuItem
              onClick={() => {
                setMenuOpen(false);
                setMatToShow({ type: "MatDetails", details: mat });
              }}
            >
              {mat.serial}
            </MenuItem>
          ) : undefined
        )}
      </Menu>
    </Box>
  );
}

const materialByPallet = selector<ReadonlyMap<string, ReadonlyArray<Readonly<IInProcessMaterial>>>>({
  key: "overview-materialByPallet",
  get: ({ get }) => {
    const st = get(currentStatus);
    return LazySeq.of(st.material)
      .filter((m) => m.location.type === LocType.OnPallet || m.action.type === ActionType.Loading)
      .toRLookup((m) => m.location.pallet ?? m.action.loadOntoPallet ?? "");
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

const maxFacesOnPallet = selector<number>({
  key: "overview-maxFacesOnPallet",
  get: () => {
    return 2;
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

function PalletFaces({ pallet, loadingOntoPallet }: { pallet: string; loadingOntoPallet?: boolean }) {
  const mats = useRecoilValue(materialByPallet).get(pallet) ?? [];
  const maxNumFaces = useRecoilValue(maxFacesOnPallet);

  const byFace = loadingOntoPallet
    ? LazySeq.of(mats)
        .filter((m) => m.location.type !== LocType.OnPallet)
        .orderedGroupBy((m) => m.action.loadOntoFace ?? 1)
    : LazySeq.of(mats)
        .filter((m) => m.location.type === LocType.OnPallet)
        .orderedGroupBy((m) => m.location.face ?? 1);

  return (
    <Box
      display="grid"
      gridTemplateRows={`${CollapsedIconSize}px`}
      gridTemplateColumns={`repeat(${maxNumFaces}, ${CollapsedIconSize}px)`}
      columnGap="5px"
      height="100%"
      alignContent="center"
      justifyContent="center"
    >
      {byFace.map(([face, mats]) => (
        <Box key={face} gridColumn={face} gridRow={1}>
          <MaterialIcon mats={mats} />
        </Box>
      ))}
    </Box>
  );
}

function useGridTemplateColumns() {
  const maxNumFaces = useRecoilValue(maxFacesOnPallet);
  // each material column is at least CollapsedIconSize * (maxNumFaces) for the icon + 5px * (maxNumFaces + 1) for the columnGap in PalletFaces
  const minColSize = CollapsedIconSize * maxNumFaces + 5 * (maxNumFaces + 1);

  return `60px minmax(${minColSize}px, auto)`;
}

function Machine() {
  const gridCols = useGridTemplateColumns();

  return (
    <Box
      display="grid"
      border="1px solid black"
      margin="5px"
      gridTemplateRows={`auto ${rowSize}px ${rowSize}px ${rowSize}px`}
      gridTemplateColumns={gridCols}
      gridTemplateAreas={`"machname machname" "inboundpal inboundmat" "worktablepal worktablemat" "outboundpal outboundmat"`}
    >
      <Typography variant="h5" gridArea="machname" padding="0.2em" borderBottom="1px solid black">
        Machine 3: Idle
      </Typography>
      <Box gridArea="inboundpal" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            In
          </Typography>
          <Typography variant="h6" textAlign="center">
            5
          </Typography>
        </Stack>
      </Box>
      <Box gridArea="inboundmat" borderBottom="1px solid black" />
      <Box gridArea="worktablepal" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Work
          </Typography>
          <Typography variant="h6" textAlign="center">
            4
          </Typography>
        </Stack>
      </Box>
      <Box gridArea="worktablemat" borderBottom="1px solid black">
        <PalletFaces pallet="7" />
      </Box>
      <Box gridArea="outboundpal" borderRight="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Out
          </Typography>
          <Typography variant="h6" textAlign="center">
            16
          </Typography>
        </Stack>
      </Box>
      <Box gridArea="outboundmat" />
    </Box>
  );
}

function LoadStation() {
  const gridCols = useGridTemplateColumns();

  return (
    <Box
      display="grid"
      border="1px solid black"
      margin="5px"
      gridTemplateRows={`auto ${rowSize}px ${rowSize}px`}
      gridTemplateColumns={gridCols}
      gridTemplateAreas={`"lulname lulname" "loading loadingmat" "currentpal currentmat"`}
    >
      <Typography variant="h5" gridArea="lulname" padding="0.2em" borderBottom="1px solid black">
        L/U 3: Idle
      </Typography>
      <Box gridArea="loading" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            To
          </Typography>
          <Typography variant="body1" textAlign="center">
            Load
          </Typography>
        </Stack>
      </Box>
      <Box sx={{ gridArea: "loadingmat", borderBottom: "1px solid black" }} />
      <Box gridArea="currentpal" borderRight="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Pallet
          </Typography>
          <Typography variant="h6" textAlign="center">
            6
          </Typography>
        </Stack>
      </Box>
      <Box gridArea="currentmat"></Box>
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
            <MaterialIcon key={m.materialID} mats={[m]} />
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
