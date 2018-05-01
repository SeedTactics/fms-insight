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

import * as ccp from './cost-per-piece';

it("loads the initial state", () => {
  // tslint:disable no-any
  let s = ccp.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(ccp.initial);
});

it("sets machine costs", () => {
  let s = ccp.reducer(ccp.initial, {
    type: ccp.ActionType.SetMachineCostPerYear,
    group: "g1",
    cost: 12
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    machineCostPerYear: {
      "g1": 12
    }
  }});

  s = ccp.reducer(s, {
    type: ccp.ActionType.SetMachineCostPerYear,
    group: "g2",
    cost: 44
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    machineCostPerYear: {
      "g1": 12,
      "g2": 44
    }
  }});

  s = ccp.reducer(s, {
    type: ccp.ActionType.SetMachineCostPerYear,
    group: "g2",
    cost: 150
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    machineCostPerYear: {
      "g1": 12,
      "g2": 150
    }
  }});
});

it("sets part material cost", () => {
  let s = ccp.reducer(ccp.initial, {
    type: ccp.ActionType.SetPartMaterialCost,
    part: "p1",
    cost: 52
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    partMaterialCost: {
      "p1": 52
    }
  }});

  s = ccp.reducer(s, {
    type: ccp.ActionType.SetPartMaterialCost,
    part: "p2",
    cost: 2
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    partMaterialCost: {
      "p1": 52,
      "p2": 2,
    }
  }});

  s = ccp.reducer(s, {
    type: ccp.ActionType.SetPartMaterialCost,
    part: "p2",
    cost: 63
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    partMaterialCost: {
      "p1": 52,
      "p2": 63,
    }
  }});
});

it("sets the number of operators", () => {
  let s = ccp.reducer(ccp.initial, {
    type: ccp.ActionType.SetNumOperators,
    numOpers: 12
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    numOperators: 12,
  }});
});

it("sets the operator cost per hour", () => {
  let s = ccp.reducer(ccp.initial, {
    type: ccp.ActionType.SetOperatorCostPerHour,
    cost: 67
  });
  expect(s).toEqual({input: {...ccp.initial.input,
    operatorCostPerHour: 67,
  }});
});