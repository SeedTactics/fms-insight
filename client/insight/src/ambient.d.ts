/* eslint-disable */

declare module "*.svg" {
  const empty = "";
  export default empty;
}

declare module "react-qr-reader" {
  import * as React from "react";

  export default class QrReader extends React.Component<any> {}
}

declare module "react-timeago" {
  import * as React from "react";

  export interface TimeAgoProps {
    readonly date: Date | string;
  }

  export default class TimeAgo extends React.Component<TimeAgoProps> {}
}

declare module "react-vis" {
  import * as React from "react";

  export class XYPlot extends React.Component<any> {}
  export class FlexibleWidthXYPlot extends React.Component<any> {}
  export class FlexibleXYPlot extends React.Component<any> {}

  export class AreaSeries extends React.Component<any> {}
  export class HorizontalBarSeries extends React.Component<any> {}
  export class VerticalBarSeries extends React.Component<any> {}
  export class HorizontalRectSeries extends React.Component<any> {}
  export class MarkSeries extends React.Component<any> {}
  export class MarkSeriesCanvas extends React.Component<any> {}
  export class HeatmapSeries extends React.Component<any> {}
  export class CustomSVGSeries extends React.Component<any> {}
  export class LabelSeries extends React.Component<any> {}

  export class VerticalGridLines extends React.Component<any> {}
  export class HorizontalGridLines extends React.Component<any> {}

  export class XAxis extends React.Component<any> {}
  export class YAxis extends React.Component<any> {}
  export class DiscreteColorLegend extends React.Component<any> {}
  export class Hint extends React.Component<any> {}
  export class Highlight extends React.Component<any> {}

  export class Sankey extends React.Component<any> {}
}
