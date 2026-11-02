---
title: Historical data and current status
nav: FMS Insight Server > Historical data
description: Historical planning, recorded actions, and replaceable live status in the Insight API.
---

Insight supplies historical log events and downloaded job/simulation plans separately from current
status. The website consumes all three; a historical analysis client can consume events and plans
without reconstructing backend state from status snapshots.

Job comments, whole-job and path holds, archive state and controller delivery state are live fields.
For compatibility they also appear on historical job transport types. Their presence does not
establish what those values were during production. Keep original API responses as retrieval
provenance, and distinguish these fields from the historical facts maintained by an analysis.
Current status is the latest backend snapshot; differences between snapshots do not establish
historical assignments, decrements or production.

## Casting assignments

`CastingAssigned` records an assignment to a job. `CastingUnassigned` records the release of a raw
assignment, including release during cancellation. Neither establishes loading or production. There
is one event per affected material. `Material` identifies the material with process/face zero,
`Result` names the assigned/released job, and durations are zero.

Both event types carry the following `details` entries:

- `Assignment`: a JSON-encoded `MaterialDetails` value preserving the job, part, process count and
  any recorded paths of the assignment being made or released, with material identity context.
- `Casting`: the source casting on assignment, or the resulting casting on release.

An assignment may not yet have a recorded path; the Niigata loading path is recorded by its load
operation. A missing path in the assignment payload is not evidence for a particular route. The
ordinary `material` fields on old log reads join current material details and paths. Later
reassignment can change those fields; use `details.Assignment` for the historical assignment action.
Events commit with their assignment changes and are published afterward. An unchanged or failed
assignment, insufficient-stock attempt, repeated release or replayed cancellation creates no extra
action. Existing serial marking, workorder assignment and invalidation events retain their meanings.

## Quantity decrements and backouts

`JobDecrement` records a committed quantity decrement. It has no manufacturing materials or
duration. `Result` is the decrement ID; `details.Decrements` is a JSON-encoded array of
`JobAndDecrementQuantity` values, including job, part, quantity, decrement ID and action time.
Initial historical job reads also contain decrements. Reconcile records by
`(JobUnique, DecrementId)` so overlap between a baseline read and event catch-up does not count the
action twice.

A plain decrement call records a new action; it is not an idempotent retry API. Cancellation has a
durable operation identity, and its retries return the prior receipt without another decrement or
casting-release event. Archive state, a casting release or a change in remaining quantity is not
itself decrement evidence. The existing `DELETE jobs/planned-cycles` operation changes production;
it must not be used as a history read.

New action events are recorded from this release forward. Older databases retain decrement records
in job reads, but cannot provide casting assignment actions that were never recorded. No historical
actions are fabricated during upgrade. These events remain in counter-based catch-up but do not
advance the controller-import time returned by `MaxLogDate`.

## Planning downloads and catch-up

A schedule ID identifies a complete planning download. Accepted jobs/routes and simulation are
immutable; revised planning uses a fresh schedule ID. Live job-field updates and explicit decrements
retain their existing behavior. Station-use rows must name their enclosing download's schedule ID. A
repeated download is rejected rather than modifying an already-known schedule.

Catch up events after their counter, and retrieve new planning by sending the schedule IDs already
held to job-history/recent reads. These are separate progress mechanisms. Historical schedule
selection includes overlapping planned job routes or simulated station intervals, including
schedules with no jobs. A selected schedule returns all its jobs and simulated station intervals,
including intervals outside the requested window. Known-ID filtering applies to simulation-only
schedules too.

Time-window coverage does not establish a complete material lifecycle. Corrections outside a window
can affect previously downloaded events inside it; follow the explicit correction and obtain
authoritative records as needed. Separate requests need not observe one atomic server snapshot.
