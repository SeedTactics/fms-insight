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
import { Card, CardContent, CardHeader, Select, MenuItem, Tooltip, IconButton, Box } from "@mui/material";
import SearchIcon from "@mui/icons-material/Search";
import ImportExport from "@mui/icons-material/ImportExport";
import { sankey, sankeyJustify, sankeyLinkHorizontal, SankeyNode as D3SankeyNode } from "d3-sankey";

import { PartIdenticon } from "../station-monitor/Material";
import { SankeyNode, inspectionDataToSankey, SankeyLink } from "../../data/inspection-sankey";

import { PartAndInspType, InspectionLogEntry, InspectionsByPartAndType } from "../../cell-status/inspections";
import InspectionDataTable from "./InspectionDataTable";
import { copyInspectionEntriesToClipboard } from "../../data/results.inspection";
import { DataTableActionZoomType } from "./DataTable";
import { useIsDemo } from "../routes";
import { Group } from "@visx/group";
import { green, grey } from "@mui/material/colors";
import { useTooltip, Tooltip as VisxTooltip, defaultStyles as defaultTooltipStyles } from "@visx/tooltip";
import { localPoint } from "@visx/event";
import { ParentSize } from "@visx/responsive";

type NodeWithData = D3SankeyNode<SankeyNode, { readonly value: number }>;
type LinkWithData = {
  readonly source: NodeWithData;
  readonly target: NodeWithData;
  readonly value: number;
  readonly width: number;
};

const marginLeft = 20;
const marginTop = 20;
const marginRight = 120;
const marginBottom = 20;

type ShowTooltipFunc = (a: {
  readonly tooltipLeft?: number;
  readonly tooltipTop?: number;
  readonly tooltipData?: LinkWithData;
}) => void;

function LinkDisplay({
  link,
  path,
  strokeWidth,
  showTooltip,
  hideTooltip,
}: {
  readonly link: LinkWithData;
  readonly path: string | null;
  readonly strokeWidth: number;
  readonly showTooltip: ShowTooltipFunc;
  readonly hideTooltip: () => void;
}) {
  const [over, setOver] = React.useState(false);
  if (path === null) return null;
  function pointerOver(e: React.PointerEvent) {
    const pt = localPoint(e);
    if (pt === null) return;
    showTooltip({ tooltipLeft: pt.x, tooltipTop: pt.y, tooltipData: link });
    setOver(true);
  }
  function pointerOut() {
    setOver(false);
    hideTooltip();
  }
  return (
    <path
      d={path}
      stroke={over ? green[600] : green[300]}
      strokeWidth={strokeWidth}
      opacity={0.2}
      fill="none"
      onPointerOver={pointerOver}
      onPointerOut={pointerOut}
    />
  );
}

function NodeDisplay({ node }: { readonly node: NodeWithData }) {
  if (node.x1 === undefined || node.x0 === undefined || node.y1 === undefined || node.y0 === undefined) return null;
  return (
    <Group top={node.y0} left={node.x0}>
      <rect width={node.x1 - node.x0} height={node.y1 - node.y0} fill={green[800]} opacity={0.8} stroke="none" />

      <text x={18} y={(node.y1 - node.y0) / 2}>
        {node.name}
      </text>
    </Group>
  );
}

const SankeyDisplay = React.memo(function InspectionSankeyDiagram({
  data,
  showTooltip,
  hideTooltip,
  parentHeight,
  parentWidth,
}: {
  readonly data: Iterable<InspectionLogEntry>;
  readonly parentHeight: number;
  readonly parentWidth: number;
  readonly showTooltip: ShowTooltipFunc;
  readonly hideTooltip: () => void;
}) {
  const { nodes, links } = React.useMemo(() => {
    const { nodes, links } = inspectionDataToSankey(data);
    const generator = sankey<SankeyNode, SankeyLink>()
      .nodeWidth(10)
      .nodePadding(10)
      .nodeAlign(sankeyJustify)
      .extent([
        [marginLeft, marginTop],
        [parentWidth - marginRight, parentHeight - marginBottom - marginTop],
      ]);
    generator({ nodes, links });
    return { nodes: nodes as NodeWithData[], links: links as unknown as LinkWithData[] };
  }, [data, parentWidth, parentHeight]);

  const path = sankeyLinkHorizontal();

  return (
    <svg width={parentWidth} height={parentHeight}>
      <g>
        {nodes.map((node, i) => (
          <NodeDisplay key={i} node={node} />
        ))}
      </g>
      <g>
        {links.map((link, i) => (
          <LinkDisplay
            key={i}
            link={link}
            path={path(link)}
            strokeWidth={Math.max(link.width ?? 1, 1)}
            showTooltip={showTooltip}
            hideTooltip={hideTooltip}
          />
        ))}
      </g>
    </svg>
  );
});

const LinkTooltip = React.memo(function LinkTooltip({
  tooltipData,
  tooltipTop,
  tooltipLeft,
}: {
  readonly tooltipData: LinkWithData;
  readonly tooltipTop: number | undefined;
  readonly tooltipLeft: number | undefined;
}) {
  return (
    <VisxTooltip
      left={tooltipLeft}
      top={tooltipTop}
      style={{ ...defaultTooltipStyles, backgroundColor: grey[800], color: "white" }}
    >
      {tooltipData.source.name} ➞ {tooltipData.target.name}: {tooltipData.value} parts
    </VisxTooltip>
  );
});

const InspectionDiagram = React.memo(function InspectionDiagram({
  data,
}: {
  readonly data: Iterable<InspectionLogEntry>;
}) {
  const { showTooltip, hideTooltip, tooltipData, tooltipLeft, tooltipTop } = useTooltip<LinkWithData>();
  return (
    <div style={{ position: "relative" }}>
      <Box sx={{ height: "calc(100vh - 100px)", width: "100%" }}>
        <ParentSize>
          {(parent) => (
            <SankeyDisplay
              data={data}
              showTooltip={showTooltip}
              hideTooltip={hideTooltip}
              parentHeight={parent.height}
              parentWidth={parent.width}
            />
          )}
        </ParentSize>
      </Box>
      {tooltipData ? (
        <LinkTooltip tooltipData={tooltipData} tooltipLeft={tooltipLeft} tooltipTop={tooltipTop} />
      ) : undefined}
    </div>
  );
});

export interface InspectionSankeyProps {
  readonly inspectionlogs: InspectionsByPartAndType;
  readonly default_date_range: Date[];
  readonly zoomType?: DataTableActionZoomType;
  readonly subtitle?: string;
  readonly restrictToPart?: string;
  readonly defaultToTable: boolean;
  readonly extendDateRange?: (numDays: number) => void;
  readonly hideOpenDetailColumn?: boolean;
}

export function InspectionSankey(props: InspectionSankeyProps) {
  const demo = useIsDemo();
  const [curPart, setSelectedPart] = React.useState<string | undefined>(demo ? "aaa" : undefined);
  const [selectedInspectType, setSelectedInspectType] = React.useState<string | undefined>(demo ? "CMM" : undefined);
  const [showTable, setShowTable] = React.useState<boolean>(props.defaultToTable);

  let curData: Iterable<InspectionLogEntry> | undefined;
  const selectedPart = props.restrictToPart || curPart;
  if (selectedPart && selectedInspectType) {
    curData = props.inspectionlogs.get(new PartAndInspType(selectedPart, selectedInspectType))?.valuesToLazySeq() ?? [];
  }
  const parts = props.inspectionlogs
    .keysToLazySeq()
    .map((x) => x.part)
    .distinct()
    .toSortedArray((x) => x);
  const inspTypes = props.inspectionlogs
    .keysToLazySeq()
    .map((e) => e.inspType)
    .distinct()
    .toSortedArray((x) => x);
  return (
    <Card raised>
      <CardHeader
        title={
          <div
            style={{
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
            }}
          >
            <SearchIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Inspections</div>
            <div style={{ flexGrow: 1 }} />
            {curData ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  onClick={() =>
                    curData
                      ? copyInspectionEntriesToClipboard(selectedPart || "", selectedInspectType || "", curData)
                      : undefined
                  }
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                  size="large"
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : undefined}
            <Select
              autoWidth
              value={showTable ? "table" : "sankey"}
              onChange={(e) => setShowTable(e.target.value === "table")}
            >
              <MenuItem key="sankey" value="sankey">
                Sankey
              </MenuItem>
              <MenuItem key="table" value="table">
                Table
              </MenuItem>
            </Select>
            <Select
              name="inspection-sankey-select-type"
              autoWidth
              displayEmpty
              style={{ marginRight: "1em", marginLeft: "1em" }}
              value={selectedInspectType || ""}
              onChange={(e) => setSelectedInspectType(e.target.value)}
            >
              {selectedInspectType ? undefined : (
                <MenuItem key={0} value="">
                  <em>Select Inspection Type</em>
                </MenuItem>
              )}
              {inspTypes.map((n) => (
                <MenuItem key={n} value={n}>
                  {n}
                </MenuItem>
              ))}
            </Select>
            {props.restrictToPart === undefined ? (
              <Select
                name="inspection-sankey-select-part"
                autoWidth
                displayEmpty
                value={selectedPart || ""}
                onChange={(e) => setSelectedPart(e.target.value)}
              >
                {selectedPart ? undefined : (
                  <MenuItem key={0} value="">
                    <em>Select Part</em>
                  </MenuItem>
                )}
                {parts.map((n) => (
                  <MenuItem key={n} value={n}>
                    <div style={{ display: "flex", alignItems: "center" }}>
                      <PartIdenticon part={n} size={30} />
                      <span style={{ marginRight: "1em" }}>{n}</span>
                    </div>
                  </MenuItem>
                ))}
              </Select>
            ) : undefined}
          </div>
        }
        subheader={props.subtitle}
      />
      <CardContent>
        {curData ? (
          showTable ? (
            <InspectionDataTable
              zoomType={props.zoomType}
              points={curData}
              default_date_range={props.default_date_range}
              extendDateRange={props.extendDateRange}
              hideOpenDetailColumn={props.hideOpenDetailColumn}
            />
          ) : (
            <InspectionDiagram data={curData} />
          )
        ) : undefined}
      </CardContent>
    </Card>
  );
}
