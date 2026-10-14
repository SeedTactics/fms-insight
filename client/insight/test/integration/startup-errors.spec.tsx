import { afterEach, expect, test, vi } from "vitest";
import { page } from "vitest/browser";
import App from "../../src/components/App.js";
import { currentRoute, RouteLocation } from "../../src/components/routes.js";
import { CurrentStatus, ServerEvent } from "../../src/network/api.js";
import { registerNetworkBackend } from "../../src/network/backend.js";
import { currentStatusIsLive } from "../../src/network/websocket.js";
import { renderInsightPage } from "./framework.js";

class TestSocket extends EventTarget {
  static sockets: ReadonlyArray<TestSocket> = [];

  constructor(readonly url: string) {
    super();
    TestSocket.sockets = [...TestSocket.sockets, this];
  }

  close(): void {
    this.dispatchEvent(new Event("close"));
  }
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function status(alarms: ReadonlyArray<string>): CurrentStatus {
  return new CurrentStatus({
    timeOfCurrentStatusUTC: new Date(),
    jobs: {},
    pallets: {},
    material: [],
    alarms: [...alarms],
    queues: {},
  });
}

async function startup(alarms: ReadonlyArray<string>, historyProblem: string | null) {
  TestSocket.sockets = [];
  vi.stubGlobal("WebSocket", TestSocket);
  vi.spyOn(window, "fetch").mockImplementation(async (input) => {
    const path = new URL(input instanceof Request ? input.url : input, window.location.href)
      .pathname;
    if (path === "/api/v1/jobs/status") return Response.json(status(alarms).toJSON());
    if (path === "/api/v1/jobs/recent" || path === "/api/v1/log/events/all") {
      return historyProblem === null
        ? Response.json(path === "/api/v1/jobs/recent" ? { jobs: {}, stationUse: [] } : [])
        : new Response(historyProblem, { status: 503 });
    }
    throw new Error(`Unexpected startup request ${path}`);
  });
  registerNetworkBackend();
  const screen = await renderInsightPage(
    <App renderCustomPage={() => ({ nav: undefined, page: <main>Custom status page</main> })} />,
    { route: { route: RouteLocation.ChooseMode } },
  );
  await expect.poll(() => TestSocket.sockets.length).toBe(1);
  const socket = TestSocket.sockets[0];
  if (!socket) throw new Error("Startup did not open a websocket");
  socket.dispatchEvent(new Event("open"));
  await expect.poll(() => screen.store.get(currentStatusIsLive)).toBe(true);
  return { screen, socket };
}

test("initial page displays the configuration alarm once when history endpoints are disabled", async () => {
  const alarm = "Configuration error: Controller address is required";
  const { screen } = await startup([alarm], JSON.stringify({ status: 503, detail: alarm }));
  await expect
    .element(screen.getByRole("heading", { name: "Select user and computer location" }))
    .toBeVisible();
  await expect.element(screen.getByRole("alert")).toMatchTextContent(alarm);
  await expect.element(screen.getByText(alarm, { exact: true })).toBeVisible();
  await expect.element(page.getByRole("dialog")).not.toBeInTheDocument();
  await expect
    .element(screen.getByRole("button", { name: "Error", exact: true }))
    .not.toBeInTheDocument();
  await expect
    .element(screen.getByText("An unexpected server error occurred."))
    .not.toBeInTheDocument();
  await expect.element(screen.getByText("Load Station", { exact: true })).toBeVisible();

  // Other pages retain access to the loading-error dialog, including the server's detail.
  screen.store.set(currentRoute, { route: RouteLocation.Client_Custom, custom: ["status"] });
  await screen.getByRole("button", { name: "Error", exact: true }).click();
  await expect.element(page.getByRole("dialog")).toMatchTextContent(alarm);
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await screen.unmount();
});

test("initial page displays the server problem when status has no alarm", async () => {
  const detail = "The history database is unavailable.";
  const { screen } = await startup([], JSON.stringify({ detail }));
  await expect.element(screen.getByRole("alert")).toMatchTextContent(detail);
  await expect.element(page.getByRole("dialog")).not.toBeInTheDocument();
  await screen.unmount();
});

test.each(["<html>Service unavailable</html>", JSON.stringify({ detail: 123 })])(
  "initial page falls back to the API error for an unsupported response: %s",
  async (problem) => {
    const { screen } = await startup([], problem);
    await expect
      .element(screen.getByRole("alert"))
      .toMatchTextContent("An unexpected server error occurred.");
    await screen.unmount();
  },
);

test("initial page removes resolved alarms when current status changes", async () => {
  const alarm = "Controller connection unavailable";
  const { screen, socket } = await startup([alarm], null);
  await expect.element(screen.getByRole("alert")).toMatchTextContent(alarm);
  socket.dispatchEvent(
    new MessageEvent("message", {
      data: JSON.stringify(new ServerEvent({ newCurrentStatus: status([]) }).toJSON()),
    }),
  );
  await expect.element(screen.getByRole("alert")).not.toBeInTheDocument();
  await expect.element(screen.getByText("Load Station", { exact: true })).toBeVisible();
  await screen.unmount();
});
