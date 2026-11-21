# Job quantity ownership

By default, Mazak's schedule quantities drive current job counts, raw supply reconciliation,
decrement, and completed-schedule cleanup. Existing installations keep this behavior.

A custom backend can select jobs through `MazakConfig.UseInsightJobQuantities`, a pure predicate
derived from job routes and backend configuration. Keep the selection stable for each job's
lifetime. One cell can contain jobs using either policy.

Selected jobs use repository production accounting: accepted cycles minus recorded cancellations,
durable first entry, and recorded completions. Controller plan increases do not increase demand.
Current status keeps unarchived selected jobs even when their schedules disappear. Explicitly
archived selected jobs stay out of current status and new downloads even if controller rows or the
copied-to-system flag lag behind archival. The backend can project its additional active
reservations through `AdjustCurrentStatus`.

For jobs that will run rework, also select `MazakConfig.UseRepositoryOnlyProductionCounts`. Those
jobs use valid FMS unloads for completions from the start. A repeated process consumes native
capacity each time. An invalidated unload contributes no good completion. Other selected jobs retain
the native execution counts as a lower bound on recorded starts and completions. During rollout,
keep previously accepted jobs on their existing policy and opt in newly accepted jobs. The backend
can use stable job IDs to implement the cutover without an allocator flag.

For local cancellation, `RecordJobDecrementOperation` commits positive once-only cancellations,
release of never-entered raw allocations, and a completed request receipt together. Replays return
the original batch, including empty results. `LoadJobDecrementOperation` only reads that receipt.
The backend still owns eligibility, commitment calculation, admission serialization and publication.
Ordinary `AddNewDecrement` callers retain their existing multiple-entry behavior.

## Controlling a live schedule

The ordinary downloader installs each selected route with zero material offered and retains it while
the job is active. A combined route has one native schedule. Split routes have a schedule per FMS
process. Generic queue reconciliation leaves supply admission to the backend.

Serialize each edit with the assigned controller's synchronization loop and use its fresh readback.
`VerifyInstalledRoute` resolves the job and FMS process to one matching native part and schedule. A
missing or ambiguous route requires operator attention. The adapter uses the installed route for its
lifetime.

- `EnsurePlan` raises the schedule to an absolute minimum. After a lost reply, reread and retry the
  same minimum.
- `SetMaterialOffer` sets the entry process quantity currently authorized to start. After a lost
  reply, recompute it from fresh readback because Mazak may have consumed the offer.
- `WithdrawMaterialOffer` issues an explicit zero edit even when readback already shows zero. Await
  the acknowledgment and settle the transfer before releasing a reserved piece.

Provide exact material identities through `ResolveLoadUnloadTransaction`. Pending loads remain
anonymous until their identity is known. `RecordedLoads` counts physical loads recorded for a
controller, job and process, including invalidated attempts, and reports unidentified pieces
separately. A job already running when tracking begins can have native attempts that predate this
history.

Archive the job after processing its final transfers and releasing its offers and equipment
reservations. `MazakSync.ApplyJobActions` retires an archived schedule after a settled, idle
readback, even when the native planned and completed counts differ. The ordinary downloader reclaims
unused Insight-owned definitions on subsequent downloads. The backend can also call
`ApplyJobActions` for initial downloads, preview them with `WriteJobs.FindPendingDownload`, and
supply selected job IDs outside the normal download window.

The generic decrement command refuses an installation containing selected, unarchived, non-manual
jobs before making any changes. Its replacement belongs to the backend.

Controller-owned backout requires successful synchronization, settled Mazak events, and confirmed
downloads for all unarchived, non-manual jobs without a previous decrement. Unconfirmed downloads
cause refusal before any controller edits, including when only pending jobs exist or their route
dates fall outside the usual download window. Backout does not cancel pending jobs or run downloads
itself. A failed write can have partial controller effects; the ordinary synchronization loop must
succeed before retrying. This is a conservative refusal policy, not an atomic controller rollback.

## Sharing a repository between controllers

Give each controller a stable `ControllerSource` and assign its jobs with `BulkJobFilter`. Keep the
original controller on `DefaultControllerSource` so it resumes from its existing CSV filenames. Set
`MachineGroupName` when controllers use different machining station groups.
