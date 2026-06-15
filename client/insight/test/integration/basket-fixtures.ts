import * as api from "../../src/network/api.js";
import { addDays, startOfToday } from "date-fns";
import type { InsightTestData } from "./framework.js";
import {
  createBasket,
  createCurrentStatus,
  createMaterial,
  createPallet,
} from "./load-station-testkit.js";
import { fakeBasketCycle, fakeBasketLoadOrUnload, fakeLoadOrUnload } from "../events.fake.js";

function createLogMaterial({
  materialID,
  jobUnique,
  partName,
  process,
  serial,
  face = 1,
  workorder = "WO-TRAY",
}: {
  readonly materialID: number;
  readonly jobUnique: string;
  readonly partName: string;
  readonly process: number;
  readonly serial: string;
  readonly face?: number;
  readonly workorder?: string;
}): api.LogMaterial {
  return new api.LogMaterial({
    id: materialID,
    uniq: jobUnique,
    part: partName,
    proc: process,
    path: 1,
    numproc: 2,
    face,
    serial,
    workorder,
  });
}

function createBasketLocationLog({
  counter,
  basket,
  time,
  startOfCycle,
  location,
  locationNum,
}: {
  readonly counter: number;
  readonly basket: number;
  readonly time: Date;
  readonly startOfCycle: boolean;
  readonly location: string;
  readonly locationNum: number;
}): api.LogEntry {
  return new api.LogEntry({
    counter,
    material: [],
    pal: basket,
    type: api.LogType.BasketInLocation,
    startofcycle: startOfCycle,
    endUTC: time,
    loc: location,
    locnum: locationNum,
    result: "",
    program: "",
    elapsed: "PT0S",
    active: "PT0S",
  });
}

function createRecentHistoricData(jobs: ReadonlyArray<api.ActiveJob>): api.RecentHistoricData {
  return new api.RecentHistoricData({
    jobs: Object.fromEntries(jobs.map((job) => [job.unique, job])),
    stationUse: [],
  });
}

function recentLogTime(dayOffset: number, hour: number, minute: number): Date {
  const time = addDays(startOfToday(), dayOffset);
  time.setUTCHours(hour, minute, 0, 0);
  return time;
}

function createBasketJob(): api.ActiveJob {
  return new api.ActiveJob({
    unique: "JOB-TRAY",
    routeStartUTC: new Date("2026-04-20T08:00:00Z"),
    routeEndUTC: new Date("2026-05-05T08:00:00Z"),
    archived: false,
    partName: "Tray Part",
    copiedToSystem: true,
    comment: "Basket-enabled route",
    cycles: 3,
    assignedWorkorders: ["WO-TRAY"],
    remainingToStart: 2,
    completed: [[1]],
    precedence: [[0]],
    procsAndPaths: [
      new api.ProcessInfo({
        basketLoadStations: [2],
        expectedBasketLoadTime: "PT4M",
        basketUnloadStations: [2],
        expectedBasketUnloadTime: "PT3M",
        paths: [
          new api.ProcPathInfo({
            palletNums: [1],
            fixture: "Fixture A",
            face: 1,
            load: [1],
            expectedLoadTime: "PT6M",
            unload: [2],
            expectedUnloadTime: "PT5M",
            stops: [
              new api.MachiningStop({
                stationGroup: "MC",
                stationNums: [5],
                expectedCycleTime: "PT15M",
                program: "CUT-1",
              }),
            ],
            simulatedStartingUTC: new Date("2026-04-22T10:00:00Z"),
            simulatedAverageFlowTime: "PT1H",
            partsPerPallet: 1,
            inputQueue: "Queue Alpha",
            outputQueue: "Queue Gamma",
            casting: "Raw Casting",
          }),
        ],
      }),
    ],
  });
}

function createPalletOnlyJob(): api.ActiveJob {
  return new api.ActiveJob({
    unique: "JOB-PALLET",
    routeStartUTC: new Date("2026-04-20T08:00:00Z"),
    routeEndUTC: new Date("2026-05-05T08:00:00Z"),
    archived: false,
    partName: "Pallet Part",
    copiedToSystem: true,
    cycles: 2,
    assignedWorkorders: ["WO-PALLET"],
    remainingToStart: 1,
    completed: [[0]],
    precedence: [[0]],
    procsAndPaths: [
      new api.ProcessInfo({
        paths: [
          new api.ProcPathInfo({
            palletNums: [11],
            fixture: "Fixture B",
            face: 1,
            load: [1],
            expectedLoadTime: "PT5M",
            unload: [1],
            expectedUnloadTime: "PT4M",
            stops: [
              new api.MachiningStop({
                stationGroup: "MC",
                stationNums: [3],
                expectedCycleTime: "PT12M",
                program: "TURN-1",
              }),
            ],
            simulatedStartingUTC: new Date("2026-04-22T10:00:00Z"),
            simulatedAverageFlowTime: "PT45M",
            partsPerPallet: 1,
            inputQueue: "Queue Alpha",
            outputQueue: "Queue Beta",
          }),
        ],
      }),
    ],
  });
}

export interface MixedBasketFixture {
  readonly data: InsightTestData;
  readonly job: api.ActiveJob;
  readonly materials: {
    readonly queueToTray: api.InProcessMaterial;
    readonly trayToQueue: api.InProcessMaterial;
    readonly stagingTray: api.InProcessMaterial;
    readonly storageTray: api.InProcessMaterial;
    readonly floatingTray: api.InProcessMaterial;
    readonly trayToPallet: api.InProcessMaterial;
    readonly palletToTray: api.InProcessMaterial;
    readonly palletWaiting: api.InProcessMaterial;
    readonly queuedWaiting: api.InProcessMaterial;
    readonly stockerWaiting: api.InProcessMaterial;
  };
}

export function createMixedBasketFixture(): MixedBasketFixture {
  const job = createBasketJob();

  const palletAtLoad = createPallet({
    palletNum: 1,
    currentPalletLocation: {
      loc: api.PalletLocationEnum.LoadUnload,
      group: "L/U",
      num: 1,
    },
    numFaces: 2,
    faceNames: ["Alpha", "Beta"],
  });
  const stockerPallet = createPallet({
    palletNum: 2,
    currentPalletLocation: {
      loc: api.PalletLocationEnum.Buffer,
      group: "Buffer",
      num: 1,
    },
    numFaces: 1,
    faceNames: ["Stocker"],
  });

  const materials = {
    queueToTray: createMaterial({
      materialID: 501,
      jobUnique: job.unique,
      partName: job.partName,
      process: 0,
      path: 1,
      serial: "QT-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.InQueue,
        currentQueue: "Queue Alpha",
        queuePosition: 0,
      },
      action: {
        type: api.ActionType.Loading,
        loadFromBasketId: 21,
        processAfterLoad: 1,
      },
    }),
    trayToQueue: createMaterial({
      materialID: 502,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "TQ-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.InBasket,
        basketId: 21,
        basketSubPosition: 0,
      },
      action: {
        type: api.ActionType.UnloadToInProcess,
        unloadIntoQueue: "Queue Beta",
      },
    }),
    stagingTray: createMaterial({
      materialID: 503,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "ST-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.InBasket,
        basketId: 22,
        basketSubPosition: 0,
      },
      action: {
        type: api.ActionType.Waiting,
      },
    }),
    storageTray: createMaterial({
      materialID: 504,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "SG-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.InBasket,
        basketId: 23,
        basketSubPosition: 0,
      },
      action: {
        type: api.ActionType.Waiting,
      },
    }),
    floatingTray: createMaterial({
      materialID: 505,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "FT-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.InBasket,
        basketId: 25,
        basketSubPosition: 0,
      },
      action: {
        type: api.ActionType.Waiting,
      },
    }),
    trayToPallet: createMaterial({
      materialID: 506,
      jobUnique: job.unique,
      partName: job.partName,
      process: 0,
      path: 1,
      serial: "TP-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.InBasket,
        basketId: 22,
        basketSubPosition: 1,
      },
      action: {
        type: api.ActionType.Loading,
        loadFromBasketId: 22,
        loadOntoPalletNum: 1,
        loadOntoFace: 1,
        processAfterLoad: 1,
      },
    }),
    palletToTray: createMaterial({
      materialID: 507,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "PT-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.OnPallet,
        palletNum: 1,
        face: 2,
      },
      action: {
        type: api.ActionType.UnloadToInProcess,
        unloadToBasketId: 22,
        unloadToBasketSubPosition: 1,
      },
    }),
    palletWaiting: createMaterial({
      materialID: 508,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "PW-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.OnPallet,
        palletNum: 1,
        face: 1,
      },
      action: {
        type: api.ActionType.Waiting,
      },
    }),
    queuedWaiting: createMaterial({
      materialID: 509,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "QW-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.InQueue,
        currentQueue: "Queue Beta",
        queuePosition: 0,
      },
      action: {
        type: api.ActionType.Waiting,
      },
    }),
    stockerWaiting: createMaterial({
      materialID: 510,
      jobUnique: job.unique,
      partName: job.partName,
      process: 1,
      path: 1,
      serial: "SP-1",
      workorderId: "WO-TRAY",
      location: {
        type: api.LocType.OnPallet,
        palletNum: 2,
        face: 1,
      },
      action: {
        type: api.ActionType.Waiting,
      },
    }),
  };

  const currentStatus = createCurrentStatus({
    pallets: [palletAtLoad, stockerPallet],
    baskets: [
      createBasket({
        basketId: 21,
        location: api.BasketLocationEnum.LoadUnload,
        locationNum: 2,
      }),
      createBasket({
        basketId: 22,
        location: api.BasketLocationEnum.LoadStationStaging,
        locationNum: 2,
      }),
      createBasket({
        basketId: 23,
        location: api.BasketLocationEnum.Storage,
        locationNum: 0,
      }),
      createBasket({
        basketId: 24,
        location: api.BasketLocationEnum.Storage,
        locationNum: 0,
      }),
      createBasket({
        basketId: 25,
        location: api.BasketLocationEnum.InTransit,
        locationNum: 0,
      }),
    ],
    material: Object.values(materials),
  });
  currentStatus.jobs = { [job.unique]: job };

  const queueToTrayLog = createLogMaterial({
    materialID: materials.queueToTray.materialID,
    jobUnique: job.unique,
    partName: job.partName,
    process: 1,
    serial: materials.queueToTray.serial ?? "",
  });
  const trayToQueueLog = createLogMaterial({
    materialID: materials.trayToQueue.materialID,
    jobUnique: job.unique,
    partName: job.partName,
    process: materials.trayToQueue.process,
    serial: materials.trayToQueue.serial ?? "",
  });
  const trayToPalletLog = createLogMaterial({
    materialID: materials.trayToPallet.materialID,
    jobUnique: job.unique,
    partName: job.partName,
    process: 1,
    serial: materials.trayToPallet.serial ?? "",
  });
  const palletToTrayLog = createLogMaterial({
    materialID: materials.palletToTray.materialID,
    jobUnique: job.unique,
    partName: job.partName,
    process: materials.palletToTray.process,
    serial: materials.palletToTray.serial ?? "",
    face: 2,
  });

  const last30Log = [
    ...fakeBasketLoadOrUnload({
      counter: 7000,
      material: [queueToTrayLog],
      basket: 21,
      part: job.partName,
      proc: 1,
      isLoad: true,
      lulNum: 2,
      time: recentLogTime(-1, 12, 10),
      elapsedMin: 4,
      activeMin: 4,
    }),
    ...fakeBasketLoadOrUnload({
      counter: 7010,
      material: [trayToQueueLog],
      basket: 21,
      part: job.partName,
      proc: 1,
      isLoad: false,
      lulNum: 2,
      time: recentLogTime(-1, 12, 20),
      elapsedMin: 5,
      activeMin: 5,
    }),
    ...fakeBasketCycle({
      counter: 7020,
      basket: 21,
      part: job.partName,
      proc: 1,
      numMats: 1,
      time: recentLogTime(-1, 12, 30),
      elapsedMin: 18,
      activeMin: 15,
    }),
    createBasketLocationLog({
      counter: 7030,
      basket: 21,
      time: recentLogTime(-1, 11, 55),
      startOfCycle: false,
      location: "Load Station Staging",
      locationNum: 2,
    }),
    createBasketLocationLog({
      counter: 7031,
      basket: 21,
      time: recentLogTime(-1, 12, 0),
      startOfCycle: true,
      location: "Load Station Staging",
      locationNum: 2,
    }),
    ...fakeLoadOrUnload({
      counter: 7040,
      material: [trayToPalletLog],
      pal: 1,
      part: job.partName,
      proc: 1,
      isLoad: true,
      lulNum: 1,
      time: recentLogTime(-1, 12, 35),
      elapsedMin: 6,
      activeMin: 6,
    }),
    ...fakeLoadOrUnload({
      counter: 7050,
      material: [palletToTrayLog],
      pal: 1,
      part: job.partName,
      proc: 1,
      isLoad: false,
      lulNum: 1,
      time: recentLogTime(-1, 12, 45),
      elapsedMin: 7,
      activeMin: 7,
    }),
  ];

  return {
    data: {
      currentStatus,
      recentHistoricData: createRecentHistoricData([job]),
      last30Log,
      fmsInfo: {
        basketName: "Tray",
        loadStationNames: {
          "1": "Prep Cell",
          "2": "Tray Cell",
        },
      },
    },
    job,
    materials,
  };
}

export function createPalletOnlyFixture(): {
  readonly data: InsightTestData;
  readonly job: api.ActiveJob;
} {
  const job = createPalletOnlyJob();
  const currentStatus = createCurrentStatus({
    pallets: [
      createPallet({
        palletNum: 11,
        currentPalletLocation: {
          loc: api.PalletLocationEnum.LoadUnload,
          group: "L/U",
          num: 1,
        },
        numFaces: 2,
        faceNames: ["Front", "Back"],
      }),
    ],
    material: [
      createMaterial({
        materialID: 801,
        jobUnique: job.unique,
        partName: job.partName,
        process: 0,
        path: 1,
        serial: "PQ-1",
        location: {
          type: api.LocType.InQueue,
          currentQueue: "Queue Alpha",
          queuePosition: 0,
        },
        action: {
          type: api.ActionType.Loading,
          loadOntoPalletNum: 11,
          loadOntoFace: 1,
          processAfterLoad: 1,
        },
      }),
      createMaterial({
        materialID: 802,
        jobUnique: job.unique,
        partName: job.partName,
        process: 1,
        path: 1,
        serial: "PP-1",
        location: {
          type: api.LocType.OnPallet,
          palletNum: 11,
          face: 1,
        },
        action: {
          type: api.ActionType.Waiting,
        },
      }),
    ],
  });
  currentStatus.jobs = { [job.unique]: job };

  return {
    data: {
      currentStatus,
      recentHistoricData: createRecentHistoricData([job]),
      last30Log: [
        ...fakeLoadOrUnload({
          counter: 9000,
          part: job.partName,
          proc: 1,
          pal: 11,
          isLoad: true,
          lulNum: 1,
          time: new Date("2026-04-24T12:00:00Z"),
          elapsedMin: 5,
          activeMin: 5,
        }),
      ],
      fmsInfo: {
        loadStationNames: {
          "1": "Prep Cell",
        },
      },
    },
    job,
  };
}

export function createBasketOutlierLogs(): ReadonlyArray<Readonly<api.ILogEntry>> {
  const baseTime = new Date("2026-04-25T09:00:00Z");
  const logs: Array<Readonly<api.ILogEntry>> = [];
  for (let idx = 0; idx < 6; idx += 1) {
    logs.push(
      ...fakeBasketLoadOrUnload({
        counter: 9500 + idx * 10,
        basket: 21,
        part: "Tray Part",
        proc: 1,
        numMats: 1,
        isLoad: true,
        lulNum: 2,
        time: new Date(baseTime.getTime() + idx * 60 * 60 * 1000),
        elapsedMin: 4,
        activeMin: 4,
      }),
    );
  }
  logs.push(
    ...fakeBasketLoadOrUnload({
      counter: 9600,
      basket: 21,
      part: "Tray Part",
      proc: 1,
      numMats: 1,
      isLoad: true,
      lulNum: 2,
      time: new Date(baseTime.getTime() + 8 * 60 * 60 * 1000),
      elapsedMin: 15,
      activeMin: 15,
    }),
  );
  return logs;
}
