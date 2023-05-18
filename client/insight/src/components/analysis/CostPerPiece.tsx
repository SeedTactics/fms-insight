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
import * as React from "react";
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
import * as localForage from "localforage";
import { CircularProgress } from "@mui/material";
import {
  MachineCostPerYear,
  copyCostPerPieceToClipboard,
  CostData,
  copyCostBreakdownToClipboard,
  compute_monthly_cost_percentages,
  convert_cost_percent_to_cost_per_piece,
} from "../../data/cost-per-piece.js";
import { format, startOfToday, addDays } from "date-fns";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import { PartIdenticon } from "../station-monitor/Material.js";
import { Typography } from "@mui/material";
import { useRecoilValue } from "recoil";
import { last30MaterialSummary, specificMonthMaterialSummary } from "../../cell-status/material-summary.js";
import { last30StationCycles, specificMonthStationCycles } from "../../cell-status/station-cycles.js";

async function loadMachineCostPerYear(): Promise<MachineCostPerYear> {
  return (await localForage.getItem("MachineCostPerYear")) ?? {};
}

async function saveMachineCostPerYear(m: MachineCostPerYear): Promise<void> {
  await localForage.setItem("MachineCostPerYear", m);
}

async function loadAutomationCostPerYear(): Promise<number | null> {
  return await localForage.getItem("AutomationCostPerYear");
}

async function saveAutomationCostPerYear(n: number | null): Promise<void> {
  await localForage.setItem("AutomationCostPerYear", n);
}

async function loadLast30LaborCost(): Promise<number | null> {
  return await localForage.getItem("Last30TotalLaborCost");
}

async function saveLast30LaborCost(c: number | null): Promise<void> {
  await localForage.setItem("Last30TotalLaborCost", c);
}

async function loadPerMonthLabor(month: Date): Promise<number | null> {
  const key = "PerMonthLabor-" + month.getFullYear().toString() + "-" + month.getMonth().toString();
  return await localForage.getItem(key);
}

async function savePerMonthLabor(month: Date, cost: number | null): Promise<void> {
  const key = "PerMonthLabor-" + month.getFullYear().toString() + "-" + month.getMonth().toString();
  await localForage.setItem(key, cost);
}

interface AutomationCostInputProps {
  readonly automationCostPerYear: number | null;
  readonly setAutomationCostPerYear: (n: number | null) => void;
}

function AutomationCostInput(props: AutomationCostInputProps) {
  const [cost, setCost] = React.useState<number | null>(null);

  return (
    <TextField
      id="auotmation-cost-year"
      type="number"
      label="Cost for automated handling system per year"
      style={{ marginTop: "1.5em" }}
      inputProps={{ min: 0 }}
      variant="outlined"
      value={cost === null ? props.automationCostPerYear ?? "" : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = isNaN(cost) ? null : cost;
          void saveAutomationCostPerYear(newCost);
          props.setAutomationCostPerYear(newCost);
        }
        setCost(null);
      }}
    />
  );
}

interface LaborCostProps {
  readonly month: Date | null;
  readonly laborCost: number | null;
  readonly setLaborCost: (n: number | null) => void;
}

function LaborCost(props: LaborCostProps) {
  const [cost, setCost] = React.useState<number | null>(null);

  return (
    <TextField
      type="number"
      label={
        "Total labor cost for " + (props.month === null ? "last 30 days" : format(props.month, "MMMM yyyy"))
      }
      inputProps={{ min: 0 }}
      variant="outlined"
      value={cost === null ? props.laborCost ?? "" : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = isNaN(cost) ? null : cost;
          if (props.month === null) {
            void saveLast30LaborCost(newCost);
          } else {
            void savePerMonthLabor(props.month, newCost);
          }
          props.setLaborCost(newCost);
        }
        setCost(null);
      }}
    />
  );
}

interface StationCostInputProps {
  readonly machineQuantities: OrderedMap<string, number>;
  readonly machineCostPerYear: MachineCostPerYear;
  readonly setMachineCostPerYear: (m: MachineCostPerYear) => void;
}

function SingleStationCostInput(props: StationCostInputProps & { readonly machineGroup: string }) {
  const [cost, setCost] = React.useState<number | null>(null);
  return (
    <TextField
      type="number"
      inputProps={{ min: 0 }}
      variant="outlined"
      label={"Cost for " + props.machineGroup + " per station per year"}
      style={{ marginTop: "1.5em" }}
      value={cost === null ? props.machineCostPerYear[props.machineGroup] ?? "" : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = { ...props.machineCostPerYear };
          if (isNaN(cost)) {
            delete newCost[props.machineGroup];
          } else {
            newCost[props.machineGroup] = cost;
          }
          void saveMachineCostPerYear(newCost);
          props.setMachineCostPerYear(newCost);
        }
        setCost(null);
      }}
    />
  );
}

function StationCostInputs(props: StationCostInputProps) {
  return (
    <>
      {props.machineQuantities.keysToAscLazySeq().map((s, idx) => (
        <SingleStationCostInput key={idx} {...props} machineGroup={s} />
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
  React.useEffect(() => {
    document.title = "Cost Breakdown - FMS Insight";
  }, []);
  const period = useRecoilValue(selectedAnalysisPeriod);
  const month = period.type === "Last30" ? null : period.month;
  const cycles = useRecoilValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const matIds = useRecoilValue(
    period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary
  );

  const thirtyDaysAgo = addDays(startOfToday(), -30);

  const costs = React.useMemo(() => {
    return compute_monthly_cost_percentages(
      cycles.valuesToLazySeq(),
      matIds.matsById,
      month ? { month: month } : { thirtyDaysAgo }
    );
  }, [cycles, matIds, month, thirtyDaysAgo]);

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
        <Table>
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

interface CostPerPieceOutputProps {
  readonly costs: CostData;
}

function CostOutputCard(props: CostPerPieceOutputProps) {
  return (
    <Table data-testid="part-cost-table">
      <TableHead>
        <TableRow>
          <TableCell>Part</TableCell>
          <TableCell align="right">Completed Quantity</TableCell>
          {props.costs.machineQuantities.keysToAscLazySeq().map((m) => (
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
                onClick={() => copyCostPerPieceToClipboard(props.costs)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {LazySeq.of(props.costs.parts)
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
              {props.costs.machineQuantities.keysToAscLazySeq().map((m) => (
                <TableCell align="right" key={m}>
                  {decimalFormat.format(c.machine.get(m) ?? 0)}
                </TableCell>
              ))}
              <TableCell align="right">{decimalFormat.format(c.labor)}</TableCell>
              <TableCell align="right">{decimalFormat.format(c.automation)}</TableCell>
              <TableCell align="right">
                {decimalFormat.format(
                  c.machine.valuesToAscLazySeq().sumBy((x) => x) + c.labor + c.automation
                )}
              </TableCell>
            </TableRow>
          ))}
      </TableBody>
    </Table>
  );
}

export const CostPerPiecePage = React.memo(function CostPerPiecePage() {
  React.useEffect(() => {
    document.title = "Cost Per Piece - FMS Insight";
  }, []);
  const [loading, setLoading] = React.useState(false);
  const [machineCostPerYear, setMachineCostPerYear] = React.useState<MachineCostPerYear>({});
  const [last30LaborCost, setLast30LaborCost] = React.useState<number | null>(null);
  const [curMonthLaborCost, setCurMonthLaborCost] = React.useState<number | null | "LOADING">(null);
  const [automationCostPerYear, setAutomationCostPerYear] = React.useState<number | null>(null);

  const period = useRecoilValue(selectedAnalysisPeriod);
  const month = period.type === "Last30" ? null : period.month;
  const cycles = useRecoilValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const matIds = useRecoilValue(
    period.type === "Last30" ? last30MaterialSummary : specificMonthMaterialSummary
  );

  const thirtyDaysAgo = addDays(startOfToday(), -30);

  React.useEffect(() => {
    void (async () => {
      setLoading(true);
      try {
        await Promise.all([
          loadMachineCostPerYear().then(setMachineCostPerYear),
          loadLast30LaborCost().then(setLast30LaborCost),
          loadAutomationCostPerYear().then(setAutomationCostPerYear),
        ]);
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  React.useEffect(() => {
    if (month) {
      setCurMonthLaborCost("LOADING");
      loadPerMonthLabor(month)
        .then(setCurMonthLaborCost)
        .catch(() => setCurMonthLaborCost(null));
    }
  }, [month]);

  const computedCosts = React.useMemo(() => {
    if (loading) {
      return {
        machineQuantities: OrderedMap.empty<string, number>(),
        parts: [],
        type: month ? { month } : { thirtyDaysAgo },
      } satisfies CostData;
    }
    let totalLaborCost = 0;
    if (month) {
      totalLaborCost = curMonthLaborCost !== "LOADING" ? curMonthLaborCost ?? 0 : 0;
    } else {
      totalLaborCost = last30LaborCost ?? 0;
    }
    const costPcts = compute_monthly_cost_percentages(
      cycles.valuesToLazySeq(),
      matIds.matsById,
      month ? { month: month } : { thirtyDaysAgo }
    );
    return convert_cost_percent_to_cost_per_piece(
      costPcts,
      machineCostPerYear,
      automationCostPerYear,
      totalLaborCost
    );
  }, [
    machineCostPerYear,
    automationCostPerYear,
    cycles,
    matIds,
    month,
    curMonthLaborCost,
    last30LaborCost,
    loading,
  ]);

  if (loading || curMonthLaborCost === "LOADING") {
    return (
      <div style={{ display: "flex", justifyContent: "center" }}>
        <CircularProgress />
      </div>
    );
  }

  return (
    <Box component="main" paddingLeft="24px" paddingRight="24px" paddingTop="20px">
      <Stack direction="column" spacing={4}>
        <Stack direction="column" spacing={2}>
          {month === null ? (
            <LaborCost month={null} laborCost={last30LaborCost} setLaborCost={setLast30LaborCost} />
          ) : (
            <LaborCost month={month} laborCost={curMonthLaborCost} setLaborCost={setCurMonthLaborCost} />
          )}
          <AutomationCostInput
            automationCostPerYear={automationCostPerYear}
            setAutomationCostPerYear={setAutomationCostPerYear}
          />
          <StationCostInputs
            machineQuantities={computedCosts.machineQuantities}
            machineCostPerYear={machineCostPerYear}
            setMachineCostPerYear={setMachineCostPerYear}
          />
        </Stack>
        <CostOutputCard costs={computedCosts} />
      </Stack>
    </Box>
  );
});
