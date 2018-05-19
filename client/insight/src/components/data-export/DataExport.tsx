/* Copyright (c) 2018, John Lenz

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
import * as React from 'react';
import * as df from 'date-fns';
import DocumentTitle from 'react-document-title';
import TextField from '@material-ui/core/TextField';
import Button from '@material-ui/core/Button';
import ExportIcon from '@material-ui/icons/ImportExport';
import ViewIcon from '@material-ui/icons/ViewList';
import Card from '@material-ui/core/Card';
import CardContent from '@material-ui/core/CardContent';
import CardHeader from '@material-ui/core/CardHeader';
import CardActions from '@material-ui/core/CardActions';
import Grid from '@material-ui/core/Grid';
import * as queryString from 'query-string';

import * as api from '../../data/api';
import { Store, connect } from '../../store/store';
import { LogEntries } from '../LogEntry';

export interface CSVExportState {
  readonly startDate: string;
  readonly startTime: string;
  readonly endDate: string;
  readonly endTime: string;
}

export class CSVExport extends React.PureComponent<{}, CSVExportState> {
  state: CSVExportState = {
    startDate: df.format(df.addDays(new Date(), -1), "YYYY-MM-DD"),
    startTime: df.format(new Date(), "HH:mm"),
    endDate: df.format(new Date(), "YYYY-MM-DD"),
    endTime: df.format(new Date(), "HH:mm"),
  };

  render() {
    const startDate = df.parse(this.state.startDate + " " + this.state.startTime);
    const endDate = df.parse(this.state.endDate + " " + this.state.endTime);
    const startEndQuery = queryString.stringify({
      startUTC: startDate.toISOString(),
      endUTC: endDate.toISOString(),
    });
    const curlUrl =
      window.location.protocol + "//" + window.location.host
     + "/api/v1/log/events/all?" + startEndQuery;

    return (
      <Card style={{margin: '2em'}}>
        <CardHeader
          title={
          <div style={{display: 'flex', alignItems: 'center'}}>
            <ExportIcon/>
            <div style={{marginLeft: '10px', marginRight: '3em'}}>
              Log Data Export
            </div>
          </div>}
        />
        <CardContent>
          <Grid container>
            <Grid item xs={12} sm={6}>
              <div style={{display: 'flex', marginBottom: '2em'}}>
                <TextField
                  label="Start Date"
                  type="date"
                  value={this.state.startDate}
                  onChange={e => this.setState({startDate: e.target.value})}
                />
                <TextField
                  style={{marginLeft: '2em'}}
                  label="Start Time"
                  type="time"
                  value={this.state.startTime}
                  onChange={e => this.setState({startTime: e.target.value})}
                />
              </div>
              <div style={{display: 'flex'}}>
                <TextField
                  label="End Date"
                  type="date"
                  value={this.state.endDate}
                  onChange={e => this.setState({endDate: e.target.value})}
                />
                <TextField
                  style={{marginLeft: '2em'}}
                  label="End Time"
                  type="time"
                  value={this.state.endTime}
                  onChange={e => this.setState({endTime: e.target.value})}
                />
              </div>
            </Grid>
            <Grid item xs={12} sm={6} md={5}>
              <p>
                Data is also available programatically over HTTP.
                See the <a href="/swagger/">OpenAPI Specification</a> or
                try the following command in the terminal or PowerShell.
              </p>
              <code>
                curl -o events.json {curlUrl}
              </code>
            </Grid>
          </Grid>
        </CardContent>
        <CardActions>
          <Button
            variant="raised"
            color="primary"
            href={"/api/v1/log/events.csv?" + startEndQuery}
          >
            Export to CSV
          </Button>
        </CardActions>
      </Card>
    );
  }
}

export interface RecentEventsProps {
  events: ReadonlyArray<Readonly<api.ILogEntry>>;
}

export function RecentEvents(p: RecentEventsProps) {
  return (
    <Card style={{margin: '2em'}}>
      <CardHeader
        title={
        <div style={{display: 'flex', alignItems: 'center'}}>
          <ViewIcon/>
          <div style={{marginLeft: '10px', marginRight: '3em'}}>
            Preview of Most Recent Events
          </div>
        </div>}
      />
      <CardContent>
        <LogEntries entries={p.events}/>
      </CardContent>
    </Card>
  );
}

const ConnectedRecentEvents = connect(
  (s: Store) => ({
    events: s.Events.last30.most_recent_10_events,
  })
)(RecentEvents);

export default function DataExport() {
  return (
    <DocumentTitle title="Data Export - FMS Insight">
      <main style={{padding: '8px'}}>
        <CSVExport/>
        <ConnectedRecentEvents/>
      </main>
    </DocumentTitle>
  );
}