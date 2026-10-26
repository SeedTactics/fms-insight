import { AlarmImpact, type AlarmActivity, type ICurrentStatusAlarm } from "../network/api.js";

export interface AlarmContext {
  readonly activity?: AlarmActivity;
  readonly basketId?: number;
}

/** Apply the backend's explicit scope and impact; text, codes and missing metadata imply no block. */
export function alarmsBlockingChanges(
  status: { readonly alarmDetails?: ReadonlyArray<Readonly<ICurrentStatusAlarm>> | null },
  context: AlarmContext,
): ReadonlyArray<Readonly<ICurrentStatusAlarm>> {
  return (status.alarmDetails ?? []).filter(
    (alarm) =>
      alarm.impact === AlarmImpact.SuspendChanges &&
      (alarm.activity == null || alarm.activity === context.activity) &&
      (alarm.basketId == null || alarm.basketId === context.basketId),
  );
}
