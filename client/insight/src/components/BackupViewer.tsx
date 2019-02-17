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
import Button from "@material-ui/core/Button";

import { connect } from "../store/store";
import Efficiency from "./efficiency/Efficiency";

interface BackupViewerProps {
  file_opened: boolean;
  loading_err: Error | undefined;
  onRequestOpenFile: () => void;
}

const InitialPage = React.memo(function(props: { onRequestOpenFile: () => void; loading_error: Error | undefined }) {
  return (
    <div>
      {props.loading_error ? <p>{props.loading_error.message || props.loading_error}</p> : undefined}
      <Button onClick={props.onRequestOpenFile}>Open File</Button>
    </div>
  );
});

const BackupViewer = React.memo(function BackupViewerF(props: BackupViewerProps) {
  if (props.file_opened && props.loading_err === undefined) {
    return <Efficiency />;
  } else {
    return <InitialPage onRequestOpenFile={props.onRequestOpenFile} loading_error={props.loading_err} />;
  }
});

export default connect(s => ({
  file_opened: s.Gui.backup_file_opened,
  loading_err: s.Events.loading_error
}))(BackupViewer);
