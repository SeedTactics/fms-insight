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
import * as im from 'immutable';
import * as api from '../../data/api';

export type MoveMaterialDestination = string;
export type MoveMaterialIdentifier = string;

export interface MoveMaterialArrowData<T> {
  readonly container: T | null;
  readonly material: im.Map<MoveMaterialIdentifier, T>;
  readonly destinations: im.Map<MoveMaterialDestination, T>;
  readonly actions: im.Map<MoveMaterialIdentifier, Readonly<api.IInProcessMaterialAction>>;
}

export interface MoveMaterialArrow {
  readonly fromX: number;
  readonly fromY: number;
  readonly toX: number;
  readonly toY: number;
}

export function computeArrows(data: MoveMaterialArrowData<ClientRect>): ReadonlyArray<MoveMaterialArrow> {
  return [];
}

export function arrowToPath(arr: MoveMaterialArrow): string {
  const startControlX = 0;
  const startControlY = 0;
  const endControlX = 0;
  const endControlY = 0;
  const curve = `C ${startControlX},${startControlY} ${endControlX},${endControlY} ${arr.toX},${arr.fromX}`;

  return `M${arr.fromX},${arr.fromY} ` + curve;
}

function elementToRect(e: Element): ClientRect {
  var r = e.getBoundingClientRect();
  return {
    left: r.left + window.scrollX,
    top: r.top + window.scrollY,
    width: r.width,
    height: r.height,
    bottom: r.bottom + window.scrollY,
    right: r.right + window.scrollX,
  };
}

type MoveMaterialArrowContainerState = MoveMaterialArrowData<Element>;

export class MoveMaterialArrowContainer extends React.Component<{}, MoveMaterialArrowContainerState> {
  state = {
    container: null,
    material: im.Map<MoveMaterialIdentifier, Element>(),
    actions: im.Map<MoveMaterialIdentifier, Readonly<api.IInProcessMaterialAction>>(),
    destinations: im.Map<MoveMaterialDestination, Element>(),
  } as MoveMaterialArrowContainerState;

  registerMaterial = (id: MoveMaterialIdentifier) => (ref: Element | null) => {
    if (ref) {
      this.setState({material: this.state.material.set(id, ref)});
    } else {
      this.setState({material: this.state.material.remove(id)});
    }
  }

  registerAction = (id: MoveMaterialIdentifier, action: Readonly<api.IInProcessMaterialAction> | null) => {
    if (action) {
      this.setState({actions: this.state.actions.set(id, action)});
    } else {
      this.setState({actions: this.state.actions.remove(id)});
    }
  }

  registerDestination = (id: MoveMaterialDestination) => (ref: Element | null) => {
    if (ref) {
      this.setState({destinations: this.state.destinations.set(id, ref)});
    } else {
      this.setState({destinations: this.state.destinations.remove(id)});
    }
  }

  getChildContext = () => ({
    registerMaterial: this.registerMaterial,
    registerAction: this.registerAction,
    registerDestination: this.registerDestination,
  })

  arrowElements(): JSX.Element {
    const elemData = this.state;
    const data: MoveMaterialArrowData<ClientRect> = {
      container: elemData.container !== null ? elementToRect(elemData.container) : null,
      material: elemData.material.map(elementToRect),
      destinations: elemData.destinations.map(elementToRect),
      actions: elemData.actions
    };
    const arrows = computeArrows(data);

    return (
      <>
        {arrows.map((arr, idx) =>
          <path
            key={idx}
            style={{fill: "none", stroke: "rgba(0,0,0,0.5)", strokeWidth: 5}}
            d={arrowToPath(arr)}
            markerEnd={`url(${location.href}#arrow)`}
          />
        )}
      </>
    );
  }

  render() {
    const arrowPath = "M0,0 L0,10 L6,5 z";
    const arrows = this.arrowElements();
    return (
      <div style={{position: "relative"}}>
        <svg style={{position: 'absolute', width: '100%', height: '100%', top: 0, right: 0}}>
          <defs>
            <marker
              id="arrow"
              markerWidth={10}
              markerHeight={6}
              refX="0"
              refY={5}
              orient="auto"
              markerUnits="strokeWidth"
            >
              <path d={arrowPath} fill="rgba(0,0,0,0.5)" />
            </marker>
          </defs>
          {arrows}
        </svg>
        <div ref={r => this.setState({container: r})}>
          {this.props.children}
        </div>
      </div>
    );
  }
}