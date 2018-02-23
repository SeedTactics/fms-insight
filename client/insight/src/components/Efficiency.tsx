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
import Radio from 'material-ui/Radio';
import TextField from 'material-ui/TextField';
import { FormControlLabel } from 'material-ui/Form';

const toolbarStyle = {
  'display': 'flex',
  'backgroundColor': '#E0E0E0',
  'paddingLeft': '24px',
  'paddingRight': '24px',
  'minHeight': '2.5em',
  'alignItems': 'center' as 'center',
  'justifyContent': 'space-evenly' as 'space-evenly'
};

function SecondaryToolbar() {
  return (
    <nav style={toolbarStyle}>
      <FormControlLabel
        control={<Radio/>}
        label="Last 30 days"
      />
      <div>
        <FormControlLabel
          control={<Radio/>}
          label="Select Month"
        />
        <TextField type="month"/>
      </div>
    </nav>
  );
}

export default function Efficiency() {
  return (
    <>
      <SecondaryToolbar/>
      <main style={{'padding': '8px'}}>
        <p>Efficiency Page</p>
      </main>
    </>
  );
}