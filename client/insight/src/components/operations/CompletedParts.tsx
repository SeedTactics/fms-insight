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
import Card from "@material-ui/core/Card";
import CardHeader from "@material-ui/core/CardHeader";
import CardContent from "@material-ui/core/CardContent";
import IconButton from "@material-ui/core/IconButton";
import Tooltip from "@material-ui/core/Tooltip";
import ImportExport from "@material-ui/icons/ImportExport";
import ExtensionIcon from "@material-ui/icons/Extension";
import Table from "@material-ui/core/Table";
import TableRow from "@material-ui/core/TableRow";
import TableCell from "@material-ui/core/TableCell";
import TableHead from "@material-ui/core/TableHead";
import TableBody from "@material-ui/core/TableBody";
import {
  CompletedPartSeries,
  buildCompletedPartSeries,
  copyCompletedPartsToClipboard
} from "../../data/results.completed-parts";
import { connect } from "../../store/store";
import { createSelector } from "reselect";
import { Last30Days } from "../../data/events";
import { addDays, startOfToday } from "date-fns";
const DocumentTitle = require("react-document-title"); // https://github.com/gaearon/react-document-title/issues/58

export interface CompletedPartsTableProps {
  readonly series: ReadonlyArray<CompletedPartSeries>;
}

export function CompletedPartsTable(props: CompletedPartsTableProps) {
  const days = props.series.length > 0 ? props.series[0].days.map(p => p.day) : [];
  return (
    <Card raised>
      <CardHeader
        title={
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
            <ExtensionIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Completed Parts</div>
            <div style={{ flexGrow: 1 }} />
            <Tooltip title="Copy to Clipboard">
              <IconButton
                style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                onClick={() => copyCompletedPartsToClipboard(props.series)}
              >
                <ImportExport />
              </IconButton>
            </Tooltip>
          </div>
        }
      />
      <CardContent>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Part</TableCell>
              {days.map((d, idx) => (
                <TableCell key={idx}>{d.toLocaleDateString()}</TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {props.series.map((part, partIdx) => (
              <TableRow key={partIdx}>
                <TableCell>{part.part}</TableCell>
                {part.days.map((day, dayIdx) => (
                  <TableCell key={dayIdx}>{day.actual.toFixed(0) + " / " + day.planned.toFixed(0)}</TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

const completedPartsSelector = createSelector(
  (last30: Last30Days) => last30.cycles.part_cycles,
  (last30: Last30Days) => last30.sim_use.production,
  (cycles, sim): ReadonlyArray<CompletedPartSeries> => {
    const start = addDays(startOfToday(), -6);
    const end = addDays(startOfToday(), 1);
    return buildCompletedPartSeries(start, end, cycles, sim);
  }
);

const ConnectedPartsTable = connect(st => ({
  series: completedPartsSelector(st.Events.last30)
}))(CompletedPartsTable);

export function CompletedParts() {
  return (
    <DocumentTitle title="Completed Parts - FMS Insight">
      <main style={{ padding: "24px" }}>
        <div data-testid="completed-parts">
          <ConnectedPartsTable />
        </div>
      </main>
    </DocumentTitle>
  );
}
