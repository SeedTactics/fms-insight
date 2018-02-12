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
import { connect } from 'react-redux';
import * as im from 'immutable';
import { duration } from 'moment';

import * as api from '../data/api';
import { Store } from '../data/store';
import { FlexibleWidthXYPlot, HorizontalBarSeries, XAxis, YAxis, MarkSeries, VerticalGridLines } from 'react-vis';

import 'react-vis/dist/style.css';

interface DataPoint {
  part: string;
  completed: number;
  totalPlan: number;
}

export function DisplayJob(job: api.IInProcessJob, proc: number): DataPoint {
  const totalPlan = job.cyclesOnFirstProcess.reduce((a, b) => a + b, 0);
  const completed = job.completed[proc].reduce((a, b) => a + b, 0);

  const stops = job.procsAndPaths[proc].paths[0].stops;
  let cycleTime = duration();
  for (let i = 0; i < stops.length; i++) {
    const x = duration(stops[i].expectedCycleTime);
    cycleTime = cycleTime.add(x);
  }
  const cycleTimeHours = cycleTime.asHours();
  return {
    part: job.partName + '-' + (proc + 1).toString(),
    completed: cycleTimeHours * completed,
    totalPlan: cycleTimeHours * totalPlan
  };
}

export interface Props {
  jobs: ReadonlyArray<Readonly<api.IInProcessJob>>;
}

export function CurrentJobs(p: Props) {
  const data = im.Seq(p.jobs)
    .flatMap(j =>
      im.Range(0, j.procsAndPaths.length).map(proc =>
        DisplayJob(j, proc)
      )
    )
    .sortBy(pt => pt.part)
    .reverse()
    .cacheResult();
  const completedData =
    data.map((pt, i) => ({x: pt.completed, y: i, part: pt.part}))
        .toArray();
  const planData =
    data.map((pt, i) => ({x: pt.totalPlan, y: i, part: pt.part}))
        .toArray();
  return (
    <div>
      <FlexibleWidthXYPlot
          height={300}
          margin={{left: 70, right: 10, top: 10, bottom: 40}}
          yType="ordinal"
      >
        <XAxis/>
        <YAxis tickFormat={(y: number, i: number) => completedData[i].part}/>
        <VerticalGridLines color="green"/>
        <HorizontalBarSeries data={completedData}/>
        <MarkSeries data={planData}/>
      </FlexibleWidthXYPlot>
      <div style={{'textAlign': 'center'}}>Machine Hours</div>
    </div>
  );
}

export default connect(
  (s: Store) => {
    return {
        jobs: Object.values(s.Jobs.current_status.jobs)
    };
  }
)(CurrentJobs);