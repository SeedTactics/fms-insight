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

Booking identifiers are opaque, ephemeral scheduling metadata. They are stored with the job for
external schedulers so the associations participate in the same ACID transaction. Insight persists
and returns them without interpreting them or using them for production decisions. They belong to
current status, including when carried by historical job responses. Backout can remove associations;
those removals are outside historical replay and do not require an event. Original response JSON may
retain bookings as retrieval provenance; exclude them from maintained analytical history.

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
action. Repeating an explicit assignment with the same job, part and process count changes nothing;
contradictory assignment details are rejected. Existing serial marking, workorder assignment and
invalidation events retain their meanings.

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
immutable; revised planning uses a fresh schedule ID. Live job-field updates, booking removals and
explicit decrements retain their existing behavior. Station-use rows must name their enclosing
download's schedule ID. A repeated download is rejected rather than modifying an already-known
schedule.

Catch up events after their counter, and retrieve new planning by sending the schedule IDs already
held to job-history/recent reads. These are separate progress mechanisms. Historical schedule
selection includes overlapping planned job routes or simulated station intervals, including
schedules with no jobs. A selected schedule returns all its jobs and simulated station intervals,
including intervals outside the requested window. Known-ID filtering applies to simulation-only
schedules too.

The `newJobs` WebSocket notification contains accepted jobs and workorder overrides with their
resolved program references. The internal accepted result also includes resolved program-content
entries; the socket omits `Programs` and `DebugMessage`. Resolution and persistence use the same
transaction, and publication follows commit. Older servers published the original request, which
could still contain null, zero or negative revision references. Historical catch-up still uses
history/recent reads to recover downloads whose notifications were missed. Durable clients record a
schedule as known only after storing its complete accepted jobs and simulated station intervals.

Event publication follows commit but can arrive out of counter order across concurrent writers. Use
live notifications to trigger catch-up, and advance a durable cursor only through ordered
`events/recent` results. Commit imported records and their cursor together; the largest counter seen
on the socket does not establish that earlier counters were received.

History reads provide historical jobs and simulated station intervals for selected schedules. Recent
reads additionally provide the latest daily-usage simulation. These responses do not provide a
complete history of every planning artifact, such as workorder simulations or extra-part snapshots.

Time-window coverage does not establish a complete material lifecycle. Corrections outside a window
can affect previously downloaded events inside it; follow the explicit correction and obtain
authoritative records as needed. Separate requests need not observe one atomic server snapshot.

## Repository API compatibility

`IRepository.AddJobs` returns the accepted `NewJobs` after committing the download. Source callers
that do not need the result can ignore it; F# callers use `|> ignore` where a unit result is
required. Plugins compiled against the previous void signature must be rebuilt when upgrading the
framework. Installations pinned to the older package keep their existing API.
`IJobAndQueueControl.AddJobs` and the HTTP API retain their existing signatures.
