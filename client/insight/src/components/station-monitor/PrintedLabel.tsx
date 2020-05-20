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
import { LazySeq } from "../../data/lazyseq";
import { format } from "date-fns";

interface BarcodeProps {
  readonly text: string;
}

function Barcode(props: BarcodeProps) {
  const ref = React.useRef(null);
  const setRef = React.useCallback(
    (node) => {
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
}

export function PrintedLabel(props: PrintedLabelProps) {
  const assignments = React.useMemo(
    () =>
      LazySeq.ofIterable(props.material || [])
        .filter((m) => m.jobUnique !== null && m.jobUnique !== undefined && m.jobUnique !== "")
        .groupBy((m) => m.jobUnique)
        .mapValues((mats) => ({ length: mats.length(), part: mats.head().getOrThrow().partName }))
        .toVector()
        .sortOn(([_, p]) => p.part)
        .toArray(),
    [props.material]
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
        <p style={{ fontSize: "x-large" }}>{format(new Date(), "eeee MMMM d, yyyy HH:mm:ss")}</p>
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
      <div style={{ marginTop: "2em", marginLeft: "4em", marginRight: "4em" }}>
        {assignments.length === 0 ? (
          <p style={{ fontSize: "x-large" }}>Not currently assigned to any jobs</p>
        ) : props.materialName ? (
          <>
            <p style={{ fontSize: "x-large" }}>Assigned To</p>
            {assignments.map(([uniq, part], idx) => (
              <div key={idx} style={{ marginTop: "1em", marginLeft: "2em", display: "flex", alignItems: "center" }}>
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
      <div style={{ marginTop: "4em", display: "flex", justifyContent: "center" }}>
        {props.operator ? <p style={{ fontSize: "x-large" }}>{props.operator}</p> : undefined}
      </div>
    </div>
  );
}
