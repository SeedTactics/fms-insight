/* Copyright (c) 2020, John Lenz

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
import { connect } from "../../store/store";
import { AnalysisPeriod } from "../../data/events";
import Card from "@material-ui/core/Card";
import CardHeader from "@material-ui/core/CardHeader";
import CardContent from "@material-ui/core/CardContent";
import Table from "@material-ui/core/Table";
import TableBody from "@material-ui/core/TableBody";
import TableCell from "@material-ui/core/TableCell";
import TableHead from "@material-ui/core/TableHead";
import TableRow from "@material-ui/core/TableRow";
import TextField from "@material-ui/core/TextField";
import MoneyIcon from "@material-ui/icons/AttachMoney";
import ImportExport from "@material-ui/icons/ImportExport";
import { PartCycleData } from "../../data/events.cycles";
import BuildIcon from "@material-ui/icons/Build";
import AnalysisSelectToolbar from "./AnalysisSelectToolbar";
import { HashSet, Vector } from "prelude-ts";
import { LazySeq } from "../../data/lazyseq";
import * as localForage from "localforage";
import CircularProgress from "@material-ui/core/CircularProgress";
import {
  MachineCostPerYear,
  PartMaterialCost,
  PartCost,
  compute_monthly_cost,
  copyCostPerPieceToClipboard,
} from "../../data/cost-per-piece";
import { format } from "date-fns";
import Tooltip from "@material-ui/core/Tooltip";
import IconButton from "@material-ui/core/IconButton";

interface Last30LaborCost {
  readonly numOperators: number | null;
  readonly averageWagePerHour: number | null;
  readonly hoursPerDay: number;
}

async function loadMachineCostPerYear(): Promise<MachineCostPerYear> {
  return (await localForage.getItem("MachineCostPerYear")) ?? {};
}

async function saveMachineCostPerYear(m: MachineCostPerYear): Promise<void> {
  await localForage.setItem("MachineCostPerYear", m);
}

async function loadPartMatCost(): Promise<PartMaterialCost> {
  return (await localForage.getItem("PartMaterialCost")) ?? {};
}

async function savePartMatCost(m: PartMaterialCost): Promise<void> {
  await localForage.setItem("PartMaterialCost", m);
}

async function loadAutomationCostPerYear(): Promise<number | null> {
  return await localForage.getItem("AutomationCostPerYear");
}

async function saveAutomationCostPerYear(n: number | null): Promise<void> {
  await localForage.setItem("AutomationCostPerYear", n);
}

async function loadLast30LaborCost(): Promise<Last30LaborCost> {
  return (
    (await localForage.getItem("Last30LaborCost")) ?? { numOperators: null, averageWagePerHour: null, hoursPerDay: 24 }
  );
}

async function saveLast30LaborCost(c: Last30LaborCost): Promise<void> {
  await localForage.setItem("Last30LaborCost", c);
}

async function loadPerMonthLabor(month: Date): Promise<number | null> {
  const key = "PerMonthLabor-" + month.getFullYear().toString() + "-" + month.getMonth().toString();
  return await localForage.getItem(key);
}

async function savePerMonthLabor(month: Date, cost: number | null): Promise<void> {
  const key = "PerMonthLabor-" + month.getFullYear().toString() + "-" + month.getMonth().toString();
  await localForage.setItem(key, cost);
}

interface Last30LaborCostProps {
  readonly input: Last30LaborCost;
  readonly setLast30: (l: Last30LaborCost) => void;
}

function Last30LaborCostInput(props: Last30LaborCostProps) {
  const [numOper, setNumOper] = React.useState<number | null>(null);
  const [perHour, setPerHour] = React.useState<number | null>(null);
  const [hoursPerDay, setHoursPerDay] = React.useState<number | null>(null);

  return (
    <>
      <TextField
        id="num-operators"
        type="number"
        label="Number of Operators"
        inputProps={{ min: 0 }}
        variant="outlined"
        value={numOper === null ? props.input.numOperators : isNaN(numOper) ? "" : numOper}
        onChange={(e) => setNumOper(parseInt(e.target.value))}
        onBlur={() => {
          if (numOper != null) {
            const newCost = { ...props.input, numOperators: isNaN(numOper) ? null : numOper };
            saveLast30LaborCost(newCost);
            props.setLast30(newCost);
          }
          setNumOper(null);
        }}
      />
      <TextField
        id="cost-per-oper-hour"
        type="number"
        style={{ marginTop: "1.5em" }}
        label="Cost per operator per hour"
        inputProps={{ min: 0 }}
        variant="outlined"
        value={perHour === null ? props.input.averageWagePerHour : isNaN(perHour) ? "" : perHour}
        onChange={(e) => setPerHour(parseFloat(e.target.value))}
        onBlur={() => {
          if (perHour != null) {
            const newCost = { ...props.input, averageWagePerHour: isNaN(perHour) ? null : perHour };
            saveLast30LaborCost(newCost);
            props.setLast30(newCost);
          }
          setPerHour(null);
        }}
      />
      <TextField
        id="hours-per-day"
        type="number"
        label="Hours Per Day"
        style={{ marginTop: "1.5em" }}
        inputProps={{ min: 0, max: 24 }}
        variant="outlined"
        value={hoursPerDay === null ? props.input.hoursPerDay : isNaN(hoursPerDay) ? "" : hoursPerDay}
        onChange={(e) => setHoursPerDay(parseFloat(e.target.value))}
        onBlur={() => {
          if (hoursPerDay != null && !isNaN(hoursPerDay)) {
            const newCost = { ...props.input, hoursPerDay: hoursPerDay };
            saveLast30LaborCost(newCost);
            props.setLast30(newCost);
          }
          setHoursPerDay(null);
        }}
      />
    </>
  );
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
          saveAutomationCostPerYear(newCost);
          props.setAutomationCostPerYear(newCost);
        }
        setCost(null);
      }}
    />
  );
}

interface SpecificMonthLaborCostProps {
  readonly laborCostForMonth: number | null;
  readonly month: Date;
  readonly setLaborCostForMonth: (n: number | null) => void;
}

function SpecificMonthLaborCost(props: SpecificMonthLaborCostProps) {
  const [cost, setCost] = React.useState<number | null>(null);

  return (
    <TextField
      type="number"
      label={"Total labor cost for " + format(props.month, "MMMM yyyy")}
      inputProps={{ min: 0 }}
      variant="outlined"
      value={cost === null ? props.laborCostForMonth ?? "" : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = isNaN(cost) ? null : cost;
          savePerMonthLabor(props.month, newCost);
          props.setLaborCostForMonth(newCost);
        }
        setCost(null);
      }}
    />
  );
}

interface StationCostInputProps {
  readonly statGroups: HashSet<string>;
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
          saveMachineCostPerYear(newCost);
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
      {props.statGroups.toArray({ sortOn: (x) => x }).map((s, idx) => (
        <SingleStationCostInput key={idx} {...props} machineGroup={s} />
      ))}
    </>
  );
}

interface PartMatCostProps {
  readonly partName: string;
  readonly partMaterialCosts: PartMaterialCost;
  readonly setPartMatCost: (c: PartMaterialCost) => void;
}

function PartMatCostInput(props: PartMatCostProps) {
  const [cost, setCost] = React.useState<number | null>(null);

  return (
    <TextField
      type="number"
      inputProps={{ min: 0 }}
      value={cost === null ? props.partMaterialCosts[props.partName] ?? "" : isNaN(cost) ? "" : cost}
      onChange={(e) => setCost(parseFloat(e.target.value))}
      onBlur={() => {
        if (cost != null) {
          const newCost = { ...props.partMaterialCosts };
          if (isNaN(cost)) {
            delete newCost[props.partName];
          } else {
            newCost[props.partName] = cost;
          }
          savePartMatCost(newCost);
          props.setPartMatCost(newCost);
        }
        setCost(null);
      }}
    />
  );
}

interface CostPerPieceOutputProps {
  readonly partMaterialCosts: PartMaterialCost;
  readonly setPartMatCost: (c: PartMaterialCost) => void;

  readonly costs: ReadonlyArray<PartCost>;
}

function CostOutputCard(props: CostPerPieceOutputProps) {
  const format = Intl.NumberFormat(undefined, {
    maximumFractionDigits: 1,
  });
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
                onClick={() => copyCostPerPieceToClipboard(props.costs, props.partMaterialCosts)}
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
              <TableCell>Material Cost</TableCell>
              <TableCell>Machine Cost</TableCell>
              <TableCell>Labor Cost</TableCell>
              <TableCell>Automation Cost</TableCell>
              <TableCell>Total</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {Vector.ofIterable(props.costs)
              .sortOn((c) => c.part)
              .transform((x) => LazySeq.ofIterable(x))
              .map((c, idx) => (
                <TableRow key={idx}>
                  <TableCell>{c.part}</TableCell>
                  <TableCell>
                    <PartMatCostInput
                      partMaterialCosts={props.partMaterialCosts}
                      setPartMatCost={props.setPartMatCost}
                      partName={c.part}
                    />
                  </TableCell>
                  <TableCell>{c.parts_completed > 0 ? format.format(c.machine_cost / c.parts_completed) : 0}</TableCell>
                  <TableCell>{c.parts_completed > 0 ? format.format(c.labor_cost / c.parts_completed) : 0}</TableCell>
                  <TableCell>
                    {c.parts_completed > 0 ? format.format(c.automation_cost / c.parts_completed) : 0}
                  </TableCell>
                  <TableCell>
                    {c.parts_completed > 0
                      ? format.format(
                          (props.partMaterialCosts[c.part] || 0) +
                            c.machine_cost / c.parts_completed +
                            c.labor_cost / c.parts_completed +
                            c.automation_cost / c.parts_completed
                        )
                      : format.format(props.partMaterialCosts[c.part] || 0)}
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
    <Card style={{ maxWidth: "45em", width: "100%" }}>
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

interface CostPerPieceProps {
  readonly statGroups: HashSet<string>;
  readonly cycles: Vector<PartCycleData>;
  readonly month: Date | null;
}

function CostPerPiecePage(props: CostPerPieceProps) {
  const [loading, setLoading] = React.useState(false);
  const [machineCostPerYear, setMachineCostPerYear] = React.useState<MachineCostPerYear>({});
  const [last30LaborCost, setLast30LaborCost] = React.useState<Last30LaborCost>({
    numOperators: null,
    averageWagePerHour: null,
    hoursPerDay: 24,
  });
  const [curMonthLaborCost, setCurMonthLaborCost] = React.useState<number | null | "LOADING">(null);
  const [automationCostPerYear, setAutomationCostPerYear] = React.useState<number | null>(null);
  const [partMatCosts, setPartMatCost] = React.useState<PartMaterialCost>({});

  React.useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        await Promise.all([
          loadMachineCostPerYear().then(setMachineCostPerYear),
          loadLast30LaborCost().then(setLast30LaborCost),
          loadAutomationCostPerYear().then(setAutomationCostPerYear),
          loadPartMatCost().then(setPartMatCost),
        ]);
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  React.useEffect(() => {
    if (props.month) {
      setCurMonthLaborCost("LOADING");
      loadPerMonthLabor(props.month)
        .then(setCurMonthLaborCost)
        .catch(() => setCurMonthLaborCost(null));
    }
  }, [props.month]);

  const computedCosts = React.useMemo(() => {
    if (loading) {
      return [];
    }
    let totalLaborCost = 0;
    if (props.month) {
      totalLaborCost = curMonthLaborCost !== "LOADING" ? curMonthLaborCost ?? 0 : 0;
    } else {
      totalLaborCost =
        (last30LaborCost.averageWagePerHour ?? 0) * last30LaborCost.hoursPerDay * (last30LaborCost.numOperators ?? 0);
    }
    return compute_monthly_cost(
      machineCostPerYear,
      partMatCosts,
      automationCostPerYear,
      totalLaborCost,
      props.cycles,
      props.month
    );
  }, [
    machineCostPerYear,
    partMatCosts,
    automationCostPerYear,
    props.cycles,
    props.month,
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
      <div style={{ display: "flex", justifyContent: "center" }}>
        <CostInputCard>
          <div style={{ display: "flex", flexDirection: "column" }}>
            {props.month === null ? (
              <Last30LaborCostInput input={last30LaborCost} setLast30={setLast30LaborCost} />
            ) : (
              <SpecificMonthLaborCost
                month={props.month}
                laborCostForMonth={curMonthLaborCost}
                setLaborCostForMonth={setCurMonthLaborCost}
              />
            )}
            <AutomationCostInput
              automationCostPerYear={automationCostPerYear}
              setAutomationCostPerYear={setAutomationCostPerYear}
            />
            <StationCostInputs
              statGroups={props.statGroups}
              machineCostPerYear={machineCostPerYear}
              setMachineCostPerYear={setMachineCostPerYear}
            />
          </div>
        </CostInputCard>
      </div>
      <CostOutputCard partMaterialCosts={partMatCosts} setPartMatCost={setPartMatCost} costs={computedCosts} />
    </>
  );
}

const ConnectedCostPerPiecePage = connect((s) => ({
  statGroups:
    s.Events.analysis_period === AnalysisPeriod.Last30Days
      ? s.Events.last30.cycles.station_groups
      : s.Events.selected_month.cycles.station_groups,
  cycles:
    s.Events.analysis_period === AnalysisPeriod.Last30Days
      ? s.Events.last30.cycles.part_cycles
      : s.Events.selected_month.cycles.part_cycles,
  month: s.Events.analysis_period === AnalysisPeriod.Last30Days ? null : s.Events.analysis_period_month,
}))(CostPerPiecePage);

export default function CostPerPiece() {
  React.useEffect(() => {
    document.title = "Cost/Piece - FMS Insight";
  }, []);
  return (
    <>
      <AnalysisSelectToolbar />
      <main style={{ padding: "8px" }}>
        <ConnectedCostPerPiecePage />
      </main>
    </>
  );
}
