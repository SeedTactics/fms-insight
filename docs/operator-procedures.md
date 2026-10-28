---
title: Load/Unload Procedures
nav: Procedures > Operator Load/Unload
description: >-
  Use FMS Insight to follow load and unload instructions, introduce material, and keep the material
  record aligned with the shop floor when production does not go as planned.
---

FMS Insight's [station pages](/fms-insight/client-station-monitor) show the material and work
relevant to each operator. Bookmark the appropriate station and select its queues. The screens use
the [whiteboard metaphor](/fms-insight/material-tracking): material cards should match the physical
material at the station. A [barcode scanner](/fms-insight/client-scanners) can help identify a
particular piece.

Available actions depend on the cell controller and installation. Use these pages to understand the
general workflow alongside the operating and recovery procedures supplied for your cell.

## Normal Loading and Unloading

![Load station showing material to load, transfer, and unload](screenshots/insight-load-station.png)

The load station page shows the expected loads, unloads, and transfers between pallet faces or
basket slots. Check the part, serial, and destination against the physical material. Open a material
card for its history or instructions.

For a controller-managed pallet, complete the displayed work and use the controller's ready or load
button according to your cell's procedure. Insight updates when the controller reports that the work
is complete. Some installations also have
[basket station work](/fms-insight/client-station-monitor#basket-work) confirmed directly in
Insight.

Queue order helps choose which eligible material to load. A pallet's assigned job and route also
constrain the selection, so the first card in a queue is not necessarily the next piece to load.
Follow the specific material shown in the load instruction.

## Introducing Raw Material

Jobs can either assume raw material is available or draw it from a configured input queue. Without
an input queue, Insight creates material records as the initial loads complete. With an input queue,
the operator records the available material before it is selected for loading.

![Raw material queues and jobs awaiting material](screenshots/insight-queues-jobs-table.png)

Bring the material to its designated location, then use the Queues page to scan or enter it.
Depending on the installation, material may be entered individually or in bulk, with or without
serials, and as a casting type or against a particular job. Review the proposed part, job, process,
and queue before adding it. The jobs and workorders shown on this page help determine what material
is needed.

Keep the queue display aligned with the physical queue. Reordering cards changes queue priority;
moving a card to another queue records a change of location. Material involved in an active load,
unload, or automated transfer cannot simply be removed or moved to another queue.

## Quarantine Material

[Quarantine](/fms-insight/material-quarantine) tracks material set aside for inspection, rework, or
a disposition decision. There are two distinct actions:

- **Quarantine** moves material that is waiting in a queue into the configured quarantine queue.
- **Signal For Quarantine** records a request for the integration to handle material when it leaves
  automation control. It does not immediately move the material or cancel a load.

Use the action available for the material's current state and follow the installation's instructions
for the physical move. When released material returns, verify its history and next process before
adding it to the appropriate production queue.

## When the Displayed Work Cannot Be Completed

Resolve a mismatch before acknowledging completion. Where **Cancel Load** is available, its
confirmation lists all material in the instruction that will be cancelled and recalculated. It can
affect more than the card you selected. Review the refreshed instruction before continuing.

Where cancellation is not offered, follow the controller-specific procedure for rejecting or
changing the load. A quarantine signal is not a substitute for cancelling an instruction. If an
alarm suspends changes, read the displayed reason and resolve it before continuing with the affected
work.

## Recovering From Misload

If completion has already been acknowledged, first reconcile the physical cell and controller using
the installation's recovery procedure. Depending on the integration, material removed from the
controller's pallet record may appear in a quarantine queue. Check the resulting Insight status
before making further changes.

Where enabled, **Invalidate Cycle** on the Queues page can correct an incorrectly recorded process.
Look up the serial and review every affected material record in the confirmation. Invalidation
affects whole events, so it may affect other material recorded on those events. All affected
material must be removed from queues, including quarantine queues, and be clear of active station
work and automation control before the correction can proceed. Follow the installation's recovery
procedure for those pieces as well.

Corrections proceed from the latest still-valid process backwards, one process at a time. Review the
refreshed history after each correction. Changing a casting or job assignment first requires
resolving any later processes. After the correction, add the material back to the appropriate queue
and verify its next process.

Invalidated events remain visible in the history but are no longer used to determine the material's
current production state. Removing material from a queue alone does not invalidate its history.

Insight does not transfer recorded pallet manufacturing history from one material record to another.
If machining was attributed to the wrong material record, add an explanatory note and use the
installation's traceability exception procedure. Do not repeat machining solely to repair a record.
Correcting a serial label on the right material record is a separate operation.
