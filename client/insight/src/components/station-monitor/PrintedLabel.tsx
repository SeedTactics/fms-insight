/* Copyright (c) 2020, John Lenz

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
import JsBarcode from "jsbarcode";
import { LazySeq } from "@seedtactics/immutable-collections";
import { currentStatus } from "../../cell-status/current-status.js";
import { useAtomValue } from "jotai";

interface BarcodeProps {
  readonly text: string;
}

function Barcode(props: BarcodeProps) {
  const ref = React.useRef<SVGSVGElement | null>(null);
  const setRef = React.useCallback(
    (node: SVGSVGElement) => {
      if (node) {
        JsBarcode(node, props.text, {
          format: "CODE128",
        });
      }
      ref.current = node;
    },
    [props.text]
  );
  React.useEffect(() => {
    if (ref.current) {
      JsBarcode(ref.current, props.text, {
        format: "CODE128",
        fontSize: 30,
      });
    }
  }, [props.text]);

  return <svg ref={setRef} />;
}

export interface PrintMaterial {
  readonly partName: string;
  readonly jobUnique: string;
  readonly serial?: string;
}

export interface PrintedLabelProps {
  readonly material: ReadonlyArray<PrintMaterial> | null;
  readonly materialName?: string | null;
  readonly operator?: string | null;
  readonly oneJobPerPage: boolean;
}

export interface SinglePageProps {
  readonly partName: string;
  readonly materialName: string | null | undefined;
  readonly count: number;
  readonly uniq: string | null;
  readonly note: string | null | undefined;
  readonly operator: string | null | undefined;
  readonly serial1: string | undefined;
  readonly serial2: string | undefined;
}

function SinglePage(props: SinglePageProps) {
  return (
    <div>
      <div style={{ marginTop: "4em", display: "flex", justifyContent: "center" }}>
        <h2>SeedTactic: FMS Insight</h2>
      </div>
      <div style={{ display: "flex", justifyContent: "center" }}>
        <p style={{ fontSize: "x-large" }}>
          {new Date().toLocaleString(undefined, {
            weekday: "long",
            month: "long",
            day: "numeric",
            year: "numeric",
            hour: "numeric",
            minute: "2-digit",
            second: "2-digit",
          })}
        </p>
      </div>
      <div style={{ marginTop: "2em", display: "flex", justifyContent: "space-around" }}>
        <Barcode text={props.materialName ?? props.partName} />
        {props.count >= 2 ? <Barcode text={props.count.toString()} /> : undefined}
      </div>
      {props.serial1 && props.serial1 !== "" ? (
        <div style={{ marginTop: "2em", display: "flex", justifyContent: "space-around" }}>
          <Barcode text={props.serial1} />
          {props.serial2 && props.serial2 !== "" ? <Barcode text={props.serial2 || ""} /> : undefined}
        </div>
      ) : undefined}
      <div style={{ marginTop: "2em", marginLeft: "4em", marginRight: "4em", marginBottom: "9em" }}>
        {props.uniq === null || props.uniq === "" ? (
          <p style={{ fontSize: "x-large" }}>Not currently assigned to any jobs</p>
        ) : (
          <>
            <p style={{ fontSize: "x-large" }}>Assigned To</p>
            <div style={{ marginTop: "1em", marginLeft: "2em", display: "flex", alignItems: "center" }}>
              <Barcode text={props.partName} />
              <h3 style={{ marginLeft: "4em" }}>assigned to {props.uniq}</h3>
            </div>
          </>
        )}
      </div>
      {props.note ? (
        <div style={{ marginBottom: "2em" }}>
          <p style={{ fontSize: "xxx-large", textAlign: "center" }}>{props.note}</p>
        </div>
      ) : undefined}
      <div style={{ display: "flex", justifyContent: "center" }}>
        {props.operator ? <p style={{ fontSize: "x-large" }}>{props.operator}</p> : undefined}
      </div>
    </div>
  );
}

function OneJobPerPage(props: PrintedLabelProps) {
  const allJobs = useAtomValue(currentStatus).jobs;

  const assignments = React.useMemo(
    () =>
      LazySeq.of(props.material || [])
        .filter((m) => m.jobUnique !== null && m.jobUnique !== undefined && m.jobUnique !== "")
        .groupBy((m) => m.jobUnique)
        .map(
          ([uniq, mats]) =>
            [
              uniq,
              {
                length: mats.length,
                part: mats[0]?.partName ?? "",
                comment: allJobs[uniq]?.comment,
                serial1: mats[0]?.serial,
                serial2: mats[mats.length - 1]?.serial,
              },
            ] as const
        )
        .toSortedArray(([_, p]) => p.part),
    [props.material, allJobs]
  );

  if (!props.material || props.material.length === 0) {
    return <div />;
  } else if (assignments.length === 0) {
    return (
      <SinglePage
        partName={props.material[0].partName}
        materialName={props.materialName}
        count={props.material.length}
        uniq={null}
        note={null}
        operator={props.operator}
        serial1={props.material[0].serial}
        serial2={props.material[props.material.length - 1]?.serial}
      />
    );
  } else {
    return (
      <div>
        {assignments.map(([uniq, a], idx) => (
          <React.Fragment key={idx}>
            <SinglePage
              partName={a.part}
              materialName={props.materialName}
              count={a.length}
              uniq={uniq}
              note={a.comment}
              operator={props.operator}
              serial1={a.serial1}
              serial2={a.serial2}
            />
            {idx < assignments.length - 1 ? <div style={{ pageBreakAfter: "always" }} /> : undefined}
          </React.Fragment>
        ))}
      </div>
    );
  }
}

function CombinedToOnePage(props: PrintedLabelProps) {
  const assignments = React.useMemo(
    () =>
      LazySeq.of(props.material || [])
        .filter((m) => m.jobUnique !== null && m.jobUnique !== undefined && m.jobUnique !== "")
        .groupBy((m) => m.jobUnique)
        .map(([k, mats]) => [k, { length: mats.length, part: mats[0]?.partName ?? "" }] as const)
        .toSortedArray(([_, p]) => p.part),
    [props.material]
  );

  const allJobs = useAtomValue(currentStatus).jobs;

  const notes = React.useMemo(
    () =>
      LazySeq.of(props.material || [])
        .collect((m) => (m.jobUnique ? allJobs[m.jobUnique]?.comment : undefined))
        .distinct()
        .toSortedArray((n) => n),
    [props.material, allJobs]
  );

  if (!props.material || props.material.length === 0) {
    return <div />;
  }
  return (
    <div>
      <div style={{ marginTop: "4em", display: "flex", justifyContent: "center" }}>
        <h2>SeedTactic: FMS Insight</h2>
      </div>
      <div style={{ display: "flex", justifyContent: "center" }}>
        <p style={{ fontSize: "x-large" }}>
          {new Date().toLocaleString(undefined, {
            weekday: "long",
            month: "long",
            day: "numeric",
            year: "numeric",
            hour: "numeric",
            minute: "2-digit",
            second: "2-digit",
          })}
        </p>
      </div>
      <div style={{ marginTop: "2em", display: "flex", justifyContent: "space-around" }}>
        <Barcode text={props.materialName ?? props.material[0].partName} />
        {props.material.length >= 2 ? <Barcode text={props.material.length.toString()} /> : undefined}
      </div>
      {props.material[0].serial && props.material[0].serial !== "" ? (
        <div style={{ marginTop: "2em", display: "flex", justifyContent: "space-around" }}>
          <Barcode text={props.material[0].serial} />
          {props.material.length >= 2 &&
          props.material[props.material.length - 1].serial &&
          props.material[props.material.length - 1].serial !== "" ? (
            <Barcode text={props.material[props.material.length - 1].serial || ""} />
          ) : undefined}
        </div>
      ) : undefined}
      <div style={{ marginTop: "2em", marginLeft: "4em", marginRight: "4em", marginBottom: "9em" }}>
        {assignments.length === 0 ? (
          <p style={{ fontSize: "x-large" }}>Not currently assigned to any jobs</p>
        ) : props.materialName ? (
          <>
            <p style={{ fontSize: "x-large" }}>Assigned To</p>
            {assignments.map(([uniq, part], idx) => (
              <div
                key={idx}
                style={{ marginTop: "1em", marginLeft: "2em", display: "flex", alignItems: "center" }}
              >
                <Barcode text={part.part} />
                <h3 style={{ marginLeft: "4em" }}>x{part.length}</h3>
                <h3 style={{ marginLeft: "4em" }}>assigned to {uniq}</h3>
              </div>
            ))}
          </>
        ) : assignments.length === 1 ? (
          <div style={{ marginTop: "1em", display: "flex", justifyContent: "space-around" }}>
            <p style={{ fontSize: "x-large" }}>
              <span>Assigned to {assignments[0][0]}</span>
              {assignments[0][1].length > 1 ? (
                <span style={{ marginLeft: "1em" }}>x{assignments[0][1].length}</span>
              ) : undefined}
            </p>
          </div>
        ) : (
          <>
            <p style={{ fontSize: "x-large" }}>Assigned To</p>
            {assignments.map(([uniq, part], idx) => (
              <h3 key={idx} style={{ marginTop: "1em", marginLeft: "2em" }}>
                <span>{uniq}</span>
                {part.length > 1 ? <span style={{ marginLeft: "1em" }}>x{part.length}</span> : undefined}
              </h3>
            ))}
          </>
        )}
      </div>
      {notes.length > 0 ? (
        <div style={{ marginBottom: "2em" }}>
          {notes.map((n, idx) => (
            <p key={idx} style={{ fontSize: "xxx-large", textAlign: "center" }}>
              {n}
            </p>
          ))}
        </div>
      ) : undefined}
      <div style={{ display: "flex", justifyContent: "center" }}>
        {props.operator ? <p style={{ fontSize: "x-large" }}>{props.operator}</p> : undefined}
      </div>
    </div>
  );
}

export function PrintedLabel(props: PrintedLabelProps) {
  if (props.oneJobPerPage) {
    return <OneJobPerPage {...props} />;
  } else {
    return <CombinedToOnePage {...props} />;
  }
}
