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

import {
  RefObject,
  ReactNode,
  memo,
  useState,
  useEffect,
  createContext,
  useRef,
  useMemo,
  useContext,
} from "react";
import {
  MoveMaterialArrow,
  computeArrows,
  MoveMaterialIdentifier,
  MoveMaterialNodeKind,
  MoveMaterialElemRect,
  AllMoveMaterialNodes,
  uniqueIdForNodeKind,
  memoPropsForNodeKind,
} from "../../data/move-arrows.js";
import { HashMap } from "@seedtactics/immutable-collections";

function elementToRect(e: Element): MoveMaterialElemRect {
  const r = e.getBoundingClientRect();
  return {
    left: r.left + window.scrollX,
    top: r.top + window.scrollY,
    width: r.width,
    height: r.height,
    bottom: r.bottom + window.scrollY,
    right: r.right + window.scrollX,
  };
}

function arrowToPath(arr: MoveMaterialArrow): string {
  // mid point of line
  const mpx = (arr.fromX + arr.toX) / 2;
  const mpy = (arr.fromY + arr.toY) / 2;

  // angle of perpendicular to line
  const theta = Math.atan2(arr.toY - arr.fromY, arr.toX - arr.fromX) + (Math.PI * arr.curveDirection) / 2;

  // control points
  const cx = mpx + 50 * Math.cos(theta);
  const cy = mpy + 50 * Math.sin(theta);

  return `M${arr.fromX},${arr.fromY} Q ${cx} ${cy} ${arr.toX} ${arr.toY}`;
}

const MoveMaterialArrows = memo(function MoveMaterialArrows({
  container,
  arrowsWithRefs,
}: {
  container: RefObject<HTMLElement | null>;
  arrowsWithRefs: AllMoveMaterialNodes<RefObject<HTMLDivElement | null>>;
}) {
  const [, setDims] = useState({
    height: window.innerHeight,
    width: window.innerWidth,
  });
  useEffect(() => {
    function handleResize() {
      setDims({ height: window.innerHeight, width: window.innerWidth });
    }
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
  }, [setDims]);

  const arrows = computeArrows(
    container.current ? elementToRect(container.current) : null,
    arrowsWithRefs.collectValues((r) =>
      r.elem.current ? { ...r, elem: elementToRect(r.elem.current) } : null,
    ),
  );

  return (
    <g>
      {arrows.map((arr, idx) => (
        <path
          key={idx}
          style={{ fill: "none", stroke: "rgba(0,0,0,0.15)", strokeWidth: 2 }}
          d={arrowToPath(arr)}
          markerEnd={`url(#arrow)`}
        />
      ))}
    </g>
  );
});

interface MoveMaterialArrowContext {
  readonly registerNode: (
    id: MoveMaterialIdentifier,
    kind: MoveMaterialNodeKind | null,
    ref: RefObject<HTMLDivElement | null> | null,
  ) => void;
}
const MoveMaterialArrowCtx = createContext<MoveMaterialArrowContext | undefined>(undefined);

export const MoveMaterialArrowContainer = memo(function MoveMaterialArrowContainer({
  children,
  hideArrows,
  whiteBackground,
}: {
  children?: ReactNode;
  hideArrows?: boolean;
  whiteBackground?: boolean;
}) {
  const container = useRef<HTMLDivElement | null>(null);
  const [nodes, setNodes] = useState<AllMoveMaterialNodes<React.RefObject<HTMLDivElement | null>>>(
    HashMap.empty(),
  );

  const ctx: MoveMaterialArrowContext = useMemo(() => {
    return {
      registerNode(
        id: MoveMaterialIdentifier,
        kind: MoveMaterialNodeKind | null,
        ref: RefObject<HTMLDivElement | null> | null,
      ) {
        if (kind && ref) {
          setNodes((ns) => ns.set(id, { ...kind, elem: ref }));
        } else {
          setNodes((nodes) => nodes.delete(id));
        }
      },
    };
  }, [setNodes]);

  return (
    <div style={{ position: "relative" }}>
      <svg
        style={{
          position: "absolute",
          pointerEvents: "none",
          width: "100%",
          height: "100%",
          top: 0,
          right: 0,
          zIndex: -1,
          backgroundColor: whiteBackground ? "white" : "#F8F8F8",
        }}
      >
        <defs>
          <marker
            id="arrow"
            markerWidth={6}
            markerHeight={10}
            refX="0"
            refY="3"
            orient="auto"
            markerUnits="strokeWidth"
          >
            <path d="M0,0 L0,6 L5,3 z" fill="rgba(0,0,0,0.15)" />
          </marker>
        </defs>
        {!hideArrows ? <MoveMaterialArrows container={container} arrowsWithRefs={nodes} /> : undefined}
      </svg>
      <div ref={container}>
        <MoveMaterialArrowCtx.Provider value={ctx}>{children}</MoveMaterialArrowCtx.Provider>
      </div>
    </div>
  );
});

export function useMoveMaterialArrowRef(kind: MoveMaterialNodeKind): RefObject<HTMLDivElement | null> {
  const ctx = useContext(MoveMaterialArrowCtx);
  if (!ctx) {
    throw new Error("useMoveMaterialArrowRef must be used within a MoveMaterialArrowContainer");
  }
  const ref = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const id = uniqueIdForNodeKind(kind);
    ctx.registerNode(id, kind, ref);
    return () => {
      ctx.registerNode(id, null, null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ctx, ...memoPropsForNodeKind(kind)]);
  return ref;
}

export function MoveMaterialArrowNode({
  kind,
  children,
}: {
  kind: MoveMaterialNodeKind;
  children?: React.ReactNode;
}) {
  const ref = useMoveMaterialArrowRef(kind);
  return <div ref={ref}>{children}</div>;
}
