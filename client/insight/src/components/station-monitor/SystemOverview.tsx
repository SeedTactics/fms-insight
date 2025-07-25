/* Copyright (c) 2024, John Lenz

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

import { useState, useRef, memo } from "react";
import {
  Paper,
  ButtonBase,
  Box,
  Typography,
  Collapse,
  Stack,
  Menu,
  MenuItem,
  LinearProgress,
  Badge,
  Button,
  Dialog,
  DialogContent,
  AppBar,
  Toolbar,
  IconButton,
  useTheme,
} from "@mui/material";
import { Close as CloseIcon } from "@mui/icons-material";
import { InProcMaterial, MaterialAction, MaterialDialog, PartIdenticon } from "./Material.js";
import {
  ActionType,
  IInProcessMaterial,
  IPalletStatus,
  LocType,
  PalletLocationEnum,
} from "../../network/api.js";
import { currentStatus, secondsSinceEpochAtom } from "../../cell-status/current-status.js";
import { LazySeq, OrderedMap } from "@seedtactics/immutable-collections";
import { materialDialogOpen } from "../../cell-status/material-details.js";
import { last30Jobs } from "../../cell-status/scheduled-jobs.js";
import { addDays } from "date-fns";
import { durationToSeconds } from "../../util/parseISODuration.js";
import {
  InvalidateCycleDialogButton,
  InvalidateCycleDialogContent,
  InvalidateCycleState,
  SwapMaterialButtons,
  SwapMaterialDialogContent,
  SwapMaterialState,
} from "./InvalidateCycle.js";
import { QuarantineMatButton } from "./QuarantineButton.js";
import { SelectInspTypeDialog, SignalInspectionButton } from "./SelectInspType.js";
import { useSetTitle } from "../routes.js";
import { useAtomValue, useSetAtom } from "jotai";

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

type MachineAtLoadStatus = {
  readonly lulNum: number;
  readonly machineMoving: boolean;
  readonly machineCurrentlyAtLoad: { readonly group: string; readonly num: number } | null;
  readonly readyMats: ReadonlyArray<Readonly<IInProcessMaterial>>;
  readonly machiningMats: ReadonlyArray<Readonly<IInProcessMaterial>>;
  readonly loadingMats: ReadonlyArray<Readonly<IInProcessMaterial>>;
};

type CellOverview = {
  readonly machines: OrderedMap<string, ReadonlyArray<MachineStatus>>;
  readonly loads: ReadonlyArray<LoadStatus>;
  readonly stockerPals: ReadonlyArray<PalletAndMaterial>;
  readonly machineAtLoad: ReadonlyArray<MachineAtLoadStatus>;
  readonly maxNumFacesOnPallet: number;
};

function useCellOverview(): CellOverview {
  const currentSt = useAtomValue(currentStatus);
  const jobs = useAtomValue(last30Jobs);

  const matByPal = LazySeq.of(currentSt.material)
    .filter((m) => m.location.type === LocType.OnPallet || m.action.type === ActionType.Loading)
    .toRLookup((m) => m.location.palletNum ?? m.action.loadOntoPalletNum ?? 0);

  let loads: OrderedMap<number, LoadStatus> = LazySeq.ofObject(currentSt.pallets)
    .filter(([_, p]) => p.currentPalletLocation.loc === PalletLocationEnum.LoadUnload)
    .toOrderedMap(([_, p]) => [
      p.currentPalletLocation.num,
      {
        lulNum: p.currentPalletLocation.num,
        pal: { pallet: p, mats: matByPal.get(p.palletNum) ?? [] },
      },
    ]);

  let machines: OrderedMap<string, OrderedMap<number, MachineStatus>> = LazySeq.ofObject(currentSt.pallets)
    .filter(
      ([, p]) =>
        p.currentPalletLocation.loc === PalletLocationEnum.Machine ||
        p.currentPalletLocation.loc === PalletLocationEnum.MachineQueue,
    )
    .map(([_, p]) => p)
    .groupBy(
      (p) => p.currentPalletLocation.group,
      (p) => p.currentPalletLocation.num,
    )
    .toLookupOrderedMap(
      ([[statGroup, _statNum], _pals]) => statGroup,
      ([[_statGroup, statNum], _pals]) => statNum,
      ([[statGroup, statNum], pals]) => {
        const worktable = pals.find((p) => p.currentPalletLocation.loc === PalletLocationEnum.Machine);
        const worktableMats = worktable ? (matByPal.get(worktable.palletNum) ?? []) : null;
        const rotary = pals.find((p) => p.currentPalletLocation.loc === PalletLocationEnum.MachineQueue);
        const rotaryMats = rotary ? (matByPal.get(rotary.palletNum) ?? []) : null;

        let isInbound = true;
        const rotaryMat0 = rotaryMats?.[0];
        if (
          rotaryMat0 &&
          rotaryMat0.lastCompletedMachiningRouteStopIndex !== null &&
          rotaryMat0.lastCompletedMachiningRouteStopIndex !== undefined
        ) {
          const stop =
            currentSt.jobs[rotaryMat0.jobUnique]?.procsAndPaths?.[rotaryMat0.process - 1]?.paths?.[
              rotaryMat0.path - 1
            ]?.stops?.[rotaryMat0.lastCompletedMachiningRouteStopIndex];
          if (stop.stationGroup === statGroup && stop.stationNums.includes(statNum)) {
            isInbound = false;
          }
        }

        if (isInbound) {
          return {
            name: statGroup + " " + statNum.toString(),
            inbound: rotary ? { pallet: rotary, mats: rotaryMats ?? [] } : null,
            worktable: worktable ? { pallet: worktable, mats: worktableMats ?? [] } : null,
            outbound: null,
          };
        } else {
          return {
            name: statGroup + " " + statNum.toString(),
            inbound: null,
            worktable: worktable ? { pallet: worktable, mats: worktableMats ?? [] } : null,
            outbound: rotary ? { pallet: rotary, mats: rotaryMats ?? [] } : null,
          };
        }
      },
    );

  const stockerPals = LazySeq.ofObject(currentSt.pallets)
    .filter(
      ([_, p]) =>
        p.currentPalletLocation.loc === PalletLocationEnum.Buffer ||
        p.currentPalletLocation.loc === PalletLocationEnum.Cart,
    )
    .collect(([_, p]) => {
      const mats = matByPal.get(p.palletNum);
      if (!mats || mats.length === 0) return null;
      return { pallet: p, mats };
    })
    .toOrderedMap((p) => [p.pallet.palletNum, p]);

  let maxLoadNum = 1;
  let maxNumFacesOnPallet = 1;
  const cutoff = addDays(new Date(), -7);

  // now add empty locations
  const allPaths = jobs
    .valuesToLazySeq()
    .filter((j) => j.routeEndUTC > cutoff)
    .concat(LazySeq.ofObject(currentSt.jobs).map(([_, j]) => j))
    .flatMap((j) => j.procsAndPaths)
    .flatMap((p) => p.paths);

  for (const path of allPaths) {
    for (const lul of path.load.concat(path.unload)) {
      maxLoadNum = Math.max(maxLoadNum, lul);
    }

    if (path.face) {
      maxNumFacesOnPallet = Math.max(maxNumFacesOnPallet, path.face);
    }

    for (const stop of path.stops) {
      machines = machines.alter(stop.stationGroup, (stationStatuses) => {
        stationStatuses = stationStatuses ?? OrderedMap.empty();
        for (const num of stop.stationNums) {
          stationStatuses = stationStatuses.alter(num, (status) => {
            if (status) return status;
            return {
              name: stop.stationGroup + " " + num.toString(),
              inbound: null,
              worktable: null,
              outbound: null,
            };
          });
        }
        return stationStatuses;
      });
    }
  }

  for (let i = 1; i <= maxLoadNum; i++) {
    loads = loads.alter(i, (status) => {
      if (status) return status;
      return {
        lulNum: i,
        pal: null,
      };
    });
  }

  for (const pal of Object.values(currentSt.pallets)) {
    maxNumFacesOnPallet = Math.max(maxNumFacesOnPallet, pal.numFaces);
  }
  for (const mat of currentSt.material) {
    if (mat.location.face !== null && mat.location.face !== undefined) {
      maxNumFacesOnPallet = Math.max(maxNumFacesOnPallet, mat.location.face);
    }
  }

  let machAtLoad = OrderedMap.empty<number, MachineAtLoadStatus>();
  if (currentSt.machineLocations && currentSt.machineLocations.length > 0) {
    for (const mach of currentSt.machineLocations) {
      // remove machine status
      machines = machines.alter(mach.machineGroup, (old) => {
        if (!old || mach.machineNum === undefined) return old;
        const n = old.delete(mach.machineNum);
        if (n.size === 0) return undefined;
        return n;
      });

      for (const lulNum of mach.possibleLoadStations) {
        // remove and lookup old load status
        let lul: LoadStatus | undefined;
        loads = loads.alter(lulNum, (s) => {
          lul = s;
          return undefined;
        });

        const allMats = LazySeq.of(lul?.pal?.mats ?? []).toLookup((m) => {
          if (m.action.type === ActionType.Machining) return "Machining";
          if (m.action.type !== ActionType.Waiting) return "Loading";

          if (
            m.lastCompletedMachiningRouteStopIndex !== null &&
            m.lastCompletedMachiningRouteStopIndex !== undefined
          ) {
            const stop =
              currentSt.jobs[m.jobUnique]?.procsAndPaths?.[m.process - 1]?.paths?.[m.path - 1]?.stops?.[
                m.lastCompletedMachiningRouteStopIndex
              ];
            if (
              !stop ||
              (stop.stationGroup === mach.machineGroup && stop.stationNums.includes(mach.machineNum))
            ) {
              return "Loading";
            }
          }
          return "Ready";
        });

        const newStatus: MachineAtLoadStatus = {
          lulNum,
          machineMoving: mach.moving,
          machineCurrentlyAtLoad:
            lulNum === mach.currentLoadStation ? { group: mach.machineGroup, num: mach.machineNum } : null,
          readyMats: allMats.get("Ready") ?? [],
          machiningMats: allMats.get("Machining") ?? [],
          loadingMats: allMats.get("Loading") ?? [],
        };

        machAtLoad = machAtLoad.set(lulNum, newStatus);
      }
    }
  }

  return {
    machines: machines.mapValues((group) => group.valuesToAscLazySeq().toRArray()),
    loads: loads.valuesToAscLazySeq().toRArray(),
    stockerPals: stockerPals.valuesToAscLazySeq().toRArray(),
    machineAtLoad: machAtLoad.valuesToAscLazySeq().toRArray(),
    maxNumFacesOnPallet,
  };
}

function MaterialIcon({ mats }: { mats: ReadonlyArray<Readonly<IInProcessMaterial>> }) {
  const [open, setOpen] = useState<boolean>(false);
  const closeTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const btnRef = useRef<HTMLButtonElement | null>(null);
  const [menuOpen, setMenuOpen] = useState<boolean>(false);
  const setMatToShow = useSetAtom(materialDialogOpen);
  const curSt = useAtomValue(currentStatus);

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

  function faceName(pallet: number | null | undefined, faceNum: number | null | undefined): string | null {
    if (faceNum === null || faceNum === undefined) return null;
    const name =
      pallet != null && pallet != undefined ? curSt.pallets[pallet]?.faceNames?.[faceNum - 1] : null;
    return name ?? "Face: " + faceNum.toString();
  }

  return (
    <Box sx={{ width: CollapsedIconSize, height: CollapsedIconSize, overflow: "visible" }}>
      <Paper
        elevation={4}
        onPointerEnter={enter}
        onPointerLeave={leave}
        sx={{ position: "relative", zIndex: open ? 10 : 0, width: "max-content", height: "max-content" }}
      >
        <Badge badgeContent={mats.length > 1 ? mats.length : 0} color="secondary">
          <ButtonBase focusRipple onClick={click} ref={btnRef}>
            <Collapse orientation="horizontal" in={open} collapsedSize={CollapsedIconSize}>
              <Box display="flex">
                <PartIdenticon part={mats[0].partName} size={CollapsedIconSize} />
                <Box marginLeft="10px" marginRight="10px" whiteSpace="nowrap" textAlign="left">
                  <Collapse in={open} collapsedSize={CollapsedIconSize}>
                    <Stack direction="column" marginBottom="0.2em">
                      <Typography variant="h6">
                        {mats[0].partName}-{mats[0].process}
                      </Typography>
                      <div>
                        <small>
                          {faceName(mats[0].location.palletNum, mats[0].location.face) ??
                            faceName(mats[0].action.loadOntoPalletNum, mats[0].action.loadOntoFace)}
                        </small>
                      </div>
                      {LazySeq.of(mats).collect((mat) =>
                        mat.serial ? (
                          <div key={mat.materialID}>
                            <small>Serial: {mat.serial}</small>
                          </div>
                        ) : undefined,
                      )}
                      <div>
                        <MaterialAction mat={mats[0]} />
                      </div>
                    </Stack>
                  </Collapse>
                </Box>
              </Box>
            </Collapse>
          </ButtonBase>
        </Badge>
      </Paper>
      <Menu
        anchorEl={btnRef.current}
        open={menuOpen}
        onClose={() => setMenuOpen(false)}
        anchorOrigin={{ vertical: "top", horizontal: "left" }}
      >
        {LazySeq.of(mats).map((mat, idx) => (
          <MenuItem
            key={mat.materialID}
            onClick={() => {
              setMenuOpen(false);
              setMatToShow({ type: "MatDetails", details: mat });
            }}
          >
            {mat.serial && mat.serial !== "" ? mat.serial : (idx + 1).toString()}
          </MenuItem>
        ))}
      </Menu>
    </Box>
  );
}

function PalletFaces({
  maxNumFaces,
  mats,
  loadingOntoPallet,
  noFilter,
  showExpanded,
}: {
  maxNumFaces: number;
  mats: ReadonlyArray<Readonly<IInProcessMaterial>>;
  loadingOntoPallet?: boolean;
  noFilter?: boolean;
  showExpanded?: boolean;
}) {
  if (showExpanded && maxNumFaces === 1) {
    return (
      <Box display="flex" flexDirection="column" flexWrap="wrap">
        {mats.map((mat) => (
          <InProcMaterial key={mat.materialID} mat={mat} />
        ))}
      </Box>
    );
  } else {
    const byFace = loadingOntoPallet
      ? LazySeq.of(mats)
          .filter((m) => noFilter || m.location.type !== LocType.OnPallet)
          .orderedGroupBy((m) => m.action.loadOntoFace ?? 1)
      : LazySeq.of(mats)
          .filter((m) => noFilter || m.location.type === LocType.OnPallet)
          .orderedGroupBy((m) =>
            m.action.type === ActionType.Loading ? (m.action.loadOntoFace ?? 1) : (m.location.face ?? 1),
          );

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
}

function gridTemplateColumns(maxNumFaces: number, includeLabelCol: boolean) {
  // each material column is at least CollapsedIconSize * (maxNumFaces) for the icon + 5px * (maxNumFaces + 1) for the columnGap in PalletFaces
  const colSize = CollapsedIconSize * maxNumFaces + 5 * (maxNumFaces + 1);
  if (includeLabelCol) {
    return `60px ${Math.max(colSize, 100)}px`;
  } else {
    return `${Math.max(colSize, 100)}px`;
  }
}

export function formatSeconds(totalSeconds: number): string {
  totalSeconds = Math.round(totalSeconds);
  const secs = Math.abs(totalSeconds) % 60;
  const totalMins = Math.floor(Math.abs(totalSeconds) / 60);
  const mins = totalMins % 60;
  const hours = Math.floor(totalMins / 60);
  if (hours > 0) {
    return `${totalSeconds < 0 ? "-" : ""}${hours}:${mins.toString().padStart(2, "0")}:${secs
      .toString()
      .padStart(2, "0")}`;
  } else {
    return `${totalSeconds < 0 ? "-" : ""}${mins}:${secs.toString().padStart(2, "0")}`;
  }
}

function useRemainingMachineTime(
  material: ReadonlyArray<Readonly<IInProcessMaterial>> | null | undefined,
): [boolean, string, number | null] {
  const mat = material?.find((m) => m.action.type === ActionType.Machining);
  const elapsedDurationFromCurSt = mat?.action?.elapsedMachiningTime;
  const remainingDurationFromCurSt = mat?.action.expectedRemainingMachiningTime;
  const currentStTime = useAtomValue(currentStatus).timeOfCurrentStatusUTC;
  const secondsSinceEpoch = useAtomValue(secondsSinceEpochAtom);

  let remainingSecs: number | null = null;
  if (remainingDurationFromCurSt) {
    const remainingSecsInCurSt = durationToSeconds(remainingDurationFromCurSt);
    remainingSecs = remainingSecsInCurSt - (secondsSinceEpoch - Math.floor(currentStTime.getTime() / 1000));
  }

  let elapsedSecs: number | null = null;
  if (elapsedDurationFromCurSt) {
    const elapsedSecsInCurSt = durationToSeconds(elapsedDurationFromCurSt);
    elapsedSecs = elapsedSecsInCurSt + (secondsSinceEpoch - Math.floor(currentStTime.getTime() / 1000));
  }

  if (remainingSecs !== null && elapsedSecs !== null) {
    return [
      true,
      `${formatSeconds(elapsedSecs)} / ${formatSeconds(remainingSecs)}`,
      remainingSecs < 0 ? -1 : (elapsedSecs / (elapsedSecs + remainingSecs)) * 100,
    ];
  } else if (remainingSecs !== null) {
    return [true, " / " + formatSeconds(remainingSecs), remainingSecs < 0 ? -1 : null];
  } else if (elapsedSecs !== null) {
    return [true, formatSeconds(elapsedSecs), null];
  } else {
    return [!!mat, "Idle", null];
  }
}

function MachineLabel({ machine }: { machine: MachineStatus }) {
  const [machining, status, elapsed] = useRemainingMachineTime(machine.worktable?.mats);
  return (
    <div>
      <Typography variant="h5">{machine.name}</Typography>
      <Typography variant="subtitle1">{status}</Typography>
      {machining ? (
        elapsed === null ? (
          <LinearProgress />
        ) : elapsed < 0 ? (
          <LinearProgress color="error" />
        ) : (
          <LinearProgress variant="determinate" value={elapsed} />
        )
      ) : undefined}
    </div>
  );
}

function Machine({ maxNumFaces, machine }: { maxNumFaces: number; machine: MachineStatus }) {
  return (
    <Box
      display="grid"
      border="1px solid black"
      margin="5px"
      gridTemplateRows={`auto ${rowSize}px ${rowSize}px ${rowSize}px`}
      gridTemplateColumns={gridTemplateColumns(maxNumFaces, true)}
      gridTemplateAreas={`"machname machname" "inboundpal inboundmat" "worktablepal worktablemat" "outboundpal outboundmat"`}
    >
      <Box gridArea="machname" padding="0.2em" borderBottom="1px solid black">
        <MachineLabel machine={machine} />
      </Box>
      <Box gridArea="inboundpal" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            In
          </Typography>
          {machine.inbound ? (
            <Typography variant="h6" textAlign="center">
              {machine.inbound.pallet.palletNum}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="inboundmat" borderBottom="1px solid black">
        {machine.inbound ? <PalletFaces mats={machine.inbound.mats} maxNumFaces={maxNumFaces} /> : undefined}
      </Box>
      <Box gridArea="worktablepal" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Work
          </Typography>
          {machine.worktable ? (
            <Typography variant="h6" textAlign="center">
              {machine.worktable.pallet.palletNum}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="worktablemat" borderBottom="1px solid black">
        {machine.worktable ? (
          <PalletFaces mats={machine.worktable.mats} maxNumFaces={maxNumFaces} />
        ) : undefined}
      </Box>
      <Box gridArea="outboundpal" borderRight="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Out
          </Typography>
          {machine.outbound ? (
            <Typography variant="h6" textAlign="center">
              {machine.outbound.pallet.palletNum}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="outboundmat">
        {machine.outbound ? (
          <PalletFaces mats={machine.outbound.mats} maxNumFaces={maxNumFaces} />
        ) : undefined}
      </Box>
    </Box>
  );
}

function useElapsedLoadTime(
  material: ReadonlyArray<Readonly<IInProcessMaterial>> | null | undefined,
): string {
  const mat = material?.find(
    (m) =>
      m.action.type === ActionType.Loading ||
      m.action.type === ActionType.UnloadToCompletedMaterial ||
      m.action.type === ActionType.UnloadToInProcess,
  );
  const elapsedDurationFromCurSt = mat?.action?.elapsedLoadUnloadTime;
  const currentStTime = useAtomValue(currentStatus).timeOfCurrentStatusUTC;
  const secondsSinceEpoch = useAtomValue(secondsSinceEpochAtom);

  let elapsedSecs: number | null = null;
  if (elapsedDurationFromCurSt) {
    const elapsedSecsInCurSt = durationToSeconds(elapsedDurationFromCurSt);
    elapsedSecs = elapsedSecsInCurSt + (secondsSinceEpoch - Math.floor(currentStTime.getTime() / 1000));
  }

  if (elapsedSecs !== null) {
    return formatSeconds(elapsedSecs);
  } else {
    return "Idle";
  }
}

function LoadStationLabel({ load }: { load: LoadStatus }) {
  const status = useElapsedLoadTime(load.pal?.mats);
  return (
    <Box display="flex" justifyContent="space-between" alignItems="baseline">
      <Typography variant="h5">L/U {load.lulNum}</Typography>
      <Typography variant="body1">{status}</Typography>
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
      gridTemplateColumns={gridTemplateColumns(maxNumFaces, true)}
      gridTemplateAreas={`"lulname lulname" "loading loadingmat" "currentpal currentmat"`}
    >
      <Box gridArea="lulname" padding="0.2em" borderBottom="1px solid black">
        <LoadStationLabel load={load} />
      </Box>
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
        {load.pal ? (
          <PalletFaces mats={load.pal.mats} maxNumFaces={maxNumFaces} loadingOntoPallet />
        ) : undefined}
      </Box>
      <Box gridArea="currentpal" borderRight="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Pallet
          </Typography>
          {load.pal ? (
            <Typography variant="h6" textAlign="center">
              {load.pal.pallet.palletNum}
            </Typography>
          ) : undefined}
        </Stack>
      </Box>
      <Box gridArea="currentmat">
        {load.pal ? <PalletFaces mats={load.pal.mats} maxNumFaces={maxNumFaces} /> : undefined}
      </Box>
    </Box>
  );
}

function MachineAtLoadLabel({ status }: { status: MachineAtLoadStatus }) {
  const lulStatus = useElapsedLoadTime(status.loadingMats);
  const [machining, mcStatus, mcElapsed] = useRemainingMachineTime(status.machiningMats);

  return (
    <div>
      <Typography variant="h5">Station {status.lulNum}</Typography>
      {status.machineCurrentlyAtLoad ? (
        <>
          <Typography variant="h5">
            {status.machineCurrentlyAtLoad.group} {status.machineCurrentlyAtLoad.num}
          </Typography>
          <Typography variant="subtitle1">{mcStatus === "Idle" ? lulStatus : mcStatus}</Typography>
          {machining ? (
            mcElapsed === null ? (
              <LinearProgress />
            ) : mcElapsed < 0 ? (
              <LinearProgress color="error" />
            ) : (
              <LinearProgress variant="determinate" value={mcElapsed} />
            )
          ) : undefined}
        </>
      ) : (
        <>
          {status.machineMoving ? <Typography variant="subtitle2">Machine Moving</Typography> : undefined}
          <Typography variant="subtitle1">Loading {lulStatus}</Typography>
        </>
      )}
    </div>
  );
}

function MachineAtLoad({ maxNumFaces, status }: { maxNumFaces: number; status: MachineAtLoadStatus }) {
  return (
    <Box
      display="grid"
      border="1px solid black"
      margin="5px"
      gridTemplateRows={`minmax(104px, max-content) repeat(3, ${
        maxNumFaces > 1 ? rowSize.toString() + "px" : "minmax(110px, max-content)"
      })`}
      gridTemplateColumns={
        maxNumFaces === 1 ? "60px minmax(230px, max-content)" : gridTemplateColumns(maxNumFaces, true)
      }
      gridTemplateAreas={`"name name" "ready readymat" "machining machiningmat" "loadstation loadstationmat"`}
    >
      <Box gridArea="name" padding="0.2em" borderBottom="1px solid black">
        <MachineAtLoadLabel status={status} />
      </Box>
      <Box gridArea="ready" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Typography variant="body1" textAlign="center">
          Ready
        </Typography>
      </Box>
      <Box
        gridArea="readymat"
        borderBottom="1px solid black"
        display="flex"
        flexDirection="column"
        justifyContent="center"
      >
        <PalletFaces mats={status.readyMats} maxNumFaces={maxNumFaces} showExpanded />
      </Box>
      <Box gridArea="machining" borderRight="1px solid black" borderBottom="1px solid black" padding="2px">
        <Typography variant="body1" textAlign="center">
          Work
        </Typography>
      </Box>
      <Box
        gridArea="machiningmat"
        borderBottom="1px solid black"
        display="flex"
        flexDirection="column"
        justifyContent="center"
      >
        <PalletFaces mats={status.machiningMats} maxNumFaces={maxNumFaces} showExpanded />
      </Box>
      <Box gridArea="loadstation" borderRight="1px solid black" padding="2px">
        <Stack>
          <Typography variant="body1" textAlign="center">
            Load
          </Typography>
          <Typography variant="body1" textAlign="center">
            Station
          </Typography>
        </Stack>
      </Box>
      <Box gridArea="loadstationmat" display="flex" flexDirection="column" justifyContent="center">
        <PalletFaces mats={status.loadingMats} maxNumFaces={maxNumFaces} showExpanded noFilter />
      </Box>
    </Box>
  );
}

function StockerPallet({ maxNumFaces, pallet }: { maxNumFaces: number; pallet: PalletAndMaterial }) {
  return (
    <Box
      display="grid"
      border="1px solid black"
      margin="5px"
      gridTemplateRows={`auto ${rowSize}px`}
      gridTemplateColumns={gridTemplateColumns(maxNumFaces, false)}
      gridTemplateAreas={`"palname" "palmat"`}
    >
      <Typography variant="h5" gridArea="palname" padding="0.2em" borderBottom="1px solid black">
        Pallet {pallet.pallet.palletNum}
      </Typography>
      <Box gridArea="palmat">
        <PalletFaces mats={pallet.mats} maxNumFaces={maxNumFaces} />
      </Box>
    </Box>
  );
}

export const SystemOverview = memo(function SystemOverview({ overview }: { overview: CellOverview }) {
  return (
    <div>
      {overview.machines.toAscLazySeq().map(([group, machines]) => (
        <Box key={group} display="flex" flexWrap="wrap" justifyContent="space-evenly">
          {machines.map((machine) => (
            <Machine key={machine.name} machine={machine} maxNumFaces={overview.maxNumFacesOnPallet} />
          ))}
        </Box>
      ))}
      {overview.loads.length > 0 ? (
        <Box display="flex" flexWrap="wrap" justifyContent="space-evenly">
          {overview.loads.map((machine) => (
            <LoadStation key={machine.lulNum} load={machine} maxNumFaces={overview.maxNumFacesOnPallet} />
          ))}
        </Box>
      ) : undefined}
      {overview.machineAtLoad.length > 0 ? (
        <Box display="flex" flexWrap="wrap" justifyContent="space-evenly">
          {overview.machineAtLoad.map((status) => (
            <MachineAtLoad key={status.lulNum} status={status} maxNumFaces={overview.maxNumFacesOnPallet} />
          ))}
        </Box>
      ) : undefined}
      {overview.stockerPals.length > 0 ? (
        <Box display="flex" flexWrap="wrap" justifyContent="space-evenly">
          {overview.stockerPals.map((pal) => (
            <StockerPallet
              key={pal.pallet.palletNum}
              pallet={pal}
              maxNumFaces={overview.maxNumFacesOnPallet}
            />
          ))}
        </Box>
      ) : undefined}
    </div>
  );
});

const SystemOverviewMaterialDialog = memo(function SystemOverviewMaterialDialog({
  ignoreOperator,
}: {
  ignoreOperator?: boolean;
}) {
  const [swapSt, setSwapSt] = useState<SwapMaterialState>(null);
  const [invalidateSt, setInvalidateSt] = useState<InvalidateCycleState | null>(null);

  function onClose() {
    setSwapSt(null);
    setInvalidateSt(null);
  }

  return (
    <MaterialDialog
      onClose={onClose}
      allowNote
      highlightProcsGreaterOrEqualTo={invalidateSt?.process ?? undefined}
      extraDialogElements={
        <>
          <SwapMaterialDialogContent st={swapSt} setState={setSwapSt} />
          {invalidateSt !== null ? (
            <InvalidateCycleDialogContent st={invalidateSt} setState={setInvalidateSt} />
          ) : null}
        </>
      }
      buttons={
        <>
          <QuarantineMatButton ignoreOperator={ignoreOperator} />
          <SignalInspectionButton />
          <SwapMaterialButtons
            st={swapSt}
            setState={setSwapSt}
            onClose={onClose}
            ignoreOperator={ignoreOperator}
          />
          <InvalidateCycleDialogButton
            st={invalidateSt}
            setState={setInvalidateSt}
            onClose={onClose}
            ignoreOperator={ignoreOperator}
          />
        </>
      }
    />
  );
});

export function SystemOverviewPage({
  ignoreOperator,
  whiteBackground,
}: {
  ignoreOperator?: boolean;
  whiteBackground?: boolean;
}) {
  useSetTitle("System Overview");
  const overview = useCellOverview();

  return (
    <main
      style={{
        padding: "10px",
        minHeight: "calc(100vh - 64px)",
        backgroundColor: whiteBackground ? "white" : "#F8F8F8",
      }}
    >
      <SystemOverview overview={overview} />
      <SystemOverviewMaterialDialog ignoreOperator={ignoreOperator} />
      <SelectInspTypeDialog />
    </main>
  );
}

const StatusIconSize = 30;

function MachineIcon({ machine }: { machine: MachineStatus }) {
  const theme = useTheme();
  const [, , elapsed] = useRemainingMachineTime(machine.worktable?.mats);
  return (
    <>
      <rect x={3} y={3} width={24} height={24} stroke="black" />;
      <rect
        x={3}
        y={3}
        width={24}
        height={8}
        fill={machine.inbound === null ? "white" : theme.palette.secondary.main}
      />
      <rect
        x={3}
        y={11}
        width={24}
        height={8}
        fill={
          machine.worktable === null
            ? "white"
            : elapsed != null && elapsed < 0
              ? theme.palette.error.main
              : theme.palette.secondary.main
        }
      />
      <rect
        x={3}
        y={19}
        width={24}
        height={8}
        fill={machine.outbound === null ? "white" : theme.palette.secondary.main}
      />
    </>
  );
}

function LoadStationIcon({ load }: { load: LoadStatus }) {
  const theme = useTheme();
  return (
    <polygon
      points="3,27 27,27 15,3"
      fill={load.pal === null ? "white" : theme.palette.secondary.main}
      stroke="black"
    />
  );
}

function MachineAtLoadIcon({ status }: { status: MachineAtLoadStatus }) {
  const theme = useTheme();
  const [, , elapsed] = useRemainingMachineTime(status.machiningMats);
  return (
    <>
      <rect x={3} y={3} width={24} height={24} stroke="black" />;
      <rect
        x={3}
        y={3}
        width={24}
        height={8}
        fill={status.readyMats.length === 0 ? "white" : theme.palette.secondary.main}
      />
      <rect
        x={3}
        y={11}
        width={24}
        height={8}
        fill={
          status.machiningMats.length === 0
            ? "white"
            : elapsed != null && elapsed < 0
              ? theme.palette.error.main
              : theme.palette.secondary.main
        }
      />
      <rect
        x={3}
        y={19}
        width={24}
        height={8}
        fill={status.loadingMats.length === 0 ? "white" : theme.palette.secondary.main}
      />
    </>
  );
}

const StatusIcons = memo(function StatusIcons({ overview }: { overview: CellOverview }) {
  const numMachines = overview.machines.toAscLazySeq().sumBy(([, machines]) => machines.length);
  return (
    <Box
      sx={{
        height: "1.5em",
        "&:hover": {
          backgroundColor: "primary.light",
        },
      }}
    >
      <svg
        viewBox={`0 0 ${
          (numMachines + overview.loads.length + overview.machineAtLoad.length) * StatusIconSize
        } ${StatusIconSize}`}
        preserveAspectRatio="none"
        width="140px"
        height="1.5em"
      >
        <g>
          {overview.machines
            .toAscLazySeq()
            .flatMap(([, machines]) => machines)
            .map((machine, idx) => (
              <g key={machine.name} transform={`translate(${idx * StatusIconSize})`}>
                <MachineIcon machine={machine} />
              </g>
            ))}
        </g>
        <g transform={`translate(${numMachines * StatusIconSize})`}>
          {overview.loads.map((load, idx) => (
            <g key={load.lulNum} transform={`translate(${idx * StatusIconSize})`}>
              <LoadStationIcon load={load} />
            </g>
          ))}
        </g>
        <g transform={`translate(${(numMachines + overview.loads.length) * StatusIconSize})`}>
          {overview.machineAtLoad.map((status, idx) => (
            <g key={status.lulNum} transform={`translate(${idx * StatusIconSize})`}>
              <MachineAtLoadIcon status={status} />
            </g>
          ))}
        </g>
      </svg>
    </Box>
  );
});

export const SystemOverviewDialogButton = memo(function SystemOverviewDialogButton({
  full,
}: {
  full: boolean;
}) {
  const [open, setOpen] = useState(false);
  const overview = useCellOverview();

  return (
    <>
      <Button onClick={() => setOpen(true)}>
        <StatusIcons overview={overview} />
      </Button>
      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="xl" fullScreen={full}>
        {full ? (
          <AppBar sx={{ position: "relative" }}>
            <Toolbar>
              <IconButton edge="start" color="inherit" onClick={() => setOpen(false)} aria-label="close">
                <CloseIcon />
              </IconButton>
              <Typography sx={{ ml: 2, flex: 1 }} variant="h6" component="div">
                System Overview
              </Typography>
            </Toolbar>
          </AppBar>
        ) : undefined}
        <DialogContent>
          <Box paddingBottom="2em" paddingLeft="5em" paddingRight="5em">
            <SystemOverview overview={overview} />
          </Box>
        </DialogContent>
      </Dialog>
    </>
  );
});
