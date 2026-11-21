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

## Custom controller loop

Use `WriteJobs.InstallJobs` to deliver the accepted jobs selected for one controller. It resumes
interrupted installs using native readback, adds the supplied routes, and marks a job copied when
all its expected schedules appear. CSV translation reads the native schedule's comment and current
status reads the native part's comment. If either identifies a route in the batch, both must agree
on that route and identify the job's part. The installer reports the conflicting schedule ID for
correction on the controller. Automatic deletion and repository-wide archival remain in
`SyncFromDatabase`.

Run downloads, plan edits, supply edits, and retirement through one serialized owner per physical
controller. With direct database access, share one `OpenDatabaseKitDB` instance for that controller.
Its write lock covers one complete `Save`, including acknowledgment of all its chunks. The caller
serializes the observation and decision leading to that save. Recheck the schedule ID and full part
name, and check the route comment when selecting by job. `BuildMazakSchedules.IncreasePlanQuantity`
builds a `ScheduleSafeEdit` for an absolute minimum. After an uncertain reply, reread the row and
apply the minimum again if needed. `BuildMazakSchedules.SetMaterialQuantity` sets ready pieces for a
native process, including an explicit zero when withdrawal needs acknowledgment. A split schedule
has one native process. An already satisfied positive quantity yields an empty write. For a combined
schedule the command also sends the observed ready counts of its other processes. Use it when the
caller can account for supply on the whole schedule. Check whether Mazak consumed a material offer
before retrying it.

Use `BuildMazakSchedules.RemoveSchedules` to calculate retirement writes and find the parts still
needed by surviving schedules. Select an idle row once its final transfer receipt, material and
reservations have settled. Handle a native refusal or uncertain reply by rereading the controller.
Reclaim definitions separately, using fresh references and removing rows in dependency order.

For a shared repository, set a stable, unique `MazakConfig.CsvLogSource` for each additional
controller. The original controller can keep its existing raw IDs. `CaptureLogWatermark`,
`AcquireCellState` and `AcknowledgeLogs` use raw filenames for the corresponding controller.
Translation uses the qualified repository key, which callers can obtain with `CsvLogForeignID` for
receipt lookups. The staged `ReconcileCellState` overload accepts the controller's machining group
explicitly.

Controller-owned backout requires successful synchronization, settled Mazak events, and confirmed
downloads for all unarchived, non-manual jobs without a previous decrement. Unconfirmed downloads
cause refusal before any controller edits, including when only pending jobs exist or their route
dates fall outside the usual download window. Backout does not cancel pending jobs or run downloads
itself. A failed write can have partial controller effects; the ordinary synchronization loop must
succeed before retrying. This is a conservative refusal policy, not an atomic controller rollback.
