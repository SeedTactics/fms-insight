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
import Button from "@material-ui/core/Button";
import Stepper from "@material-ui/core/Stepper";
import Step from "@material-ui/core/Step";
import StepLabel from "@material-ui/core/StepLabel";
import StepContent from "@material-ui/core/StepContent";

import * as matDetails from "../../data/material-details";
import { MaterialDetailTitle, MaterialDetailContent } from "../station-monitor/Material";
import { buildPathString, extractPath } from "../../data/results.inspection";
import { startOfToday, addDays, startOfDay, endOfDay } from "date-fns";
import { LogType } from "../../data/api";
import { InspectionSankey } from "../analysis/InspectionSankey";
import { DataTableActionZoomType } from "../analysis/DataTable";
import { useIsDemo } from "../IsDemo";
import { useRecoilState, useRecoilValue, useSetRecoilState } from "recoil";
import { extendRange, inspectionLogEntries, pathLookupRange } from "../../data/path-lookup";
import { DisplayLoadingAndErrorCard } from "../ErrorsAndLoading";

interface SerialLookupProps {
  readonly onSelect: (s: string) => void;
}

function SerialLookup(props: SerialLookupProps) {
  const demo = useIsDemo();
  const [serial, setSerial] = React.useState(demo ? "00000000i9" : "");
  return (
    <div style={{ textAlign: "center" }}>
      <div style={{ marginBottom: "2em" }}>
        <TextField
          label={serial === "" ? "Serial" : "Serial (press enter)"}
          value={serial}
          onChange={(e) => setSerial(e.target.value)}
          onKeyPress={(e) => {
            if (e.key === "Enter" && serial !== "") {
              e.preventDefault();
              props.onSelect(serial);
              setSerial("");
            }
          }}
        />
      </div>
      <Button variant="contained" color="secondary" disabled={serial === ""} onClick={() => props.onSelect(serial)}>
        Lookup
      </Button>
    </div>
  );
}

function lastMachineTime(mat: matDetails.MaterialDetail) {
  const first = mat.events.get(0);
  if (first.isNone()) {
    return startOfToday();
  }
  let lastEnd = first.get().endUTC;
  for (const e of mat.events) {
    if (e.type === LogType.MachineCycle) {
      lastEnd = e.endUTC;
    }
  }
  return lastEnd;
}

interface PathLookupProps {
  readonly mat: matDetails.MaterialDetail;
}

function PathLookup(props: PathLookupProps) {
  const [searchRange, setSearchRange] = useRecoilState(pathLookupRange);
  const logs = useRecoilValue(inspectionLogEntries);

  function extendDateRange(numDays: number) {
    setSearchRange(extendRange(numDays));
  }
  return (
    <InspectionSankey
      inspectionlogs={logs}
      restrictToPart={props.mat.partName}
      subtitle={"Paths for " + props.mat.partName + " around " + lastMachineTime(props.mat).toLocaleString()}
      default_date_range={searchRange ? [searchRange.curStart, searchRange.curEnd] : []}
      zoomType={searchRange ? DataTableActionZoomType.ExtendDays : undefined}
      extendDateRange={extendDateRange}
      defaultToTable
      hideOpenDetailColumn
    />
  );
}

function PartLookupStepper() {
  const [origStep, setStep] = React.useState(0);
  const mat = useRecoilValue(matDetails.materialDetail);
  const setMatToShow = useSetRecoilState(matDetails.materialToShowInDialog);

  const setSearchRange = useSetRecoilState(pathLookupRange);

  let step = origStep;
  if (step === 0 && mat) {
    step = 1;
  }
  return (
    <Stepper activeStep={step} orientation="vertical">
      <Step>
        <StepLabel>Enter or scan a serial</StepLabel>
        <StepContent>
          <SerialLookup
            onSelect={(s) => {
              setMatToShow({ type: "Serial", serial: s });
              setStep(1);
            }}
          />
        </StepContent>
      </Step>
      <Step>
        <StepLabel>
          {mat ? (
            <MaterialDetailTitle
              partName={mat.partName}
              serial={mat.serial}
              subtitle={"Path " + buildPathString(extractPath(mat)) + " at " + lastMachineTime(mat).toLocaleString()}
            />
          ) : (
            "Serial Details"
          )}
        </StepLabel>
        <StepContent>
          {mat ? (
            <div>
              <MaterialDetailContent mat={mat} />
              <div style={{ marginTop: "2em" }}>
                <Button
                  variant="contained"
                  color="secondary"
                  onClick={() => {
                    const d = lastMachineTime(mat);
                    setSearchRange({
                      part: mat.partName,
                      curStart: startOfDay(addDays(d, -5)),
                      curEnd: endOfDay(addDays(d, 5)),
                    });
                    setStep(2);
                  }}
                >
                  Lookup Similar Paths
                </Button>
                <Button
                  variant="contained"
                  style={{ marginLeft: "2em" }}
                  onClick={() => {
                    setMatToShow(null);
                    setSearchRange(null);
                    setStep(0);
                  }}
                >
                  Reset
                </Button>
              </div>
            </div>
          ) : undefined}
        </StepContent>
      </Step>
      <Step>
        <StepLabel>Lookup similar paths</StepLabel>
        <StepContent>
          {mat ? (
            <DisplayLoadingAndErrorCard>
              <PathLookup mat={mat} />
            </DisplayLoadingAndErrorCard>
          ) : undefined}
          <Button
            variant="contained"
            style={{ marginTop: "2em" }}
            onClick={() => {
              setMatToShow(null);
              setSearchRange(null);
              setStep(0);
            }}
          >
            Reset
          </Button>
        </StepContent>
      </Step>
    </Stepper>
  );
}

export function FailedPartLookup() {
  React.useEffect(() => {
    document.title = "Failed Part Lookup - FMS Insight";
  }, []);
  return (
    <main style={{ padding: "24px" }}>
      <div data-testid="failed-parts">
        <PartLookupStepper />
      </div>
    </main>
  );
}
