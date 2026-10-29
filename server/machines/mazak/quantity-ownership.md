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

The backend owns supply admission, cancellation, schedule retirement, and job archival for selected
jobs. Downloads start them with zero material supply. Generic queue reconciliation does not assign
castings or change supply for them. Generic cleanup retains their schedules, even when completed,
and retains their part definitions until job archival. The generic decrement command refuses an
installation containing selected, unarchived, non-manual jobs before making any changes. Its
replacement belongs to the custom backend, including decisions about jobs on the default policy.

Use `BuildMazakSchedules.RemoveSchedules` to calculate retirement writes and the part names needed
by surviving schedules. The caller must protect material, reservations, outstanding equipment work,
and every required process route. Run retirement through the normal serialized controller owner,
independently of new downloads, and require the ordinary post-write refresh. This helper neither
archives jobs nor records cancellation quantities. Part definitions for a selected job remain
protected during downloads until its backend explicitly archives it.

The existing `ScheduleSafeEdit` command can carry an increased `PlanQuantity`. Capacity increases
and rework admission are backend decisions requiring equipment qualification; this policy does not
implement an invalidation/rework workflow. It introduces no new quantity-decrease path. The
established controller-owned decrement path remains available to default installations.

Controller-owned backout requires successful synchronization, settled Mazak events, and confirmed
downloads for all unarchived, non-manual jobs without a previous decrement. Unconfirmed downloads
cause refusal before any controller edits, including when only pending jobs exist or their route
dates fall outside the usual download window. Backout does not cancel pending jobs or run downloads
itself. A failed write can have partial controller effects; the ordinary synchronization loop must
succeed before retrying. This is a conservative refusal policy, not an atomic controller rollback.
