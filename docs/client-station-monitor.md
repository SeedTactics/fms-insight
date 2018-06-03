---
id: client-station-monitor
title: Station Monitor
sidebar_label: Stations
---

The station monitor screen displays the [virtual whiteboard of material
sticky notes](material-tracking.md). Each in-process piece of material is
represented by a virtual sticky note. On the sticky note, FMS Insight
displays the part name, serial, assigned workorder, and any signaled
inspections. The sticky note can be clicked or tapped to open a dialog with
more details about the material including the log of events. The dialog also
allows the operator to notify Insight of changes in the material, such as
completing wash or assigning a workorder. Finally, the sticky note contains
an identicon based on the part name and the final number/letter of the
serial.

## Load Station

![Screenshot of Load Station screen](/docs/assets/insight-load-station.jpg)

On the top toolbar, the specific load station number is set.  Insight will display
only regions relevant to this specific load station, including the raw material region,
the faces of the pallet currently at the load station, and a region for completed material.
Optionally, the queues dropdown on the top toolbar can be used to add one to three virtual
whiteboard regions to display in addition to the pallet regions.  Typically we suggest that
in-process queues have their own computer with a dedicated display, but for queues closely
associated with the load station such as a transfer stand, the virtual whiteboard region for
the queue can be displayed along with the pallet regions.

When a material sticky note is clicked or tapped, a dialog will open with a log of events for the
piece of material.  In the dialog, a serial can be entered or changed, the part can be
marked for inspection, and optionally assigned to a workorder.  The server has a
[configuration value](server-config.md) called "Workorder Assignment" that determines
where workorders are assigned.  If the configuration value is set to "AssignWorkorderAtUnload",
the dialog will contain a button allowing the workorder to be assigned.

## Queues

## Inspection

![Screenshot of Inspection Station screen](/docs/assets/insight-inspection-monitor.jpg)

The inspection screen shows completed material that has been marked for inspection.  On the top
toolbar, a specific inspection type can be selected or all material for inspection can be shown.
On the left is the virtual whiteboard region for completed but not yet inspected material and on
the right is material which has completed inspections.  Insight only displays material completed
within the last 36 hours.

When a material sticky note is clicked or tapped, a dialog will open with a
log of events for the piece of material. If a specific inspection type is
selected, there will be buttons to mark a piece of material as either
successfully inspected or failed. When clicked or tapped, this will record an
event in the log and move the virtual sticky note from the left to the right.
The top toolbar on the right allows an operator name to be entered and this
operator name will be attached to the created inspection completed log entry.

Finally, an attached barcode scanner can also be used to open the material dialog.  This allows viewing
details and marking as inspected or uninspected any part by scanned serial.

## Wash

The wash screen shows completed material from the last 36 hours. On the left
is the virtual whiteboard region for completed but not yet washed material
and on the right is material which has completed final wash.

When a material sticky note is clicked or tapped, a dialog will open with a
log of events for the piece of material. There is a button to mark a piece of
material as completing wash. When clicked or tapped, this will record an
event in the log and move the virtual sticky note from the left to the right.
The top toolbar on the right allows an operator name to be entered and this
operator name will be attached to the created inspection completed log entry.

The server has a
[configuration value](server-config.md) called "Workorder Assignment" that determines
where workorders are assigned.  If the configuration value is set to "AssignWorkorderAtWash",
the dialog will contain a button allowing the workorder to be assigned.

Finally, an attached barcode scanner can also be used to open the material dialog.  This allows viewing
details and marking as inspected or uninspected any part by scanned serial.

## All Material

![Screenshot of All Material screen](/docs/assets/insight-all-material.jpg)

The All Material screen displays all virtual whiteboard regions.  This includes the regions for all pallets
and all configured in-process queues.  This is primarily indended to help visualize and debug the current state
of the whole cell; typically at specific stations during normal operation one of the other screens which focuses
on only a section of the virtual whiteboard is more appropriate.