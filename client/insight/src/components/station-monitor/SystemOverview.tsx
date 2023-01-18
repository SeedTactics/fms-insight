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
import {
  ActionType,
  IInProcessMaterial,
  IPalletLocation,
  IPalletStatus,
  LocType,
} from "../../network/api.js";
import { useRecoilValue } from "recoil";
import { currentStatus } from "../../cell-status/current-status.js";
import { ComparableObj, LazySeq } from "@seedtactics/immutable-collections";
import { useSetMaterialToShowInDialog } from "../../cell-status/material-details.js";

const CollapsedIconSize = 45;
const rowSize = CollapsedIconSize + 10; // each material row has 5px above and 5px below for padding

type PalletAndMaterial = {
  readonly pallet: Readonly<IPalletStatus>;
  readonly mats: ReadonlyArray<Readonly<IInProcessMaterial>>;
};

type MachineStatus = {
  readonly name: string;
  readonly inbound: PalletAndMaterial | null;
  readonly worktable: PalletAndMaterial | null;
  readonly outbound: PalletAndMaterial | null;
};

type LoadStatus = {
  readonly lulNum: number;
  readonly pal: PalletAndMaterial | null;
};

type CellOverview = {
  readonly machines: ReadonlyArray<MachineStatus>;
  readonly loads: ReadonlyArray<LoadStatus>;
  readonly stockerPals: ReadonlyArray<PalletAndMaterial>;
  readonly maxNumFacesOnPallet: number;
};

class PalLoc implements ComparableObj {
  constructor(public readonly loc: Readonly<IPalletLocation>) {}
  compare(other: PalLoc): number {
    return (
      this.loc.loc.localeCompare(other.loc.loc) ||
      this.loc.group.localeCompare(other.loc.group) ||
      this.loc.num - other.loc.num
    );
  }
}

function useCellOverview(): CellOverview {
  const currentSt = useRecoilValue(currentStatus);
  //const jobs = useRecoilValue(last30Jobs);

  const matByPal = LazySeq.of(currentSt.material)
    .filter((m) => m.location.type === LocType.OnPallet || m.action.type === ActionType.Loading)
    .toRLookup((m) => m.location.pallet ?? m.action.loadOntoPallet ?? "");

  const palByLoc = LazySeq.ofObject(currentSt.pallets).toOrderedMap(([palName, pal]) => [
    new PalLoc(pal.currentPalletLocation),
    { pallet: pal, mats: matByPal.get(palName) ?? [] },
  ]);

  return {
    machines: [],
    loads: [],
    stockerPals: palByLoc.valuesToAscLazySeq().toRArray(),
    maxNumFacesOnPallet: 2,
  };
}

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
              key={mat.materialID}
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

function PalletFaces({
  maxNumFaces,
  pallet,
  loadingOntoPallet,
}: {
  maxNumFaces: number;
  pallet: PalletAndMaterial;
  loadingOntoPallet?: boolean;
}) {
  const byFace = loadingOntoPallet
    ? LazySeq.of(pallet.mats)
        .filter((m) => m.location.type !== LocType.OnPallet)
        .orderedGroupBy((m) => m.action.loadOntoFace ?? 1)
    : LazySeq.of(pallet.mats)
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

function gridTemplateColumns(maxNumFaces: number) {
  // each material column is at least CollapsedIconSize * (maxNumFaces) for the icon + 5px * (maxNumFaces + 1) for the columnGap in PalletFaces
  const colSize = CollapsedIconSize * maxNumFaces + 5 * (maxNumFaces + 1);
  return `60px ${Math.max(colSize, 100)}px`;
}

function Machine({ maxNumFaces, machine }: { maxNumFaces: number; machine: MachineStatus }) {
  return (
    <Box
      display="grid"
      border="1px solid black"
      margin="5px"
      gridTemplateRows={`auto ${rowSize}px ${rowSize}px ${rowSize}px`}
      gridTemplateColumns={gridTemplateColumns(maxNumFaces)}
      gridTemplateAreas={`"machname machname" "inboundpal inboundmat" "worktablepal worktablemat" "outboundpal outboundmat"`}
    >
      <Typography variant="h5" gridArea="machname" padding="0.2em" borderBottom="1px solid black">
        {machine.name}: Idle
      </Typography>
      <Box gridArea="inboundpal" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            In
          </Typography>
          {machine.inbound ? (
            <Typography variant="h6" textAlign="center">
              {machine.inbound.pallet.pallet}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="inboundmat" borderBottom="1px solid black">
        {machine.inbound ? <PalletFaces pallet={machine.inbound} maxNumFaces={maxNumFaces} /> : undefined}
      </Box>
      <Box gridArea="worktablepal" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Work
          </Typography>
          {machine.worktable ? (
            <Typography variant="h6" textAlign="center">
              {machine.worktable.pallet.pallet}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="worktablemat" borderBottom="1px solid black">
        {machine.worktable ? <PalletFaces pallet={machine.worktable} maxNumFaces={maxNumFaces} /> : undefined}
      </Box>
      <Box gridArea="outboundpal" borderRight="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Out
          </Typography>
          {machine.outbound ? (
            <Typography variant="h6" textAlign="center">
              {machine.outbound.pallet.pallet}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="outboundmat">
        {machine.outbound ? <PalletFaces pallet={machine.outbound} maxNumFaces={maxNumFaces} /> : undefined}
      </Box>
    </Box>
  );
}

function LoadStation({ maxNumFaces, load }: { maxNumFaces: number; load: LoadStatus }) {
  return (
    <Box
      display="grid"
      border="1px solid black"
      margin="5px"
      gridTemplateRows={`auto ${rowSize}px ${rowSize}px`}
      gridTemplateColumns={gridTemplateColumns(maxNumFaces)}
      gridTemplateAreas={`"lulname lulname" "loading loadingmat" "currentpal currentmat"`}
    >
      <Typography variant="h5" gridArea="lulname" padding="0.2em" borderBottom="1px solid black">
        L/U {load.lulNum}: Idle
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
      <Box sx={{ gridArea: "loadingmat", borderBottom: "1px solid black" }}>
        {load.pal ? <PalletFaces pallet={load.pal} maxNumFaces={maxNumFaces} loadingOntoPallet /> : undefined}
      </Box>
      <Box gridArea="currentpal" borderRight="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Pallet
          </Typography>
          {load.pal ? (
            <Typography variant="h6" textAlign="center">
              {load.pal.pallet.pallet}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="currentmat">
        {load.pal ? <PalletFaces pallet={load.pal} maxNumFaces={maxNumFaces} /> : undefined}
      </Box>
    </Box>
  );
}

export function SystemOverview() {
  const overview = useCellOverview();
  return (
    <div>
      <div>
        <Box display="flex" flexWrap="wrap">
          {overview.machines.map((machine) => (
            <Machine key={machine.name} machine={machine} maxNumFaces={overview.maxNumFacesOnPallet} />
          ))}
        </Box>
      </div>
      <div>
        <Box display="flex" flexWrap="wrap">
          {overview.loads.map((machine) => (
            <LoadStation key={machine.lulNum} load={machine} maxNumFaces={overview.maxNumFacesOnPallet} />
          ))}
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
