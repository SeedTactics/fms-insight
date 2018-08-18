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
import SearchIcon from '@material-ui/icons/Search';
import Card from '@material-ui/core/Card';
import CardContent from '@material-ui/core/CardContent';
import CardHeader from '@material-ui/core/CardHeader';
import Select from '@material-ui/core/Select';
import MenuItem from '@material-ui/core/MenuItem';
import { Sankey, Hint } from 'react-vis';

import { PartIdenticon } from '../station-monitor/Material';
import { connect } from '../../store/store';
import {
  SankeyNode,
  SankeyDiagram,
  inspectionDataToSankey,
} from '../../data/inspection-sankey';

import * as events from '../../data/events';
import {
  PartAndInspType,
  mkPartAndInspType,
  InspectionLogEntry,
} from '../../data/events.inspection';

interface InspectionSankeyDiagramProps {
  readonly sankey: SankeyDiagram;
}

// d3 adds some properties to each node
interface D3SankeyNode extends SankeyNode {
  readonly x0: number;
  readonly y0: number;
  readonly x1: number;
  readonly y1: number;
}

// d3 adds some properties to each link
interface D3Link {
  readonly source: D3SankeyNode;
  readonly target: D3SankeyNode;
  readonly value: number;

  readonly index: number;
  readonly x0: number;
  readonly y0: number;
  readonly x1: number;
  readonly y1: number;
}

interface InspectionSankeyDiagramState {
  readonly activeLink?: D3Link;
}

class InspectionSankeyDiagram
    extends React.PureComponent<InspectionSankeyDiagramProps, InspectionSankeyDiagramState> {
  state: InspectionSankeyDiagramState = {};

  renderHint() {
    const {activeLink} = this.state;
    if (!activeLink) { return; }

    // calculate center x,y position of link for positioning of hint
    const x = activeLink.source.x1 + ((activeLink.target.x0 - activeLink.source.x1) / 2);
    const y = activeLink.y0 - ((activeLink.y0 - activeLink.y1) / 2);

    const hintValue = {
      [`${activeLink.source.name} ➞ ${activeLink.target.name}`]: activeLink.value.toString() + " parts"
    };

    return (
      <Hint x={x} y={y} value={hintValue} />
    );
  }

  render() {
    // d3-sankey mutates nodes and links array, so create copy
    return (
      <Sankey
        nodes={this.props.sankey.nodes.map(d => ({...d}))}
        links={this.props.sankey.links.map((l, idx) => ({...l,
          opacity: this.state.activeLink && this.state.activeLink.index === idx ? 0.6 : 0.3
        }))}
        width={window.innerWidth - 300}
        height={window.innerHeight - 200}
        onLinkMouseOver={(link: D3Link) => this.setState({activeLink: link})}
        onLinkMouseOut={() => this.setState({activeLink: undefined})}
      >
        {this.state.activeLink ? this.renderHint() : undefined}
      </Sankey>
    );
  }
}

// use purecomponent to only recalculate the SankeyDiagram when the InspectionData changes.
class ConvertInspectionDataToSankey extends React.PureComponent<{data: ReadonlyArray<InspectionLogEntry>}> {
  render() {
    return (
      <InspectionSankeyDiagram sankey={inspectionDataToSankey(this.props.data)}/>
    );
  }
}

interface InspectionSankeyProps {
  readonly inspectionlogs: im.Map<PartAndInspType, ReadonlyArray<InspectionLogEntry>>;
}

interface InspectionSankeyState {
  readonly selectedPart?: string;
  readonly selectedInspectType?: string;
}

class InspectionSankey extends React.Component<InspectionSankeyProps, InspectionSankeyState> {
  state: InspectionSankeyState = {};

  render() {
    let curData: ReadonlyArray<InspectionLogEntry> | undefined;
    if (this.state.selectedPart && this.state.selectedInspectType) {
      curData = this.props.inspectionlogs.get(
        mkPartAndInspType({
          part: this.state.selectedPart,
          inspType: this.state.selectedInspectType
        })
      );
    }
    const parts = this.props.inspectionlogs.keySeq().map(e => e.get("part", "")).toSet().toSeq().sort();
    const inspTypes = this.props.inspectionlogs.keySeq().map(e => e.get("inspType", "")).toSet().toSeq().sort();
    return (
      <Card raised>
        <CardHeader
          title={
            <div style={{display: 'flex', flexWrap: 'wrap', alignItems: 'center'}}>
              <SearchIcon/>
              <div style={{marginLeft: '10px', marginRight: '3em'}}>
                Inspections
              </div>
              <div style={{flexGrow: 1}}/>
              <Select
                autoWidth
                displayEmpty
                style={{marginRight: '1em'}}
                value={this.state.selectedInspectType || ""}
                onChange={e => this.setState({selectedInspectType: e.target.value})}
              >
                {
                  this.state.selectedInspectType ? undefined :
                    <MenuItem key={0} value=""><em>Select Inspection Type</em></MenuItem>
                }
                {
                  inspTypes.map(n =>
                    <MenuItem key={n} value={n}>
                      {n}
                    </MenuItem>
                  )
                }
              </Select>
              <Select
                autoWidth
                displayEmpty
                value={this.state.selectedPart || ""}
                onChange={e => this.setState({selectedPart: e.target.value})}
              >
                {
                  this.state.selectedPart ? undefined :
                    <MenuItem key={0} value=""><em>Select Part</em></MenuItem>
                }
                {
                  parts.map(n =>
                    <MenuItem key={n} value={n}>
                      <div style={{display: "flex", alignItems: "center"}}>
                        <PartIdenticon part={n} size={30}/>
                        <span style={{marginRight: '1em'}}>{n}</span>
                      </div>
                    </MenuItem>
                  )
                }
              </Select>
            </div>}
        />
        <CardContent>
          <div style={{display: 'flex', justifyContent: 'center'}}>
            { curData ?
              <ConvertInspectionDataToSankey data={curData}/>
              : undefined
            }
          </div>
        </CardContent>
      </Card>
    );
  }
}

export default connect(
  (st => ({
    inspectionlogs:
      st.Events.analysis_period === events.AnalysisPeriod.Last30Days
      ? st.Events.last30.inspection.by_part
      : st.Events.selected_month.inspection.by_part,
  }))
)(InspectionSankey);