/* Copyright (c) 2019, John Lenz

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
import TextField from "@material-ui/core/TextField";
import Downshift from "downshift";
import Paper from "@material-ui/core/Paper";
import ArrowDropDownIcon from "@material-ui/icons/ArrowDropDown";
import List from "@material-ui/core/List";
import ListItem from "@material-ui/core/ListItem";
import ListItemText from "@material-ui/core/ListItemText";
import ListItemSecondaryAction from "@material-ui/core/ListItemSecondaryAction";
import IconButton from "@material-ui/core/IconButton";
import DeleteIcon from "@material-ui/icons/Delete";
import Typography from "@material-ui/core/Typography";
import { User } from "oidc-client";
import { HashSet } from "prelude-ts";

import { connect, DispatchAction, mkAC } from "../store/store";
import * as operators from "../data/operators";

interface OperatorSelectProps {
  readonly currentUser: User | null;
  readonly operators: HashSet<string>;
  readonly currentOperator: string | null;
  readonly setOperator: DispatchAction<operators.ActionType.SetOperator>;
  readonly removeOperator: DispatchAction<operators.ActionType.RemoveOperator>;
}

export class OperatorSelect extends React.PureComponent<OperatorSelectProps> {
  render() {
    if (this.props.currentUser) {
      return <div>{this.props.currentUser.profile.name || this.props.currentUser.profile.sub}</div>;
    }
    const opers = this.props.operators.toArray({ sortOn: x => x });
    return (
      <Downshift
        selectedItem={this.props.currentOperator}
        onChange={o => {
          this.props.setOperator({ operator: o });
        }}
      >
        {ds => (
          <div style={{ position: "relative" }}>
            <TextField
              InputProps={{
                // tslint:disable-next-line:no-any
                ...(ds.getInputProps({ placeholder: "Operator" }) as any),
                onKeyUp: k => {
                  if (k.keyCode === 13 && ds.inputValue && ds.inputValue.length > 0) {
                    this.props.setOperator({ operator: ds.inputValue });
                    ds.closeMenu();
                  }
                },
                endAdornment: <ArrowDropDownIcon onClick={() => ds.openMenu()} />
              }}
            />
            {ds.isOpen ? (
              <Paper
                style={{
                  position: "absolute",
                  zIndex: 1,
                  left: 0,
                  right: 0
                }}
              >
                {ds.inputValue && ds.inputValue.length > 0 && !this.props.operators.contains(ds.inputValue) ? (
                  <Typography variant="caption" align="center">
                    Press enter to add new
                  </Typography>
                ) : (
                  undefined
                )}
                <List>
                  {opers.map((o, idx) => (
                    <ListItem key={idx} button {...ds.getItemProps({ item: o })}>
                      <ListItemText primary={o} />
                      <ListItemSecondaryAction>
                        <IconButton onClick={() => this.props.removeOperator({ operator: o })}>
                          <DeleteIcon />
                        </IconButton>
                      </ListItemSecondaryAction>
                    </ListItem>
                  ))}
                </List>
              </Paper>
            ) : (
              undefined
            )}
          </div>
        )}
      </Downshift>
    );
  }
}

export default connect(
  st => ({
    operators: st.Operators.operators,
    currentOperator: st.Operators.current || null,
    currentUser: st.ServerSettings.user || null
  }),
  {
    setOperator: mkAC(operators.ActionType.SetOperator),
    removeOperator: mkAC(operators.ActionType.RemoveOperator)
  }
)(OperatorSelect);
