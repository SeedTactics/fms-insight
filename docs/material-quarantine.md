---
id: material-quarantine
title: Material Quarantine
sidebar_label: Material Quarantine
---

FMS Insight provides an optional feature to help track and manage material
that has been temporarily removed from the cell for re-machining, more
detailed inspection, or some other rework. FMS Insight displays all the
material currently on hold, allows a supervisor/manager to update the status
of quarantined material, and finally allows the material to be re-introduced
to the cell or scrapped.

The implementation extends the [material tracking](material-tracking.md) to
also include whiteboard regions for quarantined material. By default, FMS
Insight creates whiteboard regions for each pallet, inspection stand, the
wash stand, and any in-process queues of material between processes. These
are the "active" whiteboard regions and contain material which is flowing
through the normal workflow. FMS Insight keeps these whiteboard regions
updated automatically by recording the log of automated cell activity.
In the [server configuration](server-config.md), FMS Insight allows you to
define a list of Queues and this list of queues must contain a queue for
each "active" in-process queue defined in the flexibility plan and used to
hold material in the normal workflow. Any queue which is listed inside
the flexibility plan is treated by FMS Insight as an "active" queue.

To support quarantined material, you can create extra whiteboard
regions/queues for the various tasks that might be performed on quarantined
material. To the list of queues defined in the [server configuration](server-config.md),
you can add extra queues to represent the various quarantined material tasks.
A queue that is not listed inside the flexibility plan is considered as a
quarantined material queue and FMS Insight will not update the queue
automatically. Instead a supervisor, manager, or engineer can manually move material
between the various quarantined material queues to assist with tracking the
material. The material can also be moved back into an "active" queue to
reintroduce it into the cell or removed completely if scrapped.

The [operations](client-operations.md#material) and [quality](client-quality.md#quarantine-material)
webpages allow supervisors, quality engineers and others to manually move
material between the various quarantined queues, view all the material, add
notes, re-introduce material to "active" queues, or scrap material. In
addition, the [server configuration](server-config.md) contains a setting
_QuarantineQueue_ for the initial quarantine queue. If this setting is given,
the [station monitor webpages](client-station-monitor.md) will contain a
button _Quarantine Material_: if pressed the material is moved out of the
"active" whiteboard regions/queues and into the quarantine queue specified in
the server configuration file.

FMS Insight allows arbitrary quarantined material queues, but we suggest you
follow a scheme similar to the "TODO, In-Progress, Done" Kanban task
management/project management technique.

- _Initial Quarantine_: We suggest a quarantine queue called _Initial Quarantine_.
  When an operator removes material from a pallet/load station/inspection
  stand/etc. the material is initially moved into this queue to get it out of
  the "active" queues and allows the operator to continue with normal
  operations. A supervisor/manager should then decide what is to be done with
  this material and move it into a more specific quarantined material queue.
  The _Initial Quarantine_ queue acts similarly to the TODO column on the task
  board. Most of the time the _Initial Quarantine_ queue should be empty; if
  there is anything in the _Initial Quarantine_ queue, it signals to the supervisor
  that a decision on the material is required and once that decision is made,
  the material is moved to a more specific quarantine queue, emptying the
  _Initial Quarantine_ queue.

- In-Progress quarantine queues: each potential action on the material should have
  a corresponding in-progress quarantined queue. For example,
  _Rework on Standalone Machines_, _Sent to Foundry_,
  _Waiting for Customer Inspection_, etc. FMS Insight allows notes to be attached
  to each piece of material, so you can keep a log or status on each piece of
  material. Material can move between one or more of these queues while it is quarantined.

- Done queues: There are no specific done queues (unlike Kanban task management);
  instead, if the material is ready to be re-introduced into the normal workflow
  of the automated handling system, it can be moved back into an "active" queue.
  If instead the material is scrap, it can be removed from the quarantined queues.

![Screenshot of Material screen](assets/insight-operations-material.png)
