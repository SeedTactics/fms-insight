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

We suggest that computers or mounted tablets be placed next to various stations
in the factory, perhaps with an attached barcode scanner.
The computer or mounted tablet can then be configured to open the specific screen
for the station by either bookmarking the page or just setting the specific page
as the homepage for the browser.

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

![Screenshot of Load Station Material Dialog](/docs/assets/insight-loadstation-details.jpg)

When a material sticky note is clicked or tapped, a dialog will open with a log of events for the
piece of material.  In the dialog, a serial can be entered or changed, the part can be
marked for inspection, assigned to a workorder, and [load instructions](part-instructions.md) can
be opened.
An attached [barcode scanner](client-scanners.md) can also be used to open the material
dialog.

## Queues

![Screenshot of Queues Screen](/docs/assets/insight-queues.jpg)

The queues screen shows the material currently inside one or more queues.  On the top toolbar,
one or more queues can be selected and the virtual whiteboard regions for the selected queues
are then displayed. The queue screen allows the operator to edit the material in the queue.
To add new material to the queue, click the plus icon button in the top-right of the queue
virtual whiteboard region.  This will bring up a dialog allowing you to enter a serial,
scan a barcode, or manually select a job for the added material.

![Screenshot of Queue Material Dialog](/docs/assets/insight-queue-details.jpg)

By clicking or tapping on a material sticky note, a dialog will open with
details about the specific piece of material. The dialog will have a button
at the bottom labeled "Remove From Queue".
An attached [barcode scanner](client-scanners.md) can also be used to open the material
dialog.

## Inspection

![Screenshot of Inspection Station screen](/docs/assets/insight-inspection-monitor.jpg)

The inspection screen shows completed material that has been marked for inspection.  On the top
toolbar, a specific inspection type can be selected or all material for inspection can be shown.
On the left is the virtual whiteboard region for completed but not yet inspected material and on
the right is material which has completed inspections.  Insight only displays material completed
within the last 36 hours.

![Screenshot of Inspection Station Material Dialog](/docs/assets/insight-inspection-details.jpg)

When a material sticky note is clicked or tapped, a dialog will open with a
log of events for the piece of material. If a specific inspection type is
selected, there will be buttons to mark a piece of material as either
successfully inspected or failed. When clicked or tapped, this will record an
event in the log and move the virtual sticky note from the left to the right.
The top toolbar on the right allows an operator name to be entered and this
operator name will be attached to the created inspection completed log entry.
Finally, the operator can open [inspection instructions](part-instructions.md).

An attached [barcode scanner](client-scanners.md) can also be used to open the material
dialog. This allows viewing details and marking as inspected or uninspected
any part by scanned serial (even if the part is not on the screen because more than 36 hours has passed).

## Wash

![Screenshot of Wash Screen](/docs/assets/insight-wash.jpg)

The wash screen shows completed material from the last 36 hours. On the left
is the virtual whiteboard region for completed but not yet washed material
and on the right is material which has completed final wash.

![Screenshot of Wash Material Details](/docs/assets/insight-wash-details.jpg)

When a material sticky note is clicked or tapped, a dialog will open with a
log of events for the piece of material. There is a button to mark a piece of
material as completing wash. When clicked or tapped, this will record an
event in the log and move the virtual sticky note from the left to the right.
The top toolbar on the right allows an operator name to be entered and this
operator name will be attached to the created wash completed log entry.
Finally, the operator can open [wash instructions](part-instructions.md).

An attached [barcode scanner](client-scanners.md) can also be used to open the material
dialog. This allows viewing details and marking as wash completed
any part by scanned serial (even if the part is not on the screen because more than 36 hours has passed).

## All Material

![Screenshot of All Material screen](/docs/assets/insight-all-material.jpg)

The All Material screen displays all virtual whiteboard regions.  This includes the regions for all pallets
and all configured in-process queues.  This is primarily intended to help visualize and debug the current state
of the whole cell; typically at specific stations during normal operation one of the other screens which focuses
on only a section of the virtual whiteboard is more appropriate.
