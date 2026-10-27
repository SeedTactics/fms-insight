import type { ReactElement, ReactNode } from "react";
import { Tooltip } from "@mui/material";

export function SystemAlarmTooltip({
  suspended,
  title = "",
  children,
}: {
  readonly suspended: boolean;
  readonly title?: ReactNode;
  readonly children: ReactElement;
}) {
  return (
    <Tooltip title={suspended ? "System alarm" : title} describeChild>
      <span style={{ display: "inline-flex" }} tabIndex={suspended ? 0 : undefined}>
        {children}
      </span>
    </Tooltip>
  );
}
