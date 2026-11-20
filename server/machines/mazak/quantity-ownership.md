# Job quantity ownership

By default, Mazak's schedule quantities drive current job counts, raw supply reconciliation,
decrement, and completed-schedule cleanup. Existing installations keep this behavior.

A custom backend can select jobs through `MazakConfig.UseInsightJobQuantities`, a pure predicate
derived from job routes and backend configuration. Keep the selection stable for each job's
lifetime. This policy is internal to the backend; accepted jobs and allocation clients need no new
flag. One cell can contain jobs using either policy.

Selected jobs use repository production accounting: accepted cycles minus recorded cancellations,
durable first entry, and recorded completions. Controller plan increases do not increase demand.
Current status keeps unarchived selected jobs even when their schedules disappear. Explicitly
archived selected jobs stay out of current status and new downloads even if controller rows or the
copied-to-system flag lag behind archival. The backend can project its additional active
reservations through `AdjustCurrentStatus`.

Controller completion evidence remains visible while its event batch is settling; repository history
preserves completion for retired split routes. Actual machining also establishes durable entry when
its preceding LOAD was unavailable, without synthesizing a LOAD event.

For local cancellation, `RecordJobDecrementOperation` commits positive once-only cancellations,
release of never-entered raw allocations, and a completed request receipt together. Replays return
the original batch, including empty results. `LoadJobDecrementOperation` only reads that receipt.
The backend still owns eligibility, commitment calculation, admission serialization and publication.
Ordinary `AddNewDecrement` callers retain their existing multiple-entry behavior.

The backend owns supply admission, cancellation, and job archival for selected jobs. Generic queue
reconciliation skips their raw supply. The generic decrement command refuses an installation
containing selected, unarchived, non-manual jobs before making any changes. Its replacement belongs
to the backend, including decisions about jobs on the default policy.

Controller-owned backout requires successful synchronization, settled Mazak events, and confirmed
downloads for all unarchived, non-manual jobs without a previous decrement. Unconfirmed downloads
cause refusal before any controller edits, including when only pending jobs exist or their route
dates fall outside the usual download window. Backout does not cancel pending jobs or run downloads
itself. A failed write can have partial controller effects; the ordinary synchronization loop must
succeed before retrying. This is a conservative refusal policy, not an atomic controller rollback.

## Managed schedule lifecycle

The normal downloader installs each selected route with zero material offered. A combined job has
one native schedule. The existing split-job rules give independently admitted processes one schedule
each. When two controllers share a repository, set `BulkJobFilter` for each controller's assigned
jobs and give each controller a stable `ControllerSource`. The original controller retains its usual
CSV history on `DefaultControllerSource`.

Set `MazakConfig.MachineGroupName` to the machining station group on each controller when the groups
differ. The override applies to machining events, pallet locations, and tool queries. Leaving it
unset uses the group inferred from the latest accepted schedule.

The downloader saves the native route identity before its first controller write and confirms the
schedule from readback. A saved route or a matching native part lets an interrupted installation
finish beyond the normal download window. Adoption and positive offers verify the effective program
and revision using the same process configuration as download. A managed route with a pinned
revision uses the controller program tagged with that revision, even when the job's program name is
also a native program number.

A required program can disappear after job acceptance. When the job is due for download, a missing
program or conflicting native definition raises a route alarm and holds that job while other
eligible jobs can download. Correcting the controller's definition lets the held job resume. After a
partial cancellation, managed jobs can still download at zero offer for the remaining work. Archival
also cleans up a partial installation with no confirmed schedule. A confirmed route retains the same
full native part name and schedule number through the job's active life, including at planned equals
completed. A missing or conflicting route raises a route-specific alarm for backend disposition.

Use `MazakManagedScheduleControl.EnsurePlan` with an absolute minimum quantity and
`SetMaterialOffer` with the currently authorized ready quantity. A combined schedule admits material
at process 1. Positive offers require the installed fixture quantity, pallet mappings, and eligible
stations to match the job route. Withdrawal remains available when the schedule's identity is
verified and its execution definition has changed. The plan is a native execution ceiling.
Increasing it leaves Insight demand and the current offer intact. Serialize the writes with the
controller's normal acquisition and download owner. After an uncertain write, acquire fresh readback
and recompute the authorized offer before writing again, since the controller may have consumed it.

`RecordedLoads` reports physical loads within the confirmed schedule's history, including
invalidated attempts and separately identified and unidentified quantities. Adopted schedules
without a recorded creation boundary have unknown attempt totals. Valid Insight completions come
from repository manufacturing history. Pending loads from a managed job's queue show the requested
quantity with anonymous material cards. The queued pieces remain visible until the backend reports
which pieces transferred. Queue-only and basket backends both need
`MazakConfig.ResolveLoadUnloadTransaction` to record material IDs for managed loads. Without a valid
resolution, the LOAD event records physical quantity with unidentified material.

After an unidentified unload, pallet status leaves the affected serials unknown. Exact transfer
evidence can identify individual pieces. While the backend reports a pending transfer, an empty
pallet observation preserves the possible serials for a delayed unload. Settled empty readback can
end an uncertain placement, and moving known missing pieces to quarantine releases their places on
the pallet. Both preserve the original manufacturing history. Historical native part identity
remains available for late events after retirement and schedule number reuse.

Settle the backend's outstanding transfers and withdraw offers before archiving the Insight job.
Archival authorizes the ordinary synchronization loop to clean up the route, including an idle
schedule with unequal planned and completed counts. Requests, pallet assignments, ready material and
executing material defer deletion. The adapter rechecks readback between cleanup writes, including
after restart, and preserves dependencies used by surviving routes. Cleanup can run without a new
download.
