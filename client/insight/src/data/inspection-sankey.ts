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

import * as api from "./api";
import { InspectionLogResultType, InspectionLogEntry } from "./events.inspection";
import { fieldsHashCode } from "prelude-ts";
import { LazySeq } from "./lazyseq";

export interface SankeyNode {
  readonly unique: string; // full unique of node
  readonly name: string; // what to display for the node
}

export interface SankeyLink {
  readonly source: number;
  readonly target: number;
  readonly value: number;
}

export interface SankeyDiagram {
  readonly nodes: ReadonlyArray<SankeyNode>;
  readonly links: ReadonlyArray<SankeyLink>;
}

class NodeR {
  public constructor(public readonly unique: string, public readonly name: string) {}
  equals(other: NodeR): boolean {
    return this.unique === other.unique && this.name === other.name;
  }
  hashCode(): number {
    return fieldsHashCode(this.unique, this.name);
  }
  toString(): string {
    return `{unique: ${this.unique}}, name: ${this.name}}`;
  }
}

class Edge {
  public constructor(public readonly from: NodeR, public readonly to: NodeR) {}
  equals(other: Edge): boolean {
    return this.from.equals(other.from) && this.to.equals(other.to);
  }
  hashCode(): number {
    return fieldsHashCode(this.from.hashCode(), this.to.hashCode());
  }
  toString(): string {
    return `{from: ${this.from.toString()}}, to: ${this.to.toString()}}`;
  }
}

function edgesForPath(
  actualPath: ReadonlyArray<Readonly<api.IMaterialProcessActualPath>>,
  toInspect: boolean,
  result: boolean | undefined
): Edge[] {
  let path = "";
  let prevNode = new NodeR("", "raw");
  const edges: Edge[] = [];
  for (const proc of actualPath) {
    for (const stop of proc.stops) {
      const cur = "P" + proc.pallet + ",M" + stop.stationNum;
      path += "->" + cur;
      const nextNode = new NodeR(path, cur);
      edges.push(new Edge(prevNode, nextNode));
      prevNode = nextNode;
    }
  }

  if (toInspect && result !== undefined) {
    if (result) {
      edges.push(new Edge(prevNode, new NodeR("@@success", "success")));
    } else {
      edges.push(new Edge(prevNode, new NodeR("@@failed", "failed")));
    }
  } else {
    edges.push(new Edge(prevNode, new NodeR("@@uninspected", "uninspected")));
  }

  return edges;
}

export function inspectionDataToSankey(d: ReadonlyArray<InspectionLogEntry>): SankeyDiagram {
  const matIdToInspResult = LazySeq.ofIterable(d)
    .filter(e => e.result.type === InspectionLogResultType.Completed)
    .toMap(
      e => [e.materialID, e.result.type === InspectionLogResultType.Completed ? e.result.success : false],
      (v1, v2) => v2 // take the later value
    );

  // create all the edges, likely with duplicate edges between nodes
  const edges = LazySeq.ofIterable(d).flatMap(c => {
    if (c.result.type === InspectionLogResultType.Triggered) {
      return edgesForPath(
        c.result.actualPath,
        c.result.toInspect,
        matIdToInspResult.get(c.materialID).getOrElse(false)
      );
    } else {
      return [];
    }
  });

  // extract the nodes and assign an index
  const nodes = edges
    .flatMap(e => [e.from, e.to])
    .toSet(x => x)
    .transform(s => LazySeq.ofIterable(s))
    .map((node, idx) => ({ idx, node }));

  // create the sankey nodes to return
  const sankeyNodes = nodes
    .map(s => ({
      unique: s.node.unique,
      name: s.node.name
    }))
    .toArray();

  // create a map from NodeR to index
  const nodesToIdx = nodes.toMap(
    n => [n.node, n.idx],
    (i1, _) => i1
  );

  // create the sankey links to return by counting Edges between nodes
  const sankeyLinks = edges
    .toMap(
      e => [e, 1],
      (c1, c2) => c1 + c2
    )
    .transform(LazySeq.ofIterable)
    .map(([link, value]) => ({
      source: nodesToIdx.get(link.from).getOrThrow(),
      target: nodesToIdx.get(link.to).getOrThrow(),
      value
    }))
    .toArray();

  return {
    nodes: sankeyNodes,
    links: sankeyLinks
  };
}
