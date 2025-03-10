/* Copyright (c) 2021, John Lenz

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
import { useState, memo } from "react";
import { Box, Stack } from "@mui/material";
import { Table } from "@mui/material";
import { TableBody } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableRow } from "@mui/material";
import { TextField } from "@mui/material";
import { ImportExport } from "@mui/icons-material";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { LazySeq, OrderedMap } from "@seedtactics/immutable-collections";
import {
  MachineCostPerYear,
  copyCostPerPieceToClipboard,
  copyCostBreakdownToClipboard,
  compute_monthly_cost_percentages,
  convert_cost_percent_to_cost_per_piece,
} from "../../data/cost-per-piece.js";
import { startOfToday, addDays } from "date-fns";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { PartIdenticon } from "../station-monitor/Material.js";
import { Typography } from "@mui/material";
import { last30MaterialSummary, specificMonthMaterialSummary } from "../../cell-status/material-summary.js";
import { last30StationCycles, specificMonthStationCycles } from "../../cell-status/station-cycles.js";
import { useSetTitle } from "../routes.js";
import { atom, useAtom, useAtomValue } from "jotai";
import { atomWithStorage, atomFamily } from "jotai/utils";
import { last30Jobs, specificMonthJobs } from "../../cell-status/scheduled-jobs.js";
import { durationToSeconds } from "../../util/parseISODuration.js";

const machineCostPerYear = atomWithStorage<MachineCostPerYear>("MachineCostPerYear", {});
const automationCostPerYear = atomWithStorage<number | null>("AutomationCostPerYear", null);
const last30LaborCost = atomWithStorage<number | null>("Last30LaborCost", null);
const perMonthLaborCost = atomFamily((month: Date) =>
  atomWithStorage<number | null>(
    "PerMonthLabor-" + month.getFullYear().toString() + "-" + month.getMonth().toString(),
    null,
  ),
);

const artifactParts = atom((get) => {
  const period = get(selectedAnalysisPeriod);
  const jobs = get(period.type === "Last30" ? last30Jobs : specificMonthJobs);
  return jobs
    .valuesToLazySeq()
    .filter(
      (j) =>
        j.procsAndPaths.length === 1 &&
        j.procsAndPaths[0].paths.length === 1 &&
        j.procsAndPaths[0].paths[0].stops.length === 1 &&
        durationToSeconds(j.procsAndPaths[0].paths[0].stops[0].expectedCycleTime) === 0,
    )
    .toHashSet((j) => j.partName);
});

const costPercentages = atom((get) => {
  const period = get(selectedAnalysisPeriod);

  const cycles = get(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const matIds = get(period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary);
  const artifacts = get(artifactParts);

  const thirtyDaysAgo = addDays(startOfToday(), -30);
  const month = period.type === "Last30" ? null : period.month;

  return compute_monthly_cost_percentages(
    cycles.valuesToLazySeq(),
    artifacts,
    matIds.matsById,
    month ? { month: month } : { thirtyDaysAgo },
  );
});

const computedCosts = atom((get) => {
  const period = get(selectedAnalysisPeriod);

  const machineCosts = get(machineCostPerYear);
  const automationCosts = get(automationCostPerYear);
  const laborCosts = get(period.type === "Last30" ? last30LaborCost : perMonthLaborCost(period.month));

  const costPcts = get(costPercentages);
  return convert_cost_percent_to_cost_per_piece(costPcts, machineCosts, automationCosts, laborCosts ?? 0);
});

function AutomationCostInput() {
  const [cost, setCost] = useState<number | null>(null);
  const [automationCost, saveAutomationCostPerYear] = useAtom(automationCostPerYear);

  return (
    <TextField
      id="auotmation-cost-year"
      type="number"
      label="Cost for automated handling system per year"
      style={{ marginTop: "1.5em" }}
      inputProps={{ min: 0 }}
      variant="outlined"
      value={cost === null ? (automationCost ?? "") : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = isNaN(cost) ? null : cost;
          saveAutomationCostPerYear(newCost);
        }
        setCost(null);
      }}
    />
  );
}

function LaborCost() {
  const period = useAtomValue(selectedAnalysisPeriod);
  const month = period.type === "Last30" ? null : period.month;
  const [cost, setCost] = useState<number | null>(null);
  const [laborCost, saveCost] = useAtom(month === null ? last30LaborCost : perMonthLaborCost(month));

  return (
    <TextField
      type="number"
      label={
        "Total labor cost for " +
        (month === null
          ? "last 30 days"
          : month.toLocaleDateString(undefined, { month: "long", year: "numeric" }))
      }
      inputProps={{ min: 0 }}
      variant="outlined"
      value={cost === null ? (laborCost ?? "") : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = isNaN(cost) ? null : cost;
          saveCost(newCost);
        }
        setCost(null);
      }}
    />
  );
}

interface StationCostInputProps {
  readonly machineQuantities: OrderedMap<string, number>;
}

function SingleStationCostInput(props: StationCostInputProps & { readonly machineGroup: string }) {
  const [cost, setCost] = useState<number | null>(null);
  const [machineCost, saveCost] = useAtom(machineCostPerYear);
  return (
    <TextField
      type="number"
      inputProps={{ min: 0 }}
      variant="outlined"
      label={"Cost for " + props.machineGroup + " per station per year"}
      style={{ marginTop: "1.5em" }}
      value={cost === null ? (machineCost[props.machineGroup] ?? "") : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = { ...machineCost };
          if (isNaN(cost)) {
            delete newCost[props.machineGroup];
          } else {
            newCost[props.machineGroup] = cost;
          }
          saveCost(newCost);
        }
        setCost(null);
      }}
    />
  );
}

function StationCostInputs() {
  const costs = useAtomValue(computedCosts);
  return (
    <>
      {costs.machineQuantities.keysToAscLazySeq().map((s, idx) => (
        <SingleStationCostInput key={idx} machineQuantities={costs.machineQuantities} machineGroup={s} />
      ))}
    </>
  );
}

const decimalFormat = Intl.NumberFormat(undefined, {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});
const pctFormat = new Intl.NumberFormat(undefined, {
  style: "percent",
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

export function CostBreakdownPage() {
  useSetTitle("Cost Percentages");
  const costs = useAtomValue(costPercentages);

  return (
    <Box paddingLeft="24px" paddingRight="24px" paddingTop="10px">
      <Box
        component="nav"
        sx={{
          display: "flex",
          minHeight: "2.5em",
          alignItems: "center",
          maxWidth: "calc(100vw - 24px - 24px)",
        }}
      >
        <Typography variant="subtitle1">Part Cost Percentage Breakdown</Typography>
        <Box flexGrow={1} />
        <Tooltip title="Copy to Clipboard">
          <IconButton
            style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
            onClick={() => copyCostBreakdownToClipboard(costs)}
            size="large"
          >
            <ImportExport />
          </IconButton>
        </Tooltip>
      </Box>
      <main>
        <Table stickyHeader>
          <TableHead>
            <TableRow>
              <TableCell>Part</TableCell>
              <TableCell align="right">Completed Quantity</TableCell>
              {costs.machineQuantities.keysToAscLazySeq().map((m) => (
                <TableCell align="right" key={m}>
                  {m} Cost %
                </TableCell>
              ))}
              <TableCell align="right">Labor Cost %</TableCell>
              <TableCell align="right">Automation Cost %</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {LazySeq.of(costs.parts)
              .sortBy((c) => c.part)
              .map((c, idx) => (
                <TableRow key={idx}>
                  <TableCell>
                    <Box
                      sx={{
                        display: "flex",
                        alignItems: "center",
                      }}
                    >
                      <Box sx={{ mr: "0.2em" }}>
                        <PartIdenticon part={c.part} size={25} />
                      </Box>
                      <div>
                        <Typography variant="body2" component="span" display="block">
                          {c.part}
                        </Typography>
                      </div>
                    </Box>
                  </TableCell>
                  <TableCell align="right">{c.parts_completed}</TableCell>
                  {costs.machineQuantities.keysToAscLazySeq().map((m) => (
                    <TableCell align="right" key={m}>
                      {pctFormat.format(c.machine.get(m) ?? 0)}
                    </TableCell>
                  ))}
                  <TableCell align="right">{pctFormat.format(c.labor)}</TableCell>
                  <TableCell align="right">{pctFormat.format(c.automation)}</TableCell>
                </TableRow>
              ))}
          </TableBody>
        </Table>
      </main>
    </Box>
  );
}

function CostOutputCard() {
  const costs = useAtomValue(computedCosts);
  return (
    <Table stickyHeader>
      <TableHead>
        <TableRow>
          <TableCell>Part</TableCell>
          <TableCell align="right">Completed Quantity</TableCell>
          {costs.machineQuantities.keysToAscLazySeq().map((m) => (
            <TableCell align="right" key={m}>
              {m} Cost
            </TableCell>
          ))}
          <TableCell align="right">Labor Cost</TableCell>
          <TableCell align="right">Automation Cost</TableCell>
          <TableCell align="right">
            <span>Total</span>
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyCostPerPieceToClipboard(costs)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {LazySeq.of(costs.parts)
          .sortBy((c) => c.part)
          .map((c, idx) => (
            <TableRow key={idx}>
              <TableCell>
                <Box
                  sx={{
                    display: "flex",
                    alignItems: "center",
                  }}
                >
                  <Box sx={{ mr: "0.2em" }}>
                    <PartIdenticon part={c.part} size={25} />
                  </Box>
                  <div>
                    <Typography variant="body2" component="span" display="block">
                      {c.part}
                    </Typography>
                  </div>
                </Box>
              </TableCell>
              <TableCell align="right">{c.parts_completed}</TableCell>
              {costs.machineQuantities.keysToAscLazySeq().map((m) => (
                <TableCell align="right" key={m}>
                  {decimalFormat.format(c.machine.get(m) ?? 0)}
                </TableCell>
              ))}
              <TableCell align="right">{decimalFormat.format(c.labor)}</TableCell>
              <TableCell align="right">{decimalFormat.format(c.automation)}</TableCell>
              <TableCell align="right">
                {decimalFormat.format(
                  c.machine.valuesToAscLazySeq().sumBy((x) => x) + c.labor + c.automation,
                )}
              </TableCell>
            </TableRow>
          ))}
      </TableBody>
    </Table>
  );
}

export const CostPerPiecePage = memo(function CostPerPiecePage() {
  useSetTitle("Cost Per Piece");

  return (
    <Box component="main" paddingLeft="24px" paddingRight="24px" paddingTop="20px">
      <Stack direction="column" spacing={4}>
        <Stack direction="column" spacing={2}>
          <LaborCost />
          <AutomationCostInput />
          <StationCostInputs />
        </Stack>
        <CostOutputCard />
      </Stack>
    </Box>
  );
});
