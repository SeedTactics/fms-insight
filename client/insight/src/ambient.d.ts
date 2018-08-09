declare module "*.svg" {
  const empty = "";
  export default empty;
}

declare module 'react-qr-reader' {
  import * as React from 'react';

  // tslint:disable-next-line:no-any
  export default class QrReader extends React.Component<any> {}

}

declare module 'react-timeago' {
  import * as React from 'react';

  export interface TimeAgoProps {
    readonly date: Date | string;
  }

  export default class TimeAgo extends React.Component<TimeAgoProps> {}
}

declare module 'react-vis' {

  import * as React from 'react';

  export class XYPlot extends React.Component<any> {}
  export class FlexibleWidthXYPlot extends React.Component<any> {}
  export class FlexibleXYPlot extends React.Component<any> {}

  export class HorizontalBarSeries extends React.Component<any> {}
  export class VerticalBarSeries extends React.Component<any> {}
  export class HorizontalRectSeries extends React.Component<any> {}
  export class MarkSeries extends React.Component<any> {}
  export class MarkSeriesCanvas extends React.Component<any> {}
  export class HeatmapSeries extends React.Component<any> {}
  export class CustomSVGSeries extends React.Component<any> {}

  export class VerticalGridLines extends React.Component<any> {}
  export class HorizontalGridLines extends React.Component<any> {}

  export class XAxis extends React.Component<any> {}
  export class YAxis extends React.Component<any> {}
  export class DiscreteColorLegend extends React.Component<any> {}
  export class Hint extends React.Component<any> {}

  export class Sankey extends React.Component<any> {}

}

// Type definitions for reconnectingwebsocket 1.0
// Project: https://github.com/joewalnes/reconnecting-websocket
// Definitions by: Nicholas Guarracino <https://github.com/nguarracino>
// Definitions: https://github.com/DefinitelyTyped/DefinitelyTyped

declare module 'reconnecting-websocket' {

export interface Options {
    automaticOpen?: boolean;
    binaryType?: 'blob' | 'arraybuffer';
    debug?: boolean;
    maxReconnectAttempts?: number | null;
    maxReconnectInterval?: number;
    reconnectDecay?: number;
    reconnectInterval?: number;
    timeoutInterval?: number;
}

export default class ReconnectingWebSocket {
    constructor(url: string, protocols?: string[], options?: Options);

    static debugAll: boolean;

    static CONNECTING: number;
    static OPEN: number;
    static CLOSING: number;
    static CLOSED: number;

    onclose: (event: any) => void;
    onconnecting: (event: any) => void;
    onerror: (event: any) => void;
    onmessage: (event: any) => void;
    onopen: (event: any) => void;

    close(code?: number, reason?: string): void;
    open(reconnectAttempt?: boolean): void;
    refresh(): void;
    send(data: any): void;

    maxReconnectAttempts: number;
    protocol: string | null;
    readyState: number;
    reconnectAttempts: number;
    url: string;
}

}