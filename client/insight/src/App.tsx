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
import AppBar from 'material-ui/AppBar/AppBar';
import Tabs, { Tab } from 'material-ui/Tabs';
import Typography from 'material-ui/Typography/Typography';
import Toolbar from 'material-ui/Toolbar/Toolbar';
import Hidden from 'material-ui/Hidden/Hidden';
import { Route, Link } from 'react-router-dom';

import Dashboard from './components/Dashboard';
import CostPerPiece from './components/CostPerPiece';
import Efficiency from './components/Efficiency';
import StationMonitor from './components/StationMonitor';
import LoadingIcon from './components/LoadingIcon';
import { requestLastWeek } from './data/events';
import store from './data/store';

// tslint:disable
const LinkTab: any = Tab as any;
// tslint:enable

const tabsStyle = {
  'align-self': 'flex-end',
  'flex-grow': '1'
};

class App extends React.Component {
  componentDidMount() {
    store.dispatch(requestLastWeek());
  }

  render() {
    const tabs = (full: boolean) => (
      <Tabs fullWidth={full} style={full ? {} : tabsStyle} value={location.pathname} onChange={(e, v) => v}>
        <LinkTab label="Dashboard" component={Link} to="/" value="/"/>
        <LinkTab label="Station Monitor" component={Link} to="/station" value="/station"/>
        <LinkTab label="Cost/Piece" component={Link} to="/cost" value="/cost"/>
        <LinkTab label="Efficiency" component={Link} to="/efficiency" value="/efficiency"/>
      </Tabs>
    );

    const largeAppBar = (
      <AppBar position="static">
        <Toolbar>
          <Typography type="title" style={{'margin-right': '2em'}}>Insight</Typography>
          {tabs(false)}
          <LoadingIcon/>
        </Toolbar>
      </AppBar>
    );

    const smallAppBar = (
      <AppBar position="static">
        <Toolbar>
          <Typography type="title">Insight</Typography>
          <div style={{'flex-grow': '1'}}/>
          <LoadingIcon/>
        </Toolbar>
        {tabs(true)}
      </AppBar>
    );

    return (
      <div className="App">
        <Hidden smDown>
          {largeAppBar}
        </Hidden>
        <Hidden mdUp>
          {smallAppBar}
        </Hidden>
        <main style={{margin: '8px'}}>
          <Route exact path="/" component={Dashboard}/>
          <Route exact path="/station" component={StationMonitor}/>
          <Route exact path="/cost" component={CostPerPiece}/>
          <Route exact path="/efficiency" component={Efficiency}/>
        </main>
      </div>
    );
  }
}

export default App;