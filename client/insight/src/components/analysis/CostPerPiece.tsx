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
import { Box, Card } from "@mui/material";
import { CardHeader } from "@mui/material";
import { CardContent } from "@mui/material";
import { Table } from "@mui/material";
import { TableBody } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableRow } from "@mui/material";
import { TextField } from "@mui/material";
import { AttachMoney as MoneyIcon, ImportExport, Build as BuildIcon, CallSplit } from "@mui/icons-material";
import AnalysisSelectToolbar from "./AnalysisSelectToolbar.js";
import { selectedAnalysisPeriod } from "../../network/load-specific-month.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import * as localForage from "localforage";
import { CircularProgress } from "@mui/material";
import {
  MachineCostPerYear,
  compute_monthly_cost,
  copyCostPerPieceToClipboard,
  CostData,
  PartCost,
  copyCostBreakdownToClipboard,
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
  readonly statGroups: ReadonlyArray<string>;
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
      {props.statGroups.map((s, idx) => (
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

interface CostBreakdownProps {
  readonly costs: CostData;
}

function CostBreakdown(props: CostBreakdownProps) {
  return (
    <Card style={{ marginTop: "2em" }}>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <CallSplit style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Part Cost Percentage Breakdown</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyCostBreakdownToClipboard(props.costs)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
      />
      <CardContent>
        <Table data-testid="part-cost-pct-table">
          <TableHead>
            <TableRow>
              <TableCell>Part</TableCell>
              <TableCell align="right">Completed Quantity</TableCell>
              {props.costs.machineCostGroups.map((m) => (
                <TableCell align="right" key={m}>
                  {m} Cost %
                </TableCell>
              ))}
              <TableCell align="right">Labor Cost %</TableCell>
              <TableCell align="right">Automation Cost %</TableCell>
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
                  {props.costs.machineCostGroups.map((m) => (
                    <TableCell align="right" key={m}>
                      {pctFormat.format(c.machine.pctPerStat.get(m) ?? 0)}
                    </TableCell>
                  ))}
                  <TableCell align="right">{pctFormat.format(c.labor.percent)}</TableCell>
                  <TableCell align="right">{pctFormat.format(c.automation_pct)}</TableCell>
                </TableRow>
              ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function MachineCostTooltip({
  part,
  machineCostForPeriod,
}: {
  readonly part: PartCost;
  readonly machineCostForPeriod: ReadonlyMap<string, number>;
}) {
  return (
    <div>
      {LazySeq.of(part.machine.pctPerStat)
        .sortBy(([stat]) => stat)
        .map(([stat, pct]) => (
          <div key={stat}>
            {stat}: {pctFormat.format(pct)} of {decimalFormat.format(machineCostForPeriod.get(stat) ?? 0)}{" "}
            total machine cost
          </div>
        ))}
    </div>
  );
}

interface CostPerPieceOutputProps {
  readonly costs: CostData;
}

function CostOutputCard(props: CostPerPieceOutputProps) {
  return (
    <Card style={{ marginTop: "2em" }}>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <BuildIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Part Cost/Piece</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyCostPerPieceToClipboard(props.costs)}
                size="large"
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
      />
      <CardContent>
        <Table data-testid="part-cost-table">
          <TableHead>
            <TableRow>
              <TableCell>Part</TableCell>
              <TableCell align="right">Completed Quantity</TableCell>
              <TableCell align="right">Machine Cost</TableCell>
              <TableCell align="right">Labor Cost</TableCell>
              <TableCell align="right">Automation Cost</TableCell>
              <TableCell align="right">Total</TableCell>
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
                  <TableCell align="right">
                    <Tooltip
                      title={
                        <MachineCostTooltip
                          part={c}
                          machineCostForPeriod={props.costs.stationCostForPeriod}
                        />
                      }
                    >
                      <span>
                        {c.parts_completed > 0 ? decimalFormat.format(c.machine.cost / c.parts_completed) : 0}
                      </span>
                    </Tooltip>
                  </TableCell>
                  <TableCell align="right">
                    <Tooltip
                      title={
                        pctFormat.format(c.labor.percent) +
                        " of " +
                        decimalFormat.format(props.costs.totalLaborCostForPeriod) +
                        " total labor cost"
                      }
                    >
                      <span>
                        {c.parts_completed > 0 ? decimalFormat.format(c.labor.cost / c.parts_completed) : 0}
                      </span>
                    </Tooltip>
                  </TableCell>
                  <TableCell align="right">
                    <Tooltip
                      title={
                        pctFormat.format(c.automation_pct) +
                        " of " +
                        decimalFormat.format(props.costs.automationCostForPeriod) +
                        " total automation cost"
                      }
                    >
                      <span>
                        {c.parts_completed > 0
                          ? decimalFormat.format(
                              (c.automation_pct * props.costs.automationCostForPeriod) / c.parts_completed
                            )
                          : 0}
                      </span>
                    </Tooltip>
                  </TableCell>
                  <TableCell align="right">
                    {c.parts_completed > 0
                      ? decimalFormat.format(
                          c.machine.cost / c.parts_completed +
                            c.labor.cost / c.parts_completed +
                            (c.automation_pct * props.costs.automationCostForPeriod) / c.parts_completed
                        )
                      : ""}
                  </TableCell>
                </TableRow>
              ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function CostInputCard(props: { children: React.ReactNode }) {
  return (
    <Card style={{ maxWidth: "45em", width: "100%", marginTop: "2em" }}>
      <CardHeader
        title={
          <div
            style={{
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
            }}
          >
            <MoneyIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Cost Inputs</div>
          </div>
        }
      />
      <CardContent>{props.children}</CardContent>
    </Card>
  );
}

export const CostPerPiecePage = React.memo(function CostPerPiecePage() {
  const [loading, setLoading] = React.useState(false);
  const [machineCostPerYear, setMachineCostPerYear] = React.useState<MachineCostPerYear>({});
  const [last30LaborCost, setLast30LaborCost] = React.useState<number | null>(null);
  const [curMonthLaborCost, setCurMonthLaborCost] = React.useState<number | null | "LOADING">(null);
  const [automationCostPerYear, setAutomationCostPerYear] = React.useState<number | null>(null);

  const period = useRecoilValue(selectedAnalysisPeriod);
  const month = period.type === "Last30" ? null : period.month;
  const cycles = useRecoilValue(period.type === "Last30" ? last30StationCycles : specificMonthStationCycles);
  const statGroups = React.useMemo(() => {
    const groups = new Set<string>();
    for (const c of cycles.valuesToLazySeq()) {
      groups.add(c.stationGroup);
    }
    return Array.from(groups).sort((a, b) => a.localeCompare(b));
  }, [cycles]);
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
        totalLaborCostForPeriod: 0,
        automationCostForPeriod: 0,
        stationCostForPeriod: new Map<string, number>(),
        machineCostGroups: [],
        parts: [],
      };
    }
    let totalLaborCost = 0;
    if (month) {
      totalLaborCost = curMonthLaborCost !== "LOADING" ? curMonthLaborCost ?? 0 : 0;
    } else {
      totalLaborCost = last30LaborCost ?? 0;
    }
    return compute_monthly_cost(
      machineCostPerYear,
      automationCostPerYear,
      totalLaborCost,
      cycles.valuesToLazySeq(),
      matIds.matsById,
      month ? { month: month } : { thirtyDaysAgo }
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
    <>
      <CostBreakdown costs={computedCosts} />
      <div style={{ display: "flex", justifyContent: "center" }}>
        <CostInputCard>
          <div style={{ display: "flex", flexDirection: "column" }}>
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
              statGroups={statGroups}
              machineCostPerYear={machineCostPerYear}
              setMachineCostPerYear={setMachineCostPerYear}
            />
          </div>
        </CostInputCard>
      </div>
      <CostOutputCard costs={computedCosts} />
    </>
  );
});

export default function CostPerPiece(): JSX.Element {
  React.useEffect(() => {
    document.title = "Cost/Piece - FMS Insight";
  }, []);
  return (
    <>
      <AnalysisSelectToolbar />
      <main style={{ padding: "8px" }}>
        <CostPerPiecePage />
      </main>
    </>
  );
}
