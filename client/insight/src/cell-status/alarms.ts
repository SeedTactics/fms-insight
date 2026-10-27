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

type StatusAlarms = {
  readonly alarmDetails?: ReadonlyArray<Readonly<ICurrentStatusAlarm>> | null;
  readonly material?: ReadonlyArray<Readonly<IInProcessMaterial>>;
};

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

/** Include captured and current source/target scopes without changing the selected operation. */
export function alarmsBlockingMaterialChanges(
  status: StatusAlarms,
  material: ReadonlyArray<Readonly<IInProcessMaterial>>,
): ReadonlyArray<Readonly<ICurrentStatusAlarm>> {
  if (!status.alarmDetails?.length) return [];
  const affectedMaterial = material.flatMap((captured) => {
    // Anonymous entries share ID -1, so they cannot be matched to a newer material entry.
    const latest =
      captured.materialID >= 0
        ? status.material?.find((candidate) => candidate.materialID === captured.materialID)
        : undefined;
    return latest && latest !== captured ? [captured, latest] : [captured];
  });
  const contexts: ReadonlyArray<AlarmContext> = [
    {},
    ...affectedMaterial.flatMap((mat) => {
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
          ? [
              {
                basketId: mat.action.loadFromBasketId,
                // Without a pallet, this legacy field names the station's destination basket.
                activity:
                  mat.action.loadOntoPalletNum == null
                    ? AlarmActivity.BasketLoadStation
                    : undefined,
              },
            ]
          : []),
        ...(station && mat.action.unloadToBasketId !== undefined
          ? [{ basketId: mat.action.unloadToBasketId }]
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
