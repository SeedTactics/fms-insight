# Who controls job quantities on a Mazak cell?

By default, FMS Insight downloads a job as Mazak schedules. Mazak tracks pieces through the
processes, and the stock `MazakSync` loop uses the controller's counts to reconcile queues, back
out work, and clean up completed schedules.

A custom backend can use a queue scan or a basket load to decide which pieces may run. It selects
the jobs it controls with `MazakConfig.UseInsightJobQuantities`, a predicate based on the route
and backend configuration. Keep that choice stable for the life of the job. Both policies can
coexist on one cell. Backend-owned jobs start with zero ready material, and stock queue
reconciliation leaves their supply alone. The backend admits pieces, cancels demand, retires
schedules, and archives jobs.

## What the counts mean

| Quantity | What it tells the backend |
| --- | --- |
| Job demand | How many pieces the job calls for. For a backend-owned job, this is the accepted quantity less recorded cancellations. |
| Mazak `PlanQuantity` | How many pieces may run through this schedule, including reruns. Raising it creates capacity. |
| Mazak `ProcessMaterialQuantity` | How many pieces are ready to load at this native process. Setting it admits work. |
| Valid completions | How many distinct pieces have a valid unload for the job, process, and path. |

Suppose six pieces finish process 2 in two sets of three. An operator invalidates the second set
and returns it to the queue. The job still calls for six pieces. On a split process-2 schedule,
the backend can raise the plan from six to nine and offer three pieces when the set is ready.
FMS process 2 is native process 1 on that schedule. [How Mazak represents material](design-material-rep.md)

| Process 2 | Mazak `CompleteQuantity` | Valid pieces |
| --- | ---: | ---: |
| Both sets finish | 6 | 6 |
| The second set is invalidated | 6 | 3 |
| The same three pieces finish again | 9 | 6 |

The generic `ActiveJob.Completed` count can include invalidated unloads. While a schedule
exists, the status builder also keeps the higher of the repository and native counts. For a
valid-piece count, read the job's material history with
`IRepository.GetLogForMaterial(materialIDs, includeInvalidatedCycles: false)`. Count distinct
IDs belonging to this job on completed pallet unloads for each process and path. Use that count
for the operator's status (through `AdjustCurrentStatus`) and when deciding whether to archive.

Entry into production answers a different question. If a machining event identifies a piece whose
load event was missed, Insight still knows that piece entered the cell. Asking for another blank
would overstate the remaining demand. The backend can also account for pieces it has reserved
when it projects status. [Mazak job status](sync/BuildCurrentStatus.cs)

## Installing and running a job

### Installation

`WriteJobs.InstallJobs` adds the whole jobs selected for one controller and preserves other
work. It marks a job copied once all its expected routes are visible there. After an interrupted
download, retry from fresh controller data. CSV
translation identifies the job and route from the schedule comment, while status uses the part
comment. If the two disagree, the installer identifies the schedule to correct. The stock
`WriteJobs.SyncFromDatabase` also performs automatic cleanup and repository-wide archival, so
it belongs in the default single-cell loop. [Job installation](sync/WriteJobs.cs)

### Changing the plan and offering material

Serialize the read, decision, and write per physical controller. With direct database access,
share one `OpenDatabaseKitDB` instance for that controller. Its lock covers one `Save` through
acknowledgment of the last chunk. The caller coordinates the preceding read and decision with
other writes. Check the schedule ID and full native part name, plus the route comment when
selecting by job.

- **Raise the plan.** `BuildMazakSchedules.IncreasePlanQuantity` takes an absolute minimum in
  pieces. If the reply is lost, reread the row. A request for nine can be repeated safely once
  the row identity is confirmed. If the plan already meets nine, the helper produces no write.
- **Offer or withdraw material.** `BuildMazakSchedules.SetMaterialQuantity` sets the ready count
  for a native process. If a reply is lost and the count is now zero, Mazak may already have
  loaded the pieces. Check for consumption before offering them again. A matching positive count
  produces no write. An explicit zero requests acknowledgment of withdrawal. On a combined
  schedule, the command also writes the observed ready counts of the other processes, so the
  caller must account for supply across the whole schedule. [Schedule writes](sync/BuildMazakSchedules.cs)

### Retirement

Mazak can keep a schedule installed after `CompleteQuantity` reaches `PlanQuantity`. Once the
final transfer is recorded, reservations are settled, and the row has no
ready or executing material or pallet assignments, the backend can select it with
`BuildMazakSchedules.RemoveSchedules`. Reread the controller after a refusal or uncertain reply.
The helper reports which parts surviving schedules still use. Reclaim unreferenced definitions
separately, in dependency order. Stock cleanup protects a backend-owned job's part definitions
until archival. An unarchived job on this policy stays visible after its schedules disappear.
Archiving it removes it from current status and stock download selection, even if a native row
remains or the copied flag is stale.

## Backing out work

Quantity ownership is per job. The stock backout command acts on the installation. If any
unarchived, non-manual job uses backend-owned quantities, the command refuses before changing
anything. The custom backend then owns the backout decision for both kinds of jobs.

For a local backout, the backend decides which pieces are still eligible and serializes that
decision with admission. `RecordJobDecrementOperation` records a positive cancellation once per job, releases the
specified raw allocations that never entered production, and saves a receipt in one transaction.
A retry with the same operation ID returns the original result, including an empty batch.
`LoadJobDecrementOperation` reads the receipt. The backend publishes the result and can leave
the Mazak schedule in place.

For controller-owned backout, the stock loop requires successful synchronization, settled Mazak
events, and confirmed downloads for all unarchived, non-manual jobs without a prior decrement.
That includes pending jobs outside the current download window. A failed write can leave some
controller changes in place. Let the normal loop synchronize successfully before retrying.
[Stock backout](sync/DecrementPlanQty.cs)

## Sharing a log repository across cells

Two controllers can produce the same CSV filename. Give each additional controller a stable,
unique `MazakConfig.CsvLogSource`. The original controller can keep its source unset so its
existing history remains addressable. Capture the watermark, acquire logs, and acknowledge them
using each controller's raw filenames. For a repository receipt lookup starting from a raw
filename, use `CsvLogForeignID` to obtain the qualified key. Pass the controller's machining
group explicitly to the staged `ReconcileCellState` call. [Log synchronization](sync/MazakSync.cs)
