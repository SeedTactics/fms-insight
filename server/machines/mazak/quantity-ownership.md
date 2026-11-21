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

The normal downloader installs each managed route with zero material offered. A combined job has one
native schedule and admits material at process 1. Split jobs have one schedule per process. The
downloader confirms the native part name and schedule ID from readback. It keeps the route installed
throughout the job's active life, even when planned quantity equals completed quantity. A missing
schedule or conflicting definition raises a route alarm. Other eligible jobs can still download.

A backend that owns queue admission can call `MazakSync.ApplyJobActions` for download and cleanup.
`WriteJobs.FindPendingDownload` previews the batch. Both accept additional managed job IDs outside
the download window. Serialize preview, validation, and writes with the controller's synchronization
loop when validating a specific batch.

| `MazakManagedScheduleControl` | Backend contract                                                                                                                                                                                                                  |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `VerifyInstalledRoute`        | Check the native schedule and installed execution route before reporting readiness.                                                                                                                                               |
| `EnsurePlan`                  | Supply an absolute minimum for the native plan. A repeat attempt can raise this ceiling while the accepted job quantity stays fixed.                                                                                              |
| `SetMaterialOffer`            | Supply the quantity currently authorized to start. After an uncertain write, read the controller and recompute the offer because Mazak may have consumed it.                                                                      |
| `WithdrawMaterialOffer`       | If a withdrawal reply was lost and the intended offer is still zero, request an explicit zero edit even when readback shows zero. Save it, await acknowledgment, and reconcile settled readback before releasing the reservation. |
| `RecordedLoads`               | Count identified and unidentified physical loads, including invalidated attempts. An adopted route with an unknown creation boundary returns `null`.                                                                              |

Queue and basket backends use `MazakConfig.ResolveLoadUnloadTransaction` to identify transferred
pieces. The pending load stays anonymous until that identity is known. A transfer with only a
physical count records that quantity and leaves the affected serials unknown. Use
`CanQuarantineMissingMaterial` to retain pallet evidence while an external transfer is outstanding.

Settle transfers and withdraw offers before archiving the job. Archival authorizes cleanup of the
idle schedule and its unused definitions, including after a partial download or a restart. The
adapter retains the route's identity for manufacturing history after the schedule ID is reused.

## Sharing a repository between controllers

Give each controller a stable `ControllerSource` and assign its jobs with `BulkJobFilter`. Keep the
original controller on `DefaultControllerSource` so it resumes from its existing CSV filenames. Set
`MachineGroupName` when controllers use different machining station groups.
