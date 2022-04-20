/* eslint-disable */

declare module "*.svg" {
  const empty = "";
  export default empty;
}

declare module "react-timeago" {
  import * as React from "react";

  export interface TimeAgoProps {
    readonly date: Date | string;
  }

  export default class TimeAgo extends React.Component<TimeAgoProps> {}
}
