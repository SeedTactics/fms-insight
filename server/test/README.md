# Server tests

Run the suite from the repository root with `dotnet run --project server/test`.

Snapshots use `Snapshot.Match` to compare text against a committed `.verified.*` file beside the
calling test source. Names may include a subdirectory. A missing or different baseline fails the
test and writes a `.received.*` file beside it; the error reports both paths. Matching snapshots
remove stale received files. Text comparison normalizes only line endings. JSON comparison ignores
whitespace and object property order, while checking values and array order.

Use `Snapshot.Json` to serialize JSON with the test's serializer options and sort object keys for
deterministic output. Array order and values, including dates and GUIDs, are preserved. Fix clocks
and IDs in test inputs, or explicitly normalize individual fields in the test when necessary.

Inspect changes with ordinary tools such as `diff -u expected.verified.json expected.received.json`.
After reviewing the output, copy the received file over the verified file and rerun the tests.
Snapshots are excluded from formatting, and received files are ignored by version control.
