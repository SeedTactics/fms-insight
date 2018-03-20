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
import { connect } from 'react-redux';
import * as jdenticon from 'jdenticon';
import Paper from 'material-ui/Paper';
import Typography from 'material-ui/Typography';
import ButtonBase from 'material-ui/ButtonBase';
import Button from 'material-ui/Button';
import Tooltip from 'material-ui/Tooltip';
import WarningIcon from 'material-ui-icons/Warning';
import Avatar from 'material-ui/Avatar';
import { CircularProgress } from 'material-ui/Progress';
import Dialog, {
  DialogActions,
  DialogContent,
  DialogTitle,
} from 'material-ui/Dialog';

import * as api from '../../data/api';
import * as matDetails from '../../data/material-details';
import LogEntry from '../LogEntry';
import { Store } from '../../data/store';

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

export function PartIdenticon({part}: {part: string}) {
  const iconSize = 50;
  // tslint:disable-next-line:no-any
  const icon = (jdenticon as any).toSvg(part, iconSize);

  return (
    <div
      style={{width: iconSize, height: iconSize}}
      dangerouslySetInnerHTML={{__html: icon}}
    />
  );
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

export interface MaterialProps {
  readonly mat: Readonly<api.IInProcessMaterial>; // TODO: deep readonly
  // tslint:disable-next-line:no-any
  onOpen: (m: Readonly<api.IInProcessMaterial>) => any;
}

export function Material(props: MaterialProps) {
  const action = materialAction(props.mat);
  const inspections = props.mat.signaledInspections.join(", ");

  return (
    <Paper elevation={4} style={{minWidth: '10em', padding: '8px'}}>
      <ButtonBase focusRipple onClick={() => props.onOpen(props.mat)}>
        <div style={{display: 'flex', textAlign: 'left'}}>
          <PartIdenticon part={props.mat.partName}/>
          <div style={{marginLeft: '8px', flexGrow: 1}}>
            <Typography variant="title">
              {props.mat.partName}
            </Typography>
            <div>
              <small>Serial: {props.mat.serial ? props.mat.serial : "none"}</small>
            </div>
            {
              props.mat.workorderId === undefined ? undefined :
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
          <div style={{marginLeft: '4px'}}>
            {props.mat.serial && props.mat.serial.length >= 1 ?
              <Avatar style={{width: "30px", height: "30px"}}>
                {props.mat.serial.substr(props.mat.serial.length - 1, 1)}
              </Avatar>
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
}

export interface MaterialEventProps {
  events: ReadonlyArray<Readonly<api.ILogEntry>>;
}

export function MaterialEvents(props: MaterialEventProps) {
  return (
    <ul style={{'list-style': 'none'}}>
      {
        props.events.map(e => (
          <li key={e.counter}>
            <LogEntry entry={e}/>
          </li>
        ))
      }
    </ul>
  );
}

export interface MaterialDialogProps extends matDetails.State {
  // tslint:disable-next-line:no-any
  onClose: () => any;
}

export function MaterialDialog(props: MaterialDialogProps) {
  let body: JSX.Element | undefined;
  if (props.display_material === undefined) {
    body = <p>None</p>;
  } else {
    const mat = props.display_material;
    body = (
      <>
        <DialogTitle disableTypography>
          <div style={{display: 'flex', textAlign: 'left'}}>
            <PartIdenticon part={mat.partName}/>
            <div style={{marginLeft: '8px', flexGrow: 1}}>
              <Typography variant="title">
                {mat.partName}
              </Typography>
            </div>
          </div>
        </DialogTitle>
        <DialogContent>
          <div>
            <small>Serial: {mat.serial || "none"}</small>
          </div>
          <div>
            <small>Workorder: {mat.workorderId || "none"}</small>
          </div>
          <div>
            <small>{materialAction(mat)}</small>
          </div>
          <div>
              {
                mat.signaledInspections.length === 0 ?
                  <small>Inspections: none</small> :
                  <small style={{color: "#F44336"}}>
                    Inspections: {mat.signaledInspections.length === 0 ? "none" : mat.signaledInspections.join(", ")}
                  </small>
              }
          </div>
          {props.loading_events ? <CircularProgress color="secondary"/> : <MaterialEvents events={props.events}/>}
        </DialogContent>
        <DialogActions>
          <Button onClick={props.onClose} color="primary">
            Close
          </Button>
        </DialogActions>
      </>
    );
  }
  return (
    <Dialog
      open={props.display_material !== undefined}
      onClose={props.onClose}
      maxWidth="md"
    >
      {body}
    </Dialog>

  );
}

export const ConnectedMaterialDialog = connect(
  (st: Store) => st.MaterialDetails,
  {
    onClose: () => ({
      type: matDetails.ActionType.CloseMaterialDialog
    }),
  }
)(MaterialDialog);