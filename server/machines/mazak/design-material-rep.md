# How Mazak represents material

## Parts, fixtures and pallets

A **Mazak part** defines one or more processes. Each process specifies its program, eligible
machines, fixture and `FixQuantity`, the number of pieces handled together. A **fixture** holds that
whole set. Mazak counts pieces but moves the set as a unit. Insight can track each piece by material
ID and serial.

A **Mazak pallet record** associates a fixture with a pallet number. A fixture can be eligible on
several pallets, and a pallet can have several eligible fixtures. By default, a pallet carries one
fixture at a time. A **fixture group** allows fixtures in the same group to occupy a pallet
together. The pallet records describe permitted configurations. The controller's current pallet
assignments describe what is mounted now.

## What a schedule counts

A schedule selects one native part and gives it a due date and a planned quantity. Its quantities
are in **pieces**, including when a fixture holds three or more pieces.

| Field                                | Meaning                                                                                                                                                                                   |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `PlanQuantity`                       | Execution ceiling for the schedule. Operators can raise it and continue using the same schedule after it reaches the previous plan.                                                       |
| `CompleteQuantity`                   | Pieces Mazak counts as having completed the schedule's last native process.                                                                                                               |
| `ProcessMaterialQuantity` (Material) | Pieces ready to be loaded for that native process.                                                                                                                                        |
| `ProcessExecuteQuantity`             | Pieces assigned to pallets for that process.                                                                                                                                              |
| `ProcessBadQuantity` (Bad)           | An operator-entered count for tracking. Insight records quality and invalidation in its manufacturing history. Some stock queue and decrement calculations also read this native counter. |

The Mazak UI and Open Database Kit enforce the same deletion rules. Deleting a schedule requires
zero ready material at **every** process and zero executing material, with all pallet assignments
for that schedule cleared. Mazak maintains agreement between executing quantities and pallet
assignments. An idle schedule can be deleted with planned and completed quantities unequal. Deleting
a part requires removing every schedule that references it. A backend can keep an idle schedule
installed for as long as its job might use it.

## FMS Insight schedules

In a **combined schedule**, the Mazak part contains the job's processes. Unloading one process makes
its pieces ready in the Material field of the next process. This suits work whose next process can
begin immediately.

In **split schedules**, each FMS process gets its own native part and schedule at download. For
example, FMS process 2 runs as **Mazak process 1** on its own schedule. Each row starts with zero
ready material. An integration can follow the pieces through a queue or basket and set Material when
they become available to the owning cell. For a live schedule, the translator matches the CSV's full
native part name to the schedule and reads its comment to identify the job and FMS process.
[Schedule construction](sync/BuildMazakSchedules.cs) · [Log translation](sync/LogTranslation.cs)

## What one load station visit reports

Mazak loads and unloads the **entire set for one process on one fixture**. Suppose a pallet carries
fixture A holding three pieces and fixture B holding two. Unloading both produces two unload-end
records, one with quantity three and one with quantity two. If A holds A1, A2 and A3, all three
leave in A's transfer. A controller simulation for this fixture must report the three-piece unload
even when Insight has lost track of one serial. A visit can also unload one set and load another.

Each end CSV supplies the full native part name, native process, `FixedQuantity`, pallet and
station. The installed part-process definition supplies the fixture. Insight associates the serials
using its queue and pallet history or an integration's exact load/unload resolver. A missing receipt
can leave the identities uncertain even when Mazak reported the physical transfer.

The reader groups related end records for a pallet visit so elapsed time can be distributed across
the operations. It leaves a trailing group pending while more CSVs may still arrive. Each record in
the group describes one whole-set transfer and corresponds to one Insight load or unload event. The
filename identifies the event within the controller that emitted it. [CSV reader](db/LogCSV.cs) ·
[Visit translation](sync/LogTranslation.cs)

## Schedule quantity and rework

A split process-2 schedule with `FixQuantity = 3` reaches six planned and six complete after two
sets. If one set's process-2 result is invalidated, the backend can raise the plan to nine when it
sees the set waiting for rework. When that set is physically available, it can offer three pieces on
the same schedule. After the rerun Mazak has counted nine pieces through process 2. The job still
calls for six distinct pieces and has six valid process-2 completions.

Raising the plan supplies capacity. Changing Material admits work. Canceling a preparation can leave
unused capacity on the row. Which material to admit and when to retire the row are decisions for the
synchronization loop that controls it. [Stock Mazak synchronization policy](quantity-ownership.md)
