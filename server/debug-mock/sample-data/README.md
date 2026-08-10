# Debug Mock Current Statuses

The debug mock loads every `status-*.json` file in this directory. Select one at startup with
`BMS_CURRENT_STATUS`, using the portion of the filename after `status-` and before `.json`.

The basket load-station QA states are:

- `z-basket-unload`: process-1 material unloading to an in-process queue and completed material
  unloading from a numbered basket;
- `z-basket-load`: raw material and in-process queue material loading into numbered basket slots.

From the repository root, start the selected server and the Vite client in separate terminals:

```bash
BMS_CURRENT_STATUS=z-basket-unload dotnet run --project server/debug-mock
pnpm --dir client/insight start
```

For a complete status document outside this repository, set `BMS_CURRENT_STATUS_FILE` to its path.
The file is deserialized as `CurrentStatus` with the normal debug-mock JSON settings and timestamp
offset. An explicit file must exist and be valid; startup reports an error instead of falling back
to a built-in status.

```bash
BMS_CURRENT_STATUS_FILE=/path/to/current-status.json dotnet run --project server/debug-mock
```

For interactive client flows, set `BMS_CURRENT_STATUS_SCENARIO` to a scripted scenario manifest.
Each named step references a full `CurrentStatus` file. A transition matches an opaque HTTP method
and path, returns its canned response, selects the next step, and publishes that status through the
normal websocket. The first version supports zero or one transition from each step.

```json
{
  "initial": "before",
  "steps": {
    "before": {
      "status": "00-before.json",
      "on": [
        {
          "method": "POST",
          "path": "/api/example/action",
          "response": { "status": 200, "json": { "Accepted": true } },
          "next": "after"
        }
      ]
    },
    "after": { "status": "01-after.json" }
  }
}
```

Status paths are relative to the manifest. Use the generic development endpoints to inspect,
advance, or reset an active scenario:

```text
GET  /api/debug-mock/scenario
POST /api/debug-mock/scenario/next
POST /api/debug-mock/scenario/reset
```

`BMS_CURRENT_STATUS_FILE` and `BMS_CURRENT_STATUS_SCENARIO` are mutually exclusive. Scenario mode
also disables debug-mock's periodic alarm changes so only scripted transitions change status.

Open `http://localhost:1234/station/loadunload/4?completed=t`. Use `z-basket-load` instead to review
the load phase.

In a Vite development build, the completion button uses a client-side fake that accepts the command
without changing server state. Production builds do not include the fake. Restart the debug server
with the other status to switch phases.
