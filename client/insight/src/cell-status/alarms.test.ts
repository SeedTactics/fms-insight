import { expect, test } from "vitest";
import {
  AlarmActivity,
  AlarmImpact,
  CurrentStatus,
  InProcessMaterial,
  ActionType,
  type ICurrentStatusAlarm,
} from "../network/api.js";
import { alarmsBlockingChanges, materialActionIsSuspended } from "./alarms.js";

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

test.each([
  { Type: "LoadingToBasket", LoadToBasketId: 7 },
  { Type: "Loading", LoadFromBasketId: 7 },
  { Type: "UnloadToInProcess", UnloadToBasketId: 7 },
  { Type: "UnloadToCompletedMaterial" },
])("transfer cues match their source and destination basket ($Type)", (action) => {
  const mat = InProcessMaterial.fromJS({
    MaterialID: 1,
    JobUnique: "job",
    PartName: "Part",
    Process: 1,
    Path: 1,
    SignaledInspections: [],
    Location:
      action.Type === "UnloadToCompletedMaterial"
        ? { Type: "InBasket", BasketId: 7 }
        : { Type: "Free" },
    Action: action,
  });
  expect(materialActionIsSuspended({ alarmDetails: [{ ...blocked, basketId: 7 }] }, mat)).toBe(
    true,
  );
  expect(materialActionIsSuspended({ alarmDetails: [{ ...blocked, basketId: 8 }] }, mat)).toBe(
    false,
  );
  expect(
    materialActionIsSuspended(
      { alarmDetails: [{ ...blocked, impact: AlarmImpact.Informational }] },
      mat,
    ),
  ).toBe(false);
});

test.each([ActionType.Waiting, ActionType.Machining])(
  "blocking changes preserves read-only %s observations",
  (type) => {
    const mat = InProcessMaterial.fromJS({
      Action: { Type: type },
      Location: { Type: "InBasket", BasketId: 7 },
    });
    expect(materialActionIsSuspended({ alarmDetails: [blocked] }, mat)).toBe(false);
  },
);

test("station alarms suspend legacy basket loads without suspending pallet loads", () => {
  const status = { alarmDetails: [{ ...blocked, ...station }] };
  const legacy = InProcessMaterial.fromJS({
    Action: { Type: "Loading", LoadFromBasketId: 7 },
    Location: { Type: "InQueue", CurrentQueue: "stock" },
  });
  expect(materialActionIsSuspended(status, legacy)).toBe(true);
  const pallet = InProcessMaterial.fromJS({
    Action: { Type: "Loading", LoadFromBasketId: 7, LoadOntoPalletNum: 1, LoadOntoFace: 1 },
    Location: { Type: "InBasket", BasketId: 7, BasketSlot: 1 },
  });
  expect(materialActionIsSuspended(status, pallet)).toBe(false);
});

test.each([
  {
    direction: "basket to pallet",
    location: { Type: "InBasket", BasketId: 7, BasketSlot: 1 },
    action: { Type: "Loading", LoadFromBasketId: 7, LoadOntoPalletNum: 1, LoadOntoFace: 1 },
  },
  {
    direction: "pallet to basket",
    location: { Type: "OnPallet", PalletNum: 1, Face: 1 },
    action: { Type: "UnloadToInProcess", UnloadToBasketId: 7, UnloadToBasketSlot: 1 },
  },
])(
  "pallet transfers keep basket scope without station activity ($direction)",
  ({ location, action }) => {
    const material = InProcessMaterial.fromJS({ Location: location, Action: action });
    expect(
      materialActionIsSuspended(
        { alarmDetails: [{ ...blocked, activity: AlarmActivity.BasketLoadStation }] },
        material,
      ),
    ).toBe(false);
    expect(
      materialActionIsSuspended({ alarmDetails: [{ ...blocked, basketId: 7 }] }, material),
    ).toBe(true);
    expect(
      materialActionIsSuspended({ alarmDetails: [{ ...blocked, basketId: 8 }] }, material),
    ).toBe(false);
  },
);
