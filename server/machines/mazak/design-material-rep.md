# How Mazak represents material

- A _Mazak part_ defines multiple processes, and for each process contains the program, machine
  assignments, fix quantity, and a fixture for each process.
- A _Mazak fixture_ holds the _fix quantity_ of parts as a single set, and mazak never splits them
  up (or even tracks them separately); Mazak does not track individual material but a set of fix
  quantity.
- A _Mazak pallet_ is slightly misnamed, since it is a many-to-many relationship between fixtures
  and pallets. A Mazak pallet record contains both a fixture and a pallet number, indicating that
  the fixture can go on the pallet. By default, only one fixture can be present on a pallet at once.
- An optional _Fixture Group_ field tells mazak that multiple fixtures in the same group may
  simultaneously be on the pallet.
- A _Mazak Schedule_ identifies a single part, with a due date, planned quantity, and a few other
  fields. It also tracks, for each process, a quantity of material waiting and ready to begin the
  process, a quantity of material currently on pallets, and a quantity of "bad" which is not used by
  Mazak itself but only there for humans to help track things. FMS Insight does not use the "bad"
  quantity, instead tracking that internally in its own databases.
- The Mazak UI and Open Database Kit API provide protection and consistency; Mazak disallows
  deleting a part if there is a schedule for it, disallows deleting a schedule if there is any
  assigned material or in-process material, maintains consistency between the count of material in
  the schedule and the material on the pallets, and so forth. FMS Insight does not need to protect
  against combinations that Mazak prevents, such as deleting a part or schedule while material is
  still on pallets.

## FMS Insight Schedules

FMS Insight supports two download layouts. In a **combined schedule**, the Mazak part contains the
job’s processes. In **split schedules**, each FMS process gets its own native part and schedule. For
example, the schedule for FMS process 2 runs **Mazak process 1**. The adapter resolves the FMS job
and process using the full native part name and the route comment. A display part name or a native
process number alone cannot identify the route.

The choice between split schedules vs combined schedules is how the material is tracked between
processes. In a combined schedule, when a piece of material is unloaded, Mazak automatically
increments the "Material" field on the next process, signaling that the material is ready to be
loaded immediately. If instead the material is not ready immediately (such as baskets or other
reasons), we use split schedules, track the material in FMS Insight via baskets/queues, and FMS
Insight is in charge of incrementing the material on the schedule once the material is ready to be
loaded.

## What one load station visit reports

Mazak loads and unloads the **entire set of pieces for a process on a fixture**. The process defines
the size of that set in `FixQuantity`. FMS Insight tracks the individual pieces in the set by
material ID and, when available, serial number.

Suppose a pallet carries fixture A with three pieces and fixture B with two. If both sets unload at
the same visit, Mazak emits one unload record for the process on fixture A with quantity three, and
another for the process on fixture B with quantity two. If A contains serials A1, A2, and A3, its
unload moves all three together. An unload of A2 followed by a separate unload of A1 and A3 from
that same set is outside the controller’s model.

The CSV reader gets the full native part name, native process, reported `FixedQuantity`, pallet, and
station from each record. Individual serials and the fixture name are absent from the CSV row. The
installed part-process definition supplies the fixture relationship. The translator groups the
ordered load/unload end records for a pallet visit and leaves a trailing group pending while Mazak
may still be writing it. One visit can therefore contain several records, each describing a whole
set. The reason for the grouping is entirely to distribute elapsed time among the individual
operations; each load or unload Mazak CSV event records the load or unload of a single set of
`FixQuantity` parts for a single process on a single fixture, which matches the FMS Insight events
1-1.

Insight associates material IDs with each transfer using its queue and pallet history or an
integration’s exact load/unload resolver. If a robot receipt is missing, the serials that made up a
transferred set may be unknown. The physical transfer still took place at the configured set size.

## Schedule quantity and rework

`FixQuantity` sets the transfer size. `PlanQuantity` sets the schedule’s execution ceiling in
pieces. The entry process’s `ProcessMaterialQuantity` tells Mazak how much material is available to
start. A schedule can remain installed with planned equal to complete and run again after its plan
increases.

For example, a process-2 schedule with `FixQuantity = 3` can reach six planned and six complete
after two sets. If an operator invalidates one set’s process-2 cycle and stages that complete set
for rework, the backend can raise the existing schedule’s plan to nine and offer three pieces. After
the rerun, Mazak has counted nine pieces through that schedule. The job still calls for six pieces.
Three of the nine executions were invalidated, leaving six valid process-2 completions.

By default, Mazak controls what runs and Insight’s download cleanup can retire completed schedules.
For jobs selected for Insight-owned quantities, the backend controls admission and retirement. A
queue or basket decision can then determine when the next complete set is offered.
