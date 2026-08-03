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

Open `http://localhost:1234/station/loadunload/4?completed=t`. Use `z-basket-load` instead to review
the load phase.

In a Vite development build, the completion button uses a client-side fake that accepts the command
without changing server state. Production builds do not include the fake. Restart the debug server
with the other status to switch phases.
