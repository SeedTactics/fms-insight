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
import * as jdenticon from 'jdenticon';
import Typography from 'material-ui/Typography';
import ButtonBase from 'material-ui/ButtonBase';
import Tooltip from 'material-ui/Tooltip';
import WarningIcon from '@material-ui/icons/Warning';
import CheckmarkIcon from '@material-ui/icons/Check';
import Avatar from 'material-ui/Avatar';
import Paper from 'material-ui/Paper';
import { CircularProgress } from 'material-ui/Progress';
import { distanceInWordsToNow } from 'date-fns';

import * as im from 'immutable';

import * as api from '../../data/api';
import * as matDetails from '../../data/material-details';
import { LogEntries } from '../LogEntry';
import { MaterialSummary } from '../../data/events';
import { withStyles } from 'material-ui';

/*
function getPosition(el: Element) {
  const box = el.getBoundingClientRect();
  const doc = document.documentElement;
  const body = document.body;
  var clientTop  = doc.clientTop  || body.clientTop  || 0;
  var clientLeft = doc.clientLeft || body.clientLeft || 0;
  var scrollTop  = window.pageYOffset || doc.scrollTop;
  var scrollLeft = window.pageXOffset || doc.scrollLeft;
  return {
    top: box.top  + scrollTop  - clientTop,
    left: box.left + scrollLeft - clientLeft
  };
}*/

export class PartIdenticon extends React.PureComponent<{part: string}> {
  render() {
    const iconSize = 50;
    // tslint:disable-next-line:no-any
    const icon = (jdenticon as any).toSvg(this.props.part, iconSize);

    return (
      <div
        style={{width: iconSize, height: iconSize}}
        dangerouslySetInnerHTML={{__html: icon}}
      />
    );
  }
}

function materialAction(mat: Readonly<api.IInProcessMaterial>): string | undefined {
  switch (mat.action.type) {
    case api.ActionType.Loading:
      switch (mat.location.type) {
        case api.LocType.OnPallet:
          return "Transfer to face " + mat.action.loadOntoFace.toString();
        default:
          return "Load onto face " + mat.action.loadOntoFace.toString();
      }
    case api.ActionType.UnloadToInProcess:
    case api.ActionType.UnloadToCompletedMaterial:
      if (mat.action.unloadIntoQueue) {
        return "Unload into queue " + mat.action.unloadIntoQueue;
      } else {
        return "Unload from pallet";
      }
  }
  return undefined;
}

const matStyles = withStyles(theme => ({
  paper: {
    minWidth: '10em',
    padding: '8px'
  },
  container: {
    display: 'flex' as 'flex',
    textAlign: 'left' as 'left',
  },
  mainContent: {
    marginLeft: '8px',
    flexGrow: 1,
  },
  rightContent: {
    marginLeft: '4px',
    display: 'flex',
    'flex-direction': 'column',
    'justify-content': 'space-between',
    'align-items': 'flex-end',
  },
  avatar: {
    width: '30px',
    height: '30px'
  }
}));

export interface InProcMaterialProps {
  readonly mat: Readonly<api.IInProcessMaterial>; // TODO: deep readonly
  onOpen: (m: Readonly<api.IInProcessMaterial>) => void;
}

const InProcMaterialWithStyles = matStyles<InProcMaterialProps>(props => {
  const action = materialAction(props.mat);
  const inspections = props.mat.signaledInspections.join(", ");

  return (
    <Paper elevation={4} className={props.classes.paper}>
      <ButtonBase focusRipple onClick={() => props.onOpen(props.mat)}>
        <div className={props.classes.container}>
          <PartIdenticon part={props.mat.partName}/>
          <div className={props.classes.mainContent}>
            <Typography variant="title">
              {props.mat.partName}
            </Typography>
            <div>
              <small>Serial: {props.mat.serial ? props.mat.serial : "none"}</small>
            </div>
            {
              props.mat.workorderId === undefined || props.mat.workorderId === "" ? undefined :
                <div>
                  <small>Workorder: {props.mat.workorderId}</small>
                </div>
            }
            {
              action === undefined ? undefined :
                <div>
                  <small>{action}</small>
                </div>
            }
          </div>
          <div className={props.classes.rightContent}>
            {props.mat.serial && props.mat.serial.length >= 1 ?
              <div>
                <Avatar className={props.classes.avatar}>
                  {props.mat.serial.substr(props.mat.serial.length - 1, 1)}
                </Avatar>
              </div>
              : undefined
            }
            {
              props.mat.signaledInspections.length === 0 ? undefined :
                <div>
                  <Tooltip title={inspections}>
                    <WarningIcon/>
                  </Tooltip>
                </div>
            }
          </div>
        </div>
      </ButtonBase>
    </Paper>
  );
});

// decorate doesn't work well with classes yet.
// https://github.com/Microsoft/TypeScript/issues/4881
export class InProcMaterial extends React.PureComponent<InProcMaterialProps> {
  render() {
    return <InProcMaterialWithStyles {...this.props}/>;
  }
}

export interface MaterialSummaryProps {
  readonly mat: Readonly<MaterialSummary>; // TODO: deep readonly
  readonly checkInspectionType?: string;
  readonly checkWashCompleted?: boolean;
  onOpen: (m: Readonly<MaterialSummary>) => void;
}

const MatSummaryWithStyles = matStyles<MaterialSummaryProps>(props => {
  let showInspCheckmark: boolean;
  if (props.checkInspectionType === undefined) {
    showInspCheckmark = false;
  } else if (props.checkInspectionType === "") {
    showInspCheckmark = im.Set(props.mat.signaledInspections).subtract(props.mat.completedInspections)
      .isEmpty();
  } else {
    showInspCheckmark = props.mat.completedInspections.indexOf(props.checkInspectionType) >= 0;
  }

  let showWashCheckmark: boolean;
  if (props.checkWashCompleted) {
    showWashCheckmark = props.mat.wash_completed !== undefined;
  } else {
    showWashCheckmark = false;
  }

  return (
    <Paper elevation={4} className={props.classes.paper}>
      <ButtonBase
        focusRipple
        onClick={() => props.onOpen(props.mat)}
      >
        <div className={props.classes.container}>
          <PartIdenticon part={props.mat.partName}/>
          <div className={props.classes.mainContent}>
            <Typography variant="title">
              {props.mat.partName}
            </Typography>
            <div>
              <small>Serial: {props.mat.serial ? props.mat.serial : "none"}</small>
            </div>
            {
              props.mat.workorderId === undefined || props.mat.workorderId === "" ? undefined :
                <div>
                  <small>Workorder: {props.mat.workorderId}</small>
                </div>
            }
            {
              props.mat.completed_time === undefined ? undefined :
                <div>
                  <small>Completed {distanceInWordsToNow(props.mat.completed_time, {addSuffix: true})}</small>
                </div>
            }
            {
              props.mat.signaledInspections.length === 0 ? undefined :
                <div>
                  <small>Inspections: </small>
                  {
                    props.mat.signaledInspections.map((type, i) => (
                      <span key={i}>
                        <small>{i === 0 ? type : ", " + type}</small>
                      </span>
                    ))
                  }
                </div>
            }
          </div>
          <div className={props.classes.rightContent}>
            {props.mat.serial && props.mat.serial.length >= 1 ?
              <div>
                <Avatar className={props.classes.avatar}>
                  {props.mat.serial.substr(props.mat.serial.length - 1, 1)}
                </Avatar>
              </div>
              : undefined
            }
            {
              showInspCheckmark || showWashCheckmark ?
                <div>
                  <CheckmarkIcon/>
                </div>
                : undefined
            }
          </div>
        </div>
      </ButtonBase>
    </Paper>
  );
});

// decorate doesn't work well with classes yet.
// https://github.com/Microsoft/TypeScript/issues/4881
export class MatSummary extends React.PureComponent<MaterialSummaryProps> {
  render() {
    return <MatSummaryWithStyles {...this.props}/>;
  }
}

export class MaterialDetailTitle extends React.PureComponent<{partName: string, serial?: string}> {
  render () {
    return (
      <div style={{display: 'flex', textAlign: 'left'}}>
        <PartIdenticon part={this.props.partName}/>
        <div style={{marginLeft: '8px', flexGrow: 1}}>
          <Typography variant="title">
            { this.props.partName +
              (this.props.serial === undefined || this.props.serial === "" ? "" : " - " + this.props.serial)
            }
          </Typography>
        </div>
      </div>
    );
  }
}

export interface MaterialDetailProps {
  readonly mat: matDetails.MaterialDetail;
}

export class MaterialDetailContent extends React.PureComponent<MaterialDetailProps> {
  render () {
    const mat = this.props.mat;
    function colorForInspType(type: string): string {
      if (mat.completedInspections && mat.completedInspections.indexOf(type) >= 0) {
        return "black";
      } else {
        return "red";
      }
    }
    return (
      <>
        <div style={{marginLeft: '1em'}}>
          <div>
            <small>Workorder: {mat.workorderId || "none"}</small>
          </div>
          <div>
            <small>Inspections: </small>
              {
                mat.signaledInspections.length === 0
                  ? <small>none</small>
                  :
                  mat.signaledInspections.map((type, i) => (
                    <span key={i}>
                      <small>{i === 0 ? "" : ", "}</small>
                      <small style={{color: colorForInspType(type)}}>
                        {type}
                      </small>
                    </span>
                  ))
              }
          </div>
        </div>
        {mat.loading_events ? <CircularProgress color="secondary"/> : <LogEntries entries={mat.events}/>}
      </>
    );
  }
}
