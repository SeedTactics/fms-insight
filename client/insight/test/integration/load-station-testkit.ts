import * as api from "../../src/network/api.js";

type PalletInput = Omit<
  Partial<api.IPalletStatus>,
  "currentPalletLocation" | "targetLocation" | "palletNum" | "numFaces"
> & {
  readonly palletNum: number;
  readonly numFaces: number;
  readonly currentPalletLocation: api.IPalletLocation;
  readonly targetLocation?: api.IPalletLocation;
};

type MaterialInput = Omit<
  Partial<api.IInProcessMaterial>,
  "location" | "action" | "materialID" | "jobUnique" | "partName" | "process" | "path"
> & {
  readonly materialID: number;
  readonly jobUnique: string;
  readonly partName: string;
  readonly process: number;
  readonly path: number;
  readonly location: api.IInProcessMaterialLocation;
  readonly action: api.IInProcessMaterialAction;
};

export function createCurrentStatus({
  pallets = [],
  baskets = [],
  material = [],
}: {
  readonly pallets?: ReadonlyArray<api.PalletStatus>;
  readonly baskets?: ReadonlyArray<api.BasketStatus>;
  readonly material?: ReadonlyArray<api.InProcessMaterial>;
} = {}): api.CurrentStatus {
  return new api.CurrentStatus({
    timeOfCurrentStatusUTC: new Date("2026-04-24T12:00:00Z"),
    jobs: {},
    pallets: Object.fromEntries(pallets.map((p) => [p.palletNum, p])),
    material: Array.from(material),
    alarms: [],
    queues: {},
    baskets: Object.fromEntries(baskets.map((b) => [b.basketId, b])),
  });
}

export function createPallet(data: PalletInput): api.PalletStatus {
  return new api.PalletStatus({
    ...data,
    fixtureOnPallet: data.fixtureOnPallet ?? "",
    onHold: data.onHold ?? false,
    currentPalletLocation: new api.PalletLocation(data.currentPalletLocation),
    targetLocation: data.targetLocation ? new api.PalletLocation(data.targetLocation) : undefined,
  });
}

export function createBasket(data: api.IBasketStatus): api.BasketStatus {
  return new api.BasketStatus(data);
}

export function createMaterial(data: MaterialInput): api.InProcessMaterial {
  return new api.InProcessMaterial({
    ...data,
    signaledInspections: data.signaledInspections ?? [],
    quarantineAfterUnload: data.quarantineAfterUnload ?? undefined,
    workorderId: data.workorderId ?? "",
    location: new api.InProcessMaterialLocation(data.location),
    action: new api.InProcessMaterialAction(data.action),
  });
}

export function createCompletedUnloadLog({
  counter,
  materialId,
  partName,
  serial,
  process,
  numProcesses,
}: {
  readonly counter: number;
  readonly materialId: number;
  readonly partName: string;
  readonly serial: string;
  readonly process: number;
  readonly numProcesses: number;
}): api.LogEntry {
  return new api.LogEntry({
    counter,
    material: [
      new api.LogMaterial({
        id: materialId,
        uniq: "",
        part: partName,
        proc: process,
        path: 1,
        numproc: numProcesses,
        face: 1,
        serial,
        workorder: "",
      }),
    ],
    type: api.LogType.LoadUnloadCycle,
    startofcycle: false,
    endUTC: new Date(),
    loc: "L/U",
    locnum: 1,
    pal: 1,
    program: "",
    result: "UNLOAD",
    elapsed: "PT1M",
    active: "PT1M",
  });
}

export function queueRegionTestId(queue: string): string {
  return `load-station-queue-${encodeURIComponent(queue)}`;
}

export function basketRegionTestId(basketId: number): string {
  return `load-station-basket-${encodeURIComponent(basketId.toString())}`;
}

export function palletFaceRegionTestId(faceNum: number): string {
  return `load-station-pallet-face-${faceNum}`;
}

export const activeBasketRegionTestId = "load-station-active-basket";
export const completedRegionTestId = "load-station-completed";
export const basketsColumnTestId = "load-station-baskets";

export function region<T>(screen: { getByTestId: (testId: string) => T }, testId: string): T {
  return screen.getByTestId(testId);
}
