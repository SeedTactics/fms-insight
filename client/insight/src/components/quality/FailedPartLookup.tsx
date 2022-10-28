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
import { CircularProgress, Stack, TextField } from "@mui/material";
import { Button } from "@mui/material";
import { Stepper } from "@mui/material";
import { Step } from "@mui/material";
import { StepLabel } from "@mui/material";
import { StepContent } from "@mui/material";
import { ErrorBoundary } from "react-error-boundary";

import * as matDetails from "../../cell-status/material-details.js";
import { MaterialDetailTitle, MaterialDetailContent } from "../station-monitor/Material.js";
import { buildPathString, extractPath } from "../../data/results.inspection.js";
import { startOfToday, addDays, startOfDay, endOfDay } from "date-fns";
import { ILogEntry, LogType } from "../../network/api.js";
import { InspectionSankey } from "../analysis/InspectionSankey.js";
import { DataTableActionZoomType } from "../analysis/DataTable.js";
import { useIsDemo } from "../routes.js";
import { useRecoilState, useRecoilValue, useRecoilValueLoadable, useSetRecoilState } from "recoil";
import { extendRange, inspectionLogEntries, pathLookupRange } from "../../data/path-lookup.js";
import { DisplayError, DisplayLoadingAndError } from "../ErrorsAndLoading.js";
import { LogBackend } from "../../network/backend.js";
import { LazySeq } from "@seedtactics/immutable-collections";

interface SerialLookupProps {
  readonly onSelect: () => void;
}

function SerialLookup(props: SerialLookupProps) {
  const demo = useIsDemo();
  const setMatToShow = matDetails.useSetMaterialToShowInDialog();
  const [serial, setSerial] = React.useState(demo ? "00000000i9" : "");
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<string | null>(null);

  function lookup() {
    if (serial && serial !== "") {
      setLoading(true);
      setError(null);
      LogBackend.materialForSerial(serial)
        .then((mats) => {
          const mat = LazySeq.of(mats).maxBy((m) => m.materialID);
          if (mat) {
            setMatToShow({ type: "MatDetails", details: mat });
            setSerial("");
            setError(null);
            props.onSelect();
          } else {
            setError("No material found with serial " + serial);
          }
        })
        .catch((e: Error | string) => setError(e instanceof Error ? e.message : e.toString()))
        .finally(() => setLoading(false));
    }
  }

  return (
    <div style={{ textAlign: "center" }}>
      <Stack direction="column" spacing={2}>
        <div>
          <TextField
            label={serial === "" ? "Serial" : "Serial (press enter)"}
            value={serial}
            onChange={(e) => setSerial(e.target.value)}
            onKeyPress={(e) => {
              if (e.key === "Enter" && serial !== "") {
                e.preventDefault();
                lookup();
              }
            }}
          />
        </div>
        {error && error !== "" ? <div>{error}</div> : undefined}
        <Button variant="contained" color="secondary" disabled={serial === "" || loading} onClick={lookup}>
          {loading ? (
            <Stack direction="row" spacing={2}>
              <CircularProgress /> Searching
            </Stack>
          ) : (
            <>Lookup</>
          )}
        </Button>
      </Stack>
    </div>
  );
}

function lastMachineTime(evts: ReadonlyArray<Readonly<ILogEntry>>): Date {
  const first = evts[0];
  if (first === undefined) {
    return startOfToday();
  }
  let lastEnd = first.endUTC;
  for (const e of evts) {
    if (e.type === LogType.MachineCycle) {
      lastEnd = e.endUTC;
    }
  }
  return lastEnd;
}

function LoadingDetailsStep() {
  return (
    <>
      <StepLabel>Serial Details</StepLabel>
      <StepContent>
        <Stack direction="row" spacing={2}>
          <CircularProgress /> <div>Loading...</div>
        </Stack>
      </StepContent>
    </>
  );
}

function DetailsStep({ setStep }: { setStep: (step: number) => void }) {
  const mat = useRecoilValue(matDetails.materialInDialogInfo);
  const matEvents = useRecoilValue(matDetails.materialInDialogEvents);
  const setSearchRange = useSetRecoilState(pathLookupRange);
  const closeMatDialog = matDetails.useCloseMaterialDialog();

  return (
    <>
      <StepLabel>
        {mat ? (
          <MaterialDetailTitle
            partName={mat.partName}
            serial={mat.serial}
            subtitle={
              "Path " +
              buildPathString(extractPath(matEvents)) +
              " at " +
              lastMachineTime(matEvents).toLocaleString()
            }
          />
        ) : (
          "Serial Details"
        )}
      </StepLabel>
      <StepContent>
        {mat ? (
          <div>
            <MaterialDetailContent />
            <div style={{ marginTop: "2em" }}>
              <Button
                variant="contained"
                color="secondary"
                onClick={() => {
                  const d = lastMachineTime(matEvents);
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
                  closeMatDialog();
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
    </>
  );
}

interface PathLookupProps {
  readonly setStep: (step: number) => void;
}

function PathLookupStep(props: PathLookupProps) {
  const mat = useRecoilValue(matDetails.materialInDialogInfo);
  const matEvents = useRecoilValue(matDetails.materialInDialogEvents);
  const [searchRange, setSearchRange] = useRecoilState(pathLookupRange);
  const logs = useRecoilValue(inspectionLogEntries);
  const closeMatDialog = matDetails.useCloseMaterialDialog();

  if (mat === null) return null;

  function extendDateRange(numDays: number) {
    setSearchRange(extendRange(numDays));
  }

  return (
    <div>
      <InspectionSankey
        inspectionlogs={logs}
        restrictToPart={mat.partName}
        subtitle={"Paths for " + mat.partName + " around " + lastMachineTime(matEvents).toLocaleString()}
        default_date_range={searchRange ? [searchRange.curStart, searchRange.curEnd] : []}
        zoomType={searchRange ? DataTableActionZoomType.ExtendDays : undefined}
        extendDateRange={extendDateRange}
        defaultToTable
        hideOpenDetailColumn
      />
      <Button
        variant="contained"
        style={{ marginTop: "2em" }}
        onClick={() => {
          closeMatDialog();
          setSearchRange(null);
          props.setStep(0);
        }}
      >
        Reset
      </Button>
    </div>
  );
}

export function PartLookupStepper() {
  const [origStep, setStep] = React.useState(0);
  const matLoadable = useRecoilValueLoadable(matDetails.materialInDialogInfo);

  let step = origStep;
  if (step === 0 && matLoadable.valueMaybe()) {
    step = 1;
  }
  return (
    <Stepper activeStep={step} orientation="vertical">
      <Step>
        <StepLabel>Enter or scan a serial</StepLabel>
        <StepContent>
          {matLoadable.state === "loading" ? (
            <Stack direction="row" spacing={2}>
              <CircularProgress />
              <div>Loading barcode...</div>
            </Stack>
          ) : (
            <SerialLookup
              onSelect={() => {
                setStep(1);
              }}
            />
          )}
        </StepContent>
      </Step>
      <Step>
        <ErrorBoundary FallbackComponent={DisplayError}>
          <React.Suspense fallback={<LoadingDetailsStep />}>
            <DetailsStep setStep={setStep} />
          </React.Suspense>
        </ErrorBoundary>
      </Step>
      <Step>
        <StepLabel>Lookup similar paths</StepLabel>
        <StepContent>
          <DisplayLoadingAndError>
            <PathLookupStep setStep={setStep} />
          </DisplayLoadingAndError>
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
