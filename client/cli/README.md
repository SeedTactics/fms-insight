# Insight CLI

`insight-cli` reads Insight history into a private SQLite database and queries it locally. It can be
used interactively or from scripts and agents. All server requests are read operations, including
the job-history POST; SQL queries use a read-only connection.

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project client/cli -- --help
```

The examples below use `insight-cli`; during development substitute
`dotnet run --project client/cli --`. Publishing executable downloads is deferred.

```sh
insight-cli sync --server http://cell-controller:5000 --database study.sqlite \
  --from 2026-10-01T00:00:00Z --to 2026-10-08T00:00:00Z
insight-cli sync --server http://cell-controller:5000 --database study.sqlite
insight-cli query --database study.sqlite --sql "SELECT * FROM metadata"
insight-cli query --database study.sqlite --sql-file cycles.sql --format csv > cycles.csv
insight-cli material --server http://cell-controller:5000 --database study.sqlite --id 123
insight-cli status --server http://cell-controller:5000 > status.json
insight-cli program --server http://cell-controller:5000 --name 123 --revision 2 > program.nc
```

Use a private directory and a new database path for the first sync. It requires both time bounds;
later runs resume that window, and a later `--to` extends it. Times require `Z` or an explicit UTC
offset. Source, lower bound or cache-schema changes require a new path. Keep existing evidence and
stop the writer before copying its database. Check the source after a checkpoint conflict; rebuild
at a new path if necessary.

Set `INSIGHT_TOKEN` to an externally obtained bearer token when required. Credentials are not stored
in the cache. SQL defaults to stdin and JSON output; `--format csv` includes a header. Results go to
stdout and diagnostics to stderr. Check the exit code before using output, which may be partial on
error or cancellation. Ctrl+C cancels a running command.

Inspect `metadata`, `coverage`, `material_coverage` and `unresolved_job_references` before analyzing
results. `complete` means the last sync finished; `job_context_complete` separately indicates
whether referenced plans were available. Neither proves complete material lifecycles or inspection
outcomes. `material --serial SERIAL` retrieves every matching material ID; matches can be ambiguous.
Targeted reads have separate coverage and do not advance global event progress. Status and program
reads do not update the historical cache.

Inspect the schema with SQL such as `SELECT name,sql FROM sqlite_schema WHERE type='table'` or
`PRAGMA table_info(events)`. The main tables are `events`, `event_material`, `material`, `jobs`,
`job_decrements`, `casting_actions`, `schedules`, `sim_station_use` and `sim_station_parts`.
Durations are seconds; timestamps are UTC.

- Filter event times for a bounded analysis: targeted history and supporting corrections can lie
  outside the sync window. Selected schedules are retrieved in full and can also extend beyond it.
- `jobs.plan_json` contains historical planning. Comments, holds, bookings and archive/delivery
  state remain only in response provenance. Original response JSON is not a historical snapshot of
  live state. Decrements are reconciled separately; job `cycles` is the original planned quantity.
- Elapsed time is recorded wall time; active time is the planning target. Count shared cycles and
  simulated station intervals once before joining their material or job participants.
- Material metadata on old events reflects the backend when retrieved. Casting actions preserve
  their own assignment context. Older servers may lack assignment/decrement events; unrecorded
  actions cannot be recovered. A long catch-up can require a large response from the server's
  unpaged recent-events endpoint.
