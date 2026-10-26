import { expect, test } from "vitest";
import {
  AlarmActivity,
  AlarmImpact,
  CurrentStatus,
  type ICurrentStatusAlarm,
} from "../network/api.js";
import { alarmsBlockingChanges } from "./alarms.js";

const blocked: Readonly<ICurrentStatusAlarm> = {
  code: "observation-unavailable",
  message: "The last observation could not be updated.",
  impact: AlarmImpact.SuspendChanges,
};
const station = { activity: AlarmActivity.BasketLoadStation, basketId: 7 };

test("only explicit blocking impact suspends changes", () => {
  const status = CurrentStatus.fromJS({
    Alarms: ["Error: status is old. Stop work."],
    AlarmDetails: [{ Code: "diagnostic", Message: "Diagnostic", Impact: "Informational" }],
  });
  expect(alarmsBlockingChanges({}, station)).toEqual([]);
  expect(alarmsBlockingChanges(status, station)).toEqual([]);
  expect(alarmsBlockingChanges({ alarmDetails: [blocked] }, station)).toEqual([blocked]);
});

test("scope filters intersect and leave unrelated work available", () => {
  const allStations = { ...blocked, activity: AlarmActivity.BasketLoadStation };
  const basket = { ...blocked, basketId: 7 };
  const basketStation = { ...allStations, basketId: 7 };
  const status = { alarmDetails: [blocked, allStations, basket, basketStation] };

  expect(alarmsBlockingChanges(status, station)).toEqual(status.alarmDetails);
  expect(alarmsBlockingChanges(status, { ...station, basketId: 8 })).toEqual([
    blocked,
    allStations,
  ]);
  expect(
    alarmsBlockingChanges(status, { ...station, activity: AlarmActivity.BasketMovement }),
  ).toEqual([blocked, basket]);
  expect(alarmsBlockingChanges(status, {})).toEqual([blocked]);
});

test("wire null scope means the whole status and a later snapshot removes the block", () => {
  const parsed = CurrentStatus.fromJS({
    AlarmDetails: [
      {
        Code: "status-refresh-failed",
        Message: blocked.message,
        Impact: "SuspendChanges",
        Activity: null,
        BasketId: null,
      },
    ],
  });
  expect(alarmsBlockingChanges(parsed, station)).toHaveLength(1);
  expect(alarmsBlockingChanges(CurrentStatus.fromJS({ AlarmDetails: [] }), station)).toEqual([]);
});
