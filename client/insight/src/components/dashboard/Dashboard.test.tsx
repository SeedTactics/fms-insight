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
import { render, cleanup, wait } from 'react-testing-library';
afterEach(cleanup);
import { Provider } from 'react-redux';
import 'jest-dom/extend-expect';

// tslint:disable-next-line:no-any
(window as any).FMS_INSIGHT_DEMO_MODE = true;
import { initStore } from '../../store/store';
import Dashboard from './Dashboard';
import LoadingIcon from '../LoadingIcon';
import { differenceInSeconds, addDays } from "date-fns";
import { loadMockData } from "../../mock-data/load";

it("renders the dashboard", async () => {
  const store = initStore();

  const result = render(
    <Provider store={store}>
      <div>
        <LoadingIcon />
        <Dashboard />
      </div>
    </Provider>
  );

  expect(result.getByTestId("loading-icon")).toBeInTheDocument();

  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(addDays(new Date(2018, 7, 5, 15, 33, 0), -28), jan18);

  // tslint:disable-next-line:no-any
  (window as any).FMS_INSIGHT_RESOLVE_MOCK_DATA(
    loadMockData(offsetSeconds)
  );

  await wait(() =>
    expect(result.queryByTestId("loading-icon")).not.toBeInTheDocument()
  );

  expect(result.container.querySelector("div.rv-xy-plot")).toMatchSnapshot("current jobs plot");
  expect(result.getByTestId("stationoee-container")).toMatchSnapshot("station oee");
});