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

import * as im from 'immutable';
import { InspectionLogResultType, InspectionLogEntry, InspectionCounter } from './events.inspection';

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

type NodeR = im.Record<{unique: string, name: string}>;
const mkNodeR = im.Record({unique: "", name: ""});

const mkEdgeR = im.Record({from: 0, to: 0});

interface Edge {
  readonly from: NodeR;
  readonly to: NodeR;
}

function edgesForCounter(counter: InspectionCounter, toInspect: boolean, result: boolean | undefined): Edge[] {
  let path = "";
  let prevNode = mkNodeR({unique: "", name: "start"});
  let edges: Edge[] = [];
  const numProc = Math.max(counter.pallets.length, counter.stations.length);
  for (let i = 0; i < numProc; i++) {
    let cur: string | undefined;
    if (i < counter.pallets.length && i < counter.stations.length) {
      cur = "P" + counter.pallets[i] + ",M" + counter.stations[i];
    } else if (i < counter.pallets.length) {
      cur = "P" + counter.pallets[i];
    } else if (i < counter.stations.length) {
      cur = "M" + counter.stations[i];
    }
    if (cur) {
      path += "->" + cur;
      const nextNode = mkNodeR({unique: path, name: cur});
      edges.push({
        from: prevNode,
        to: nextNode,
      });
      prevNode = nextNode;
    }
  }

  if (toInspect && result !== undefined) {
    if (result) {
      edges.push({
        from: prevNode,
        to: mkNodeR({unique: path + "->success", name: "success"})
      });
    } else {
      edges.push({
        from: prevNode,
        to: mkNodeR({unique: path + "->failed", name: "failed"})
      });
    }
  } else {
    edges.push({
      from: prevNode,
      to: mkNodeR({unique: path + "->uninspected", name: "uninspected"})
    });
  }

  return edges;
}

export function inspectionDataToSankey(d: ReadonlyArray<InspectionLogEntry>): SankeyDiagram {
  const entrySeq = im.Seq(d);

  const matIdToInspResult =
    entrySeq
    .filter(e => e.result.type === InspectionLogResultType.Completed)
    .toKeyedSeq()
    .mapKeys((idx, e) => e.materialID)
    .map(e => e.result.type === InspectionLogResultType.Completed ? e.result.success : false)
    .toMap()
    ;

  // create all the edges, likely with duplicate edges between nodes
  const edges =
    entrySeq
    .flatMap(c => {
      if (c.result.type === InspectionLogResultType.Triggered) {
        return edgesForCounter(c.result.counter, c.result.toInspect, matIdToInspResult.get(c.materialID));
      } else {
        return [];
      }
    })
    ;

  // extract the nodes and assign an index
  const nodes = edges
    .flatMap(e => [e.from, e.to])
    .toSet()
    .toIndexedSeq()
    .map((node, idx) => ({ idx, node }))
    ;

  // create the sankey nodes to return
  const sankeyNodes = nodes
    .map(s => ({
      unique: s.node.get("unique", ""),
      name: s.node.get("name", "")
    }))
    .toArray()
    ;

  // create a map from NodeR to index
  const nodesToIdx = nodes
    .toKeyedSeq()
    .mapKeys((key, n) => n.node)
    .map(n => n.idx)
    .toMap();

  // create the sankey links to return by counting Edges between nodes
  const sankeyLinks = edges
    .countBy(e => mkEdgeR({
      from: nodesToIdx.get(e.from, 0),
      to: nodesToIdx.get(e.to, 0),
    }))
    .map((value, link) => ({
      source: link.get("from", 0),
      target: link.get("to", 0),
      value,
    }))
    .valueSeq()
    .toArray()
    ;

  return {
    nodes: sankeyNodes,
    links: sankeyLinks,
  };

  /*
  return {
    nodes: [{name: 'a'}, {name: 'b'}, {name: 'c'}],
    links: [
      {source: 0, target: 1, value: 10},
      {source: 0, target: 2, value: 20},
      {source: 1, target: 2, value: 20}
    ],
  };*/
}