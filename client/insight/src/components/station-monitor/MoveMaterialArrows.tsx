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

import * as React from "react";
import {
  MoveMaterialArrow,
  MoveMaterialArrowData,
  computeArrows,
  MoveMaterialIdentifier,
  MoveMaterialNodeKind,
  MoveArrowElemRect,
} from "../../data/move-arrows";
import { emptyIMap } from "../../util/imap";

class MoveMaterialArrows extends React.PureComponent<MoveMaterialArrowData<Element>> {
  static elementToRect(e: Element): MoveArrowElemRect {
    const r = e.getBoundingClientRect();
    return {
      left: r.left + window.scrollX,
      top: r.top + window.scrollY,
      width: r.width,
      height: r.height,
      bottom: r.bottom + window.scrollY,
      right: r.right + window.scrollX,
    };
  }

  static arrowToPath(arr: MoveMaterialArrow): string {
    // mid point of line
    const mpx = (arr.fromX + arr.toX) / 2;
    const mpy = (arr.fromY + arr.toY) / 2;

    // angle of perpendicular to line
    const theta = Math.atan2(arr.toY - arr.fromY, arr.toX - arr.fromX) + (Math.PI * arr.curveDirection) / 2;

    // control points
    const cx = mpx + 50 * Math.cos(theta);
    const cy = mpy + 50 * Math.sin(theta);

    return `M${arr.fromX},${arr.fromY} Q ${cx} ${cy} ${arr.toX} ${arr.toY}`;
  }

  render() {
    const data: MoveMaterialArrowData<MoveArrowElemRect> = {
      container: this.props.container !== null ? MoveMaterialArrows.elementToRect(this.props.container) : null,
      nodes: this.props.nodes.toLazySeq().toIMap(([k, e]) => [k, MoveMaterialArrows.elementToRect(e)]),
      node_type: this.props.node_type,
    };
    const arrows = computeArrows(data);

    return (
      <g>
        {arrows.map((arr, idx) => (
          <path
            key={idx}
            style={{ fill: "none", stroke: "rgba(0,0,0,0.5)", strokeWidth: 2 }}
            d={MoveMaterialArrows.arrowToPath(arr)}
            markerEnd={`url(#arrow)`}
          />
        ))}
      </g>
    );
  }
}

interface MoveMaterialArrowContext {
  readonly registerNode: (id: MoveMaterialIdentifier) => (ref: Element | null) => void;
  readonly registerNodeKind: (id: MoveMaterialIdentifier, kind: MoveMaterialNodeKind | null) => void;
}
const MoveMaterialArrowCtx = React.createContext<MoveMaterialArrowContext | undefined>(undefined);

export class MoveMaterialArrowContainer extends React.PureComponent<
  { readonly children: JSX.Element },
  MoveMaterialArrowData<Element>
> {
  state = {
    container: null,
    nodes: emptyIMap<MoveMaterialIdentifier, Element>(),
    node_type: emptyIMap<MoveMaterialIdentifier, MoveMaterialNodeKind>(),
  } as MoveMaterialArrowData<Element>;

  readonly ctx: MoveMaterialArrowContext | undefined = undefined;

  constructor(props: { readonly children: JSX.Element }) {
    super(props);
    this.ctx = {
      registerNode: this.registerNode.bind(this),
      registerNodeKind: this.registerNodeKind.bind(this),
    };
  }

  registerNode(id: MoveMaterialIdentifier) {
    return (ref: Element | null): void => {
      if (ref) {
        this.setState((s) => ({ nodes: s.nodes.set(id, ref) }));
      } else {
        this.setState((s) => ({
          nodes: s.nodes.delete(id),
          node_type: s.node_type.delete(id),
        }));
      }
    };
  }

  registerNodeKind(id: MoveMaterialIdentifier, kind: MoveMaterialNodeKind | null): void {
    if (kind) {
      this.setState((s) => ({ node_type: s.node_type.set(id, kind) }));
    } else {
      this.setState((s) => ({ node_type: s.node_type.delete(id) }));
    }
  }

  render(): JSX.Element {
    return (
      <div style={{ position: "relative" }}>
        <svg
          style={{
            position: "absolute",
            width: "100%",
            height: "100%",
            top: 0,
            right: 0,
          }}
        >
          <defs>
            <marker
              id="arrow"
              markerWidth={6}
              markerHeight={10}
              refX="0"
              refY="3"
              orient="auto"
              markerUnits="strokeWidth"
            >
              <path d="M0,0 L0,6 L5,3 z" fill="rgba(0,0,0,0.5)" />
            </marker>
          </defs>
          <MoveMaterialArrows {...this.state} />
        </svg>
        <div ref={(r) => this.setState({ container: r })}>
          <MoveMaterialArrowCtx.Provider value={this.ctx}>{this.props.children}</MoveMaterialArrowCtx.Provider>
        </div>
      </div>
    );
  }
}

interface MoveMaterialArrowNodeHelperProps {
  readonly kind: MoveMaterialNodeKind;
  readonly ctx: MoveMaterialArrowContext;
}

class MoveMaterialArrowNodeHelper extends React.PureComponent<
  MoveMaterialArrowNodeHelperProps & { children?: JSX.Element }
> {
  readonly ident = MoveMaterialIdentifier.allocateNodeId();

  constructor(props: MoveMaterialArrowNodeHelperProps) {
    super(props);
    props.ctx.registerNodeKind(this.ident, props.kind);
  }

  componentDidUpdate(oldProps: MoveMaterialArrowNodeHelperProps) {
    if (oldProps.kind !== this.props.kind) {
      this.props.ctx.registerNodeKind(this.ident, this.props.kind);
    }
  }

  render() {
    return <div ref={this.props.ctx.registerNode(this.ident)}>{this.props.children}</div>;
  }
}

export class MoveMaterialArrowNode extends React.PureComponent<MoveMaterialNodeKind & { children?: JSX.Element }> {
  render(): JSX.Element {
    const { children, ...kind } = this.props;
    return (
      <MoveMaterialArrowCtx.Consumer>
        {(ctx) =>
          ctx === undefined ? undefined : (
            <MoveMaterialArrowNodeHelper ctx={ctx} kind={kind}>
              {children}
            </MoveMaterialArrowNodeHelper>
          )
        }
      </MoveMaterialArrowCtx.Consumer>
    );
  }
}
