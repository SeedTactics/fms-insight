/* Copyright (c) 2019, John Lenz

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
// eslint-disable-next-line @typescript-eslint/no-var-requires
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58
import * as ccp from "../../data/cost-per-piece";
import { DispatchAction, connect, mkAC, Store } from "../../store/store";
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
import Divider from "@material-ui/core/Divider";
import MoneyIcon from "@material-ui/icons/AttachMoney";
import { PartCycleData } from "../../data/events.cycles";
import { createSelector } from "reselect";
import BuildIcon from "@material-ui/icons/Build";
import AnalysisSelectToolbar from "./AnalysisSelectToolbar";
import { HashSet, Vector } from "prelude-ts";
import { LazySeq } from "../../data/lazyseq";

interface CostPerPieceInputProps {
  readonly statGroups: HashSet<string>;
  readonly input: ccp.CostInput;

  readonly setMachineCost: DispatchAction<ccp.ActionType.SetMachineCostPerYear>;
  readonly setPartMatCost: DispatchAction<ccp.ActionType.SetPartMaterialCost>;
  readonly setAutomationCost: DispatchAction<ccp.ActionType.SetAutomationCost>;
  readonly setNumOper: DispatchAction<ccp.ActionType.SetNumOperators>;
  readonly setOperCostPerHour: DispatchAction<ccp.ActionType.SetOperatorCostPerHour>;
}

function CostPerPieceInput(props: CostPerPieceInputProps) {
  return (
    <div style={{ display: "flex", justifyContent: "center" }}>
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
        <CardContent>
          <div style={{ display: "flex", justifyContent: "space-around" }}>
            <TextField
              id="num-operators"
              type="number"
              label="Number of Operators"
              inputProps={{ min: 0 }}
              value={props.input.numOperators}
              onChange={(e) => props.setNumOper({ numOpers: parseFloat(e.target.value) })}
            />
            <TextField
              id="cost-per-oper-hour"
              type="number"
              label="Cost per operator per hour"
              inputProps={{ min: 0 }}
              value={props.input.operatorCostPerHour}
              onChange={(e) => props.setOperCostPerHour({ cost: parseFloat(e.target.value) })}
            />
          </div>
          <div style={{ marginTop: "1em", display: "flex", justifyContent: "space-around" }}>
            <div style={{ width: "20em" }}>
              <TextField
                id="auotmation-cost-year"
                type="number"
                fullWidth
                label="Cost for automated handling system per year"
                inputProps={{ min: 0 }}
                value={props.input.automationCostPerYear || 0}
                onChange={(e) => props.setAutomationCost({ cost: parseFloat(e.target.value) })}
              />
            </div>
          </div>
          <Divider style={{ margin: "15px" }} />
          <Table data-testid="station-cost-table">
            <TableHead>
              <TableRow>
                <TableCell>Station</TableCell>
                <TableCell>Cost/Year</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {props.statGroups.toArray({ sortOn: (x) => x }).map((s, idx) => (
                <TableRow key={idx}>
                  <TableCell>{s}</TableCell>
                  <TableCell>
                    <TextField
                      type="number"
                      inputProps={{ min: 0 }}
                      fullWidth
                      value={props.input.machineCostPerYear[s] || 0}
                      onChange={(e) =>
                        props.setMachineCost({
                          group: s,
                          cost: parseFloat(e.target.value),
                        })
                      }
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}

const ConnectedCostPerPieceInput = connect(
  (s) => ({
    statGroups:
      s.Events.analysis_period === AnalysisPeriod.Last30Days
        ? s.Events.last30.cycles.station_groups
        : s.Events.selected_month.cycles.station_groups,
    input: s.CostPerPiece.input,
  }),
  {
    setMachineCost: mkAC(ccp.ActionType.SetMachineCostPerYear),
    setAutomationCost: mkAC(ccp.ActionType.SetAutomationCost),
    setPartMatCost: mkAC(ccp.ActionType.SetPartMaterialCost),
    setNumOper: mkAC(ccp.ActionType.SetNumOperators),
    setOperCostPerHour: mkAC(ccp.ActionType.SetOperatorCostPerHour),
  }
)(CostPerPieceInput);

interface CostPerPieceOutputProps {
  readonly input: ccp.CostInput;
  readonly costs: ReadonlyArray<ccp.PartCost>;

  readonly setPartMatCost: DispatchAction<ccp.ActionType.SetPartMaterialCost>;
}

function CostPerPieceOutput(props: CostPerPieceOutputProps) {
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
                    <TextField
                      type="number"
                      inputProps={{ min: 0 }}
                      value={props.input.partMaterialCost[c.part] || 0}
                      onChange={(e) =>
                        props.setPartMatCost({
                          part: c.part,
                          cost: parseFloat(e.target.value),
                        })
                      }
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
                          (props.input.partMaterialCost[c.part] || 0) +
                            c.machine_cost / c.parts_completed +
                            c.labor_cost / c.parts_completed +
                            c.automation_cost / c.parts_completed
                        )
                      : format.format(props.input.partMaterialCost[c.part] || 0)}
                  </TableCell>
                </TableRow>
              ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

const calcCostPerPiece = createSelector(
  (s: Store) =>
    s.Events.analysis_period === AnalysisPeriod.Last30Days
      ? s.Events.last30.cycles.part_cycles
      : s.Events.selected_month.cycles.part_cycles,
  (s: Store) => s.CostPerPiece.input,
  (s: Store) => (s.Events.analysis_period === AnalysisPeriod.Last30Days ? undefined : s.Events.analysis_period_month),
  (cycles: Vector<PartCycleData>, input: ccp.CostInput, month: Date | undefined) => {
    return ccp.compute_monthly_cost(input, cycles, month);
  }
);

const ConnectedCostPerPieceOutput = connect(
  (s) => ({
    input: s.CostPerPiece.input,
    costs: calcCostPerPiece(s),
  }),
  {
    setPartMatCost: mkAC(ccp.ActionType.SetPartMaterialCost),
  }
)(CostPerPieceOutput);

export default function CostPerPiece() {
  return (
    <DocumentTitle title="Cost/Piece - FMS Insight">
      <>
        <AnalysisSelectToolbar />
        <main style={{ padding: "8px" }}>
          <ConnectedCostPerPieceInput />
          <ConnectedCostPerPieceOutput />
        </main>
      </>
    </DocumentTitle>
  );
}
