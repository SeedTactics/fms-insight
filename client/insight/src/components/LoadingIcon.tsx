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
import CircularProgress from "@material-ui/core/CircularProgress";

import { connect, Store } from "../store/store";
import Tooltip from "@material-ui/core/Tooltip";

function LoadingIcon({ loading }: { loading: boolean }) {
  if (loading) {
    return (
      <Tooltip title="Loading">
        <CircularProgress data-testid="loading-icon" color="secondary" />
      </Tooltip>
    );
  } else {
    return null;
  }
}

export default connect((st: Store) => ({
  loading:
    st.Events.loading_log_entries ||
    st.Events.loading_job_history ||
    st.Current.loading ||
    st.Events.loading_analysis_month_log ||
    st.Events.loading_analysis_month_jobs ||
    st.Websocket.websocket_reconnecting ||
    st.MaterialDetails.add_mat_in_progress ||
    (st.MaterialDetails.material
      ? st.MaterialDetails.material.loading_events ||
        st.MaterialDetails.material.loading_workorders ||
        st.MaterialDetails.material.updating_material
      : false),
}))(LoadingIcon);
