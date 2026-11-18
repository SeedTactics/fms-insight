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

## Selected split routes

A custom backend can enable `UseDurableRouteIdentity` alongside `UseInsightJobQuantities` and
calculate native writes with `MazakRouteCapacity.Calculate`. Pass an accepted job's process and
path, the planned native quantity, and the material offer justified by the caller. A new schedule
starts with zero offered material. The calculation reserves the exact native part name in the
manufacturing repository, then returns the next transaction. Apply it through the serialized
controller owner and calculate again from fresh readback. Use the same request across a lost
response. A plan increase uses `ScheduleSafeEdit` with priority, due date and hold copied from
readback and no schedule process rows. After confirming the plan, the material offer uses the
ordinary material edit. The caller still owns when a route can accept work and when existing
capacity must be retired. Use `BulkJobFilter` to select the ordinary jobs owned by this controller
when the repository also contains custom-managed or peer jobs.

Historical attribution uses the controller source, exact full part name and native process. For
legacy definitions, the caller can supply a full name for adoption after establishing its ownership.
A retired name retains its job route in the repository. The published quantities for Insight-owned
jobs use durable entries and valid unloads. The backend can project its own reservations with
`AdjustCurrentStatus`.

Use the repository-aware overload of `MazakRouteRetirement.Capture` before deleting parts. It
includes the fixture and program reserved before a preparation write, even if the part was never
installed. Retain the selected names and dependency candidates across retries.
`MazakRouteRetirement.Calculate` returns the next transaction from a complete controller
observation. Surviving schedules and parts protect shared dependencies through schedule, part,
pallet mapping and dependency cleanup stages.

Assign each additional controller a distinct, stable `ControllerSource` independent of its endpoint.
Keep the original controller on `DefaultControllerSource` to retain its existing LG history. Use
distinct Insight pallet and station numbering across controllers sharing a repository. Controller
reads and acknowledgments take raw filenames. Repository correlation uses `MazakConfig.LogForeignId`
on those filenames.
