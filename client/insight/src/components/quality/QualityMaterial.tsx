/* Copyright (c) 2022, John Lenz

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
import {
  CircularProgress,
  Stack,
  TextField,
  Button,
  Stepper,
  Step,
  StepLabel,
  Typography,
} from "@mui/material";

import * as matDetails from "../../cell-status/material-details.js";
import { MaterialDetailTitle, MaterialDetailContent, MaterialLoading } from "../station-monitor/Material.js";
import { buildPathString, extractPath } from "../../data/results.inspection.js";
import { startOfToday, addDays, startOfDay, endOfDay } from "date-fns";
import { ApiException, ILogEntry, LogType } from "../../network/api.js";
import { InspectionSankey } from "../analysis/InspectionSankey.js";
import { DataTableActionZoomType } from "../analysis/DataTable.js";
import { useIsDemo, useSetTitle } from "../routes.js";
import { extendRange, inspectionLogEntries, pathLookupRange } from "../../data/path-lookup.js";
import { DisplayLoadingAndError } from "../ErrorsAndLoading.js";
import { LogBackend } from "../../network/backend.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { RecentFailedInspectionsTable } from "./RecentFailedInspections.js";
import { useAtom, useAtomValue, useSetAtom } from "jotai";
import { loadable } from "jotai/utils";

function SerialLookup() {
  const demo = useIsDemo();
  const setMatToShow = useSetAtom(matDetails.materialDialogOpen);
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
          } else {
            setError("No material found with serial " + serial);
          }
        })
        .catch((e: Error | string) => {
          if (ApiException.isApiException(e)) {
            setError(e.response);
          } else if (e instanceof Error) {
            setError(e.message);
          } else {
            setError(e.toString());
          }
        })
        .finally(() => setLoading(false));
    }
  }

  return (
    <>
      <Stack direction="row" spacing={2} alignItems="center" marginLeft="auto" marginRight="auto">
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
        <div>
          <Button variant="contained" color="secondary" disabled={serial === "" || loading} onClick={lookup}>
            {loading ? (
              <Stack direction="row" spacing={2}>
                <CircularProgress /> Searching
              </Stack>
            ) : (
              <>Lookup</>
            )}
          </Button>
        </div>
      </Stack>
      {error && error !== "" ? <div>{error}</div> : undefined}
    </>
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

function DetailsStepTitle() {
  const matL = useAtomValue(loadable(matDetails.materialInDialogInfo));
  const matEvents = useAtomValue(matDetails.materialInDialogEvents);
  const mat = matL.state === "hasData" ? matL.data : null;
  if (mat) {
    return (
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
    );
  } else {
    return <div>Serial Details</div>;
  }
}

function DetailsStepButtons({ setStep }: { setStep: (step: number) => void }) {
  const matL = useAtomValue(loadable(matDetails.materialInDialogInfo));
  const mat = matL.state === "hasData" ? matL.data : null;
  const matEvents = useAtomValue(matDetails.materialInDialogEvents);
  const setSearchRange = useSetAtom(pathLookupRange);
  const setMatToShow = useSetAtom(matDetails.materialDialogOpen);

  return (
    <Stack direction="row" spacing={2} mt="2em">
      {mat ? (
        <Button
          variant="contained"
          color="secondary"
          onClick={() => {
            const d = lastMachineTime(matEvents);
            setSearchRange({
              part: mat?.partName ?? "",
              curStart: startOfDay(addDays(d, -5)),
              curEnd: endOfDay(addDays(d, 5)),
            });
            setStep(2);
          }}
        >
          Lookup Similar Paths
        </Button>
      ) : undefined}
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
    </Stack>
  );
}

interface PathLookupProps {
  readonly setStep: (step: number) => void;
}

function PathLookupStep(props: PathLookupProps) {
  const mat = useAtomValue(matDetails.materialInDialogInfo);
  const matEvents = useAtomValue(matDetails.materialInDialogEvents);
  const [searchRange, setSearchRange] = useAtom(pathLookupRange);
  const logs = useAtomValue(inspectionLogEntries);
  const setMatToShow = useSetAtom(matDetails.materialDialogOpen);

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
        hideOpenDetailColumn
        onlyTable
      />
      <Button
        variant="contained"
        style={{ marginTop: "2em" }}
        onClick={() => {
          setMatToShow(null);
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
  const matToShow = useAtomValue(matDetails.materialDialogOpen);

  let step = origStep;
  if (step === 0 && matToShow) {
    step = 1;
  }
  return (
    <Stack direction="column" spacing={2}>
      <Stepper activeStep={step} orientation="horizontal">
        <Step>
          <StepLabel>Select Material</StepLabel>
        </Step>
        <Step>
          <StepLabel>
            <DetailsStepTitle />
          </StepLabel>
        </Step>
        <Step>
          <StepLabel>Lookup similar paths</StepLabel>
        </Step>
      </Stepper>
      {step === 0 ? (
        <Stack direction="column" spacing={4}>
          <Typography>
            Enter a serial number, scan a barcode, or click on a material in the table below to see details.
          </Typography>
          <SerialLookup />
          <RecentFailedInspectionsTable />
        </Stack>
      ) : step === 1 ? (
        <>
          <DisplayLoadingAndError fallback={<MaterialLoading />}>
            <MaterialDetailContent />
          </DisplayLoadingAndError>
          <DetailsStepButtons setStep={setStep} />
        </>
      ) : step === 2 ? (
        <DisplayLoadingAndError>
          <PathLookupStep setStep={setStep} />
        </DisplayLoadingAndError>
      ) : undefined}
    </Stack>
  );
}

export function QualityMaterialPage() {
  useSetTitle("Quality Material");
  return (
    <main style={{ padding: "24px" }}>
      <PartLookupStepper />
    </main>
  );
}
