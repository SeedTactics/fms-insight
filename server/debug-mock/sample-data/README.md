# Debug Mock Sample Data

The debug mock runs a real FMS Insight server over this sample data for manual review of the client.
The data is shifted so it ends near the current date.

- `events-*.json`: a month of simulated events, plus hand-added inspections, closeouts and
  workorders.
- `newjobs.json`, `tools.json`, `tool-use.json`, `programs.json`: the schedule, tools and programs.
- `status-mock.json`: the hand-written current status, kept consistent with the events.

Set `BMS_CURRENT_STATUS_FILE` to display a different `CurrentStatus` JSON file, such as a snapshot
produced by a scenario test (e.g. the Mazak `BuildCurrentStatusSpec` snapshots in
`server/test/mazak/read-snapshots`). The events and jobs remain the sample data above, so material
in the snapshot does not have matching history.

```bash
BMS_CURRENT_STATUS_FILE=/path/to/current-status.json dotnet run --project server/debug-mock
pnpm --dir client/insight start
```
