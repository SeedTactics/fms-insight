import {
  ActionType,
  AlarmActivity,
  AlarmImpact,
  LocType,
  type ICurrentStatusAlarm,
  type IInProcessMaterial,
} from "../network/api.js";

export interface AlarmContext {
  readonly activity?: AlarmActivity;
  readonly basketId?: number;
}

type StatusAlarms = { readonly alarmDetails?: ReadonlyArray<Readonly<ICurrentStatusAlarm>> | null };

function applies(alarm: Readonly<ICurrentStatusAlarm>, context: AlarmContext): boolean {
  return (
    alarm.impact === AlarmImpact.SuspendChanges &&
    (alarm.activity == null || alarm.activity === context.activity) &&
    (alarm.basketId == null || alarm.basketId === context.basketId)
  );
}

/** Apply the backend's explicit scope and impact; text, codes and missing metadata imply no block. */
export function alarmsBlockingChanges(
  status: StatusAlarms,
  context: AlarmContext,
): ReadonlyArray<Readonly<ICurrentStatusAlarm>> {
  return (status.alarmDetails ?? []).filter((alarm) => applies(alarm, context));
}

/** Include every source/target basket in an atomic material operation, even in an open dialog. */
export function alarmsBlockingMaterialChanges(
  status: StatusAlarms,
  material: ReadonlyArray<Readonly<IInProcessMaterial>>,
): ReadonlyArray<Readonly<ICurrentStatusAlarm>> {
  const contexts: ReadonlyArray<AlarmContext> = [
    {},
    ...material.flatMap((mat) => {
      const station =
        mat.action.type === ActionType.LoadingToBasket ||
        mat.action.type === ActionType.UnloadToInProcess ||
        mat.action.type === ActionType.UnloadToCompletedMaterial;
      return [
        ...(mat.location.type === LocType.InBasket
          ? [
              {
                basketId: mat.location.basketId,
                activity: station ? AlarmActivity.BasketLoadStation : undefined,
              },
            ]
          : []),
        ...(mat.action.type === ActionType.LoadingToBasket
          ? [
              {
                basketId: mat.action.loadToBasketId,
                activity: AlarmActivity.BasketLoadStation,
              },
            ]
          : []),
        ...(mat.action.type === ActionType.Loading && mat.action.loadFromBasketId !== undefined
          ? [{ basketId: mat.action.loadFromBasketId }]
          : []),
        ...(station && mat.action.unloadToBasketId !== undefined
          ? [{ basketId: mat.action.unloadToBasketId, activity: AlarmActivity.BasketLoadStation }]
          : []),
      ];
    }),
  ];
  return (status.alarmDetails ?? []).filter((alarm) =>
    contexts.some((context) => applies(alarm, context)),
  );
}

/** Suspend human transfer cues while retaining read-only observations such as machining/waiting. */
export function materialActionIsSuspended(
  status: StatusAlarms,
  material: Readonly<IInProcessMaterial>,
): boolean {
  return (
    (material.action.type === ActionType.Loading ||
      material.action.type === ActionType.LoadingToBasket ||
      material.action.type === ActionType.UnloadToInProcess ||
      material.action.type === ActionType.UnloadToCompletedMaterial) &&
    alarmsBlockingMaterialChanges(status, [material]).length > 0
  );
}
