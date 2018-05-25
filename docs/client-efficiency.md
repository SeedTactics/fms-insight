---
id: client-efficiency
title: Efficiency
sidebar_label: Efficiency
---

The efficiency tab shows some charts and graphs for a monthly summary of the
operation of the cell.  These reports are designed to expose potential inefficiencies
and problems, allowing you to gradually alter the flexibility or operation of the cell
to improve performance.  We suggest you review the efficiency data once per month.
At the top of the tab, you can choose to view either the data from the past 30 days or view
data for a specific month.

## Station Cycles

![Screenshot of station cycle chart](assets/insight-station-cycle.jpg)

Select a part from the combo box in the top-right. Once selected, all load
and machine cycles for the part will be displayed. The x-axis is the days of
the month and the y-axis is cycle time in minutes. The cycle time is the wall
clock time between cycle start and cycle end of the machine or the wall clock
time between a pallet arriving at the load station to the pallet leaving the
load station. The legend at the bottom shows which colors coorespond to which
stations, and by clicking on stations in the legened you can enable or
disable the viewing of specific stations. Finally, by clicking on a point you
can obtain details about that specific cycle in a tooltip.

The example screenshot above is an example of a part program which might need
improvement. We see that most of the machine cycles are fine at around 40
minutes, but there are quite a few machine cycles longer than 40 minutes
which likely come from program interruptions, tool problems, etc. and since
there are so many it is likely worth it to investigate and improve the part
program. On the other hand, if there were only a couple of machine cycles
longer than 40 minutes, we might instead conclude that it is not worth the
effort to spend a large amount of time focusing on this part. Finally, the
load station cycles seem consistent at between 5 and 15 minutes so likely no
improvements are required at the load station.  If instead there were a
large number of outlier load/unload times, we might spend some time investigating
and potentially making operational changes.  By periodically viewing this
cycle chart for each part, you can get a feel for your specific system
and iteratively detect and fix problems.

## Pallet Cycles

![Screenshot of pallet cycle chart](assets/insight-pallet-cycles.jpg)

Select a pallet from the combo box in the top-right. Once selected, all
pallet cycles are displayed. The x-axis is the days of the month and the
y-axis is the pallet cycle time in minutes. A pallet cycle time is the wall
clock time from when a pallet leaves the load station to the next time it
leaves the load station. This will therefore include cart transfer time,
buffer time, machining time, and load/unload time.  By clicking on a point, you
can obtain more information about the pallet cycle in a tooltip.

## Station OEE

![Screenshot of Station OEE Heatmap](assets/insight-station-oee.jpg)

## Part Production

![Screenshot of Station OEE Heatmap](assets/insight-part-production.jpg)

## Inspections

![Screenshot of Inspection Sankey](assets/insight-inspection-sankey.jpg)