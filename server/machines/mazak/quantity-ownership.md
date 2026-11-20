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

On the ordinary download path, controller completion evidence remains visible while its event batch
is settling. For Insight-owned jobs using durable route identity, only valid repository unloads
contribute to completion. Actual machining also establishes durable entry when its preceding LOAD
was unavailable.

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

A custom backend enables `UseDurableRouteIdentity` and selects its jobs with
`UseInsightJobQuantities`. Pass an accepted job, process, path and planned native quantity to
`MazakRouteCapacity.Calculate`. The backend can select an individual process for any such job with
one path per process, including parts that return through a queue. The ordinary bulk download skips
these jobs by default. Set `BulkJobFilter` to select the jobs for this controller's bulk writer when
several controllers share a repository.

The calculation reserves the exact native part name in the manufacturing repository and returns the
next transaction. Apply it through the serialized controller owner and calculate again from fresh
readback, including after a lost reply. `MazakRouteCapacity.Preview` runs the same checks and plans
the next write without saving reservations. Use `Calculate` before applying a write.

A new schedule starts with zero offered material. An existing schedule keeps its current offer. An
increase to the plan uses `ScheduleSafeEdit` with the observed priority, due date and hold.

When material is ready, pass the observed schedule row and the available piece count to
`MazakRouteCapacity.SetMaterialQuantity`. The caller owns admission, including which pieces make up
a set. After an uncertain material edit, read the controller and the staging evidence again before
calculating the next offer. Replaying the old quantity after Mazak consumed it could offer the same
pieces twice.

The preparation API manages one live schedule per selected route. For example, after a piece is
invalidated and returned to a queue, use `MazakRouteRetirement.RemoveSchedule` to remove the
verified idle schedule and keep its definition. A lower plan also requires removing the schedule and
creating its replacement. The exact load resolver supplies the existing material ID. The repeated
unload restores its valid completion and the piece counts as one entry.

Capture the repository's `MaxLogCounter` before creating a schedule. `RecordedLoads` counts physical
load attempts for the controller and exact route after that boundary, including attempts later
invalidated and loads whose material IDs could not be established. It reports identified and
unidentified quantities separately so the backend can account for physical attempts while deciding
whether it has enough serial evidence to admit more work.

Historical attribution uses the controller source, exact full part name and native process. For
legacy definitions, the caller can supply a full name for adoption after establishing its ownership.
Mazak requires all material for the schedule to clear its pallets and the rest of the system, and
every schedule process to have zero Material ready to load, before its UI or API allows deletion. A
retired name retains its job route for unload logs that arrive after deletion. Pallet reconciliation
uses native pallet assignments after settling the available logs. An empty assignment can become
visible before its unload CSV. A handling owner awaiting that transfer should return false from
`CanQuarantineMissingMaterial` for the pallet until it settles the outcome. When an operator removes
material and clears its assignment in Mazak, Insight quarantines the missing serials if configured
and the owner allows reconciliation. Insight-owned jobs retain their repository quantities and valid
unloads while material is outside Mazak, including when it is in baskets. The backend can project
its own reservations with `AdjustCurrentStatus`.

For reserved routes, native pallet assignments supply the current quantity. Insight history supplies
serial candidates, and unresolved attribution is reported separately. An unload request or a CSV
batch still settling can explain a discrepancy without adding material to the pallet. A custom
handling owner can keep its outstanding-transfer guard active until its CSV arrives. Once logs
settle, an empty observation outside the load station ends the placement even when there is no
quarantine queue. At the load station, the observation also needs to confirm that every schedule
associated with the prior placement has retired and the station has no request. The transfer guard
applies in either location. Earlier loads remain in manufacturing history. A delayed machining end
dated before the empty observation can complete its original run using that run's start and serials.
Its tool usage is unknown once the pallet placement has changed. An end dated after the observation
requires evidence of a subsequent start, even when the new load CSV is missing.

Use `MazakRouteRetirement.Capture` before deleting parts. It includes the fixture and program
reserved before a preparation write, even if the part was never installed. Retain the
controller-bound candidates across retries. Use readback from that controller.
`MazakRouteRetirement.Calculate` verifies the most recent rows against their reserved routes. Apply
its next write, continue waiting for complete readback, or close the intent when it reports
completion. It refuses a selected schedule deletion while a process has material ready or executing,
or a pallet still reports a positive assignment. An empty schedule may retire with unequal planned
and completed counts. Surviving schedules, parts and active preparations protect their shared
fixtures and programs, including programs already installed on the controller. The final readback
releases the preparation so a later installation can choose an available fixture group. The
historical part identity remains reserved.

Scoped bulk downloads save cleanup candidates before deleting parts. After an uncertain write, the
next pass uses fresh readback to recheck ownership and references and finish eligible cleanup.
Cleanup can resume independently after the incoming job leaves the download window. Candidates
blocked only by an active station request remain pending for the next pass.

Assign each additional controller a distinct, stable `ControllerSource` independent of its endpoint.
Keep the original controller on `DefaultControllerSource` to retain its existing LG history. Use
distinct Insight pallet and station numbering across controllers sharing a repository. Controller
reads and acknowledgments take raw filenames. Repository correlation uses `MazakConfig.LogForeignId`
on those filenames.

Set `MazakConfig.MachineGroupName` to the machining station group in that controller's job routes
when cells use different groups. This keeps new machining events, pallet locations, and tool queries
on the controller's group as other cells publish jobs. Leaving it unset uses the latest accepted
schedule as before.
