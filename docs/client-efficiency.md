---
title: FMS Monthly Flexibility Analysis
nav: FMS Insight Client > Flexibility Analysis
description: >-
  The FMS Insight flexibility analysis pages are used by managers to view
  a monthly report on the system and to focus on continuous improvement.
  The pages highlight potential bottlenecks and places for efficiency improvements
  in the cell.
---

# FMS Insight Monthly Flexibility Analysis

FMS Insight supports continuous improvement by assisting with a monthly review.
We suggest that approximately once a month, all stakeholders review the operation of the cell and
decide on potential improvements. The [improve an FMS](improve-fms)
documentation goes into more details about how to make the most of these
efficiency calculations.

![Screenshot of choosing analysis month](screenshots/insight-choose-analysis-month.png)

At the top of the page are two radio buttons allowing you to analyze either the last
30 days or a specific calendar month.

The best metric for continuous improvement of an FMS is not cost/piece but
instead is the bottlenecks and utilization of system resources (the
goal is to produce more stuff with the same machines and quality). The
efficiency tab shows some charts and graphs for a monthly summary of the
operation of the cell, displaying reports that in our experience are very
helpful to find and fix bottlenecks. We suggest you review this data once a
month and use these reports to gradually alter the flexibility or operation
of the cell to improve the performance.

## Buffer Occupancy

![Screenshot of buffer occupancy chart](screenshots/insight-buffer-occupancy.png)

As material moves through the system, it buffers before various operations. It can buffer
on a pallet waiting on the inbound rotary table, it can buffer on a pallet in the
stocker waiting for either machining or unloading, or it can buffer between processes
in a [transfer queue](material-tracking). In general, material will buffer right in
front of the bottleneck operation. The buffer occupancy chart can thus be used to determine
which operation (machining, loading, unloading) is the current bottleneck and also how the
bottleneck changes over time.

The buffer occupancy chart calculates a moving average of the quantity of material buffered
in all these various places. The x-axis is the days of the month. For each time on
the x-axis, a window around that point is used to calculate the average quantity of material
in the buffer during the window of time. This average quantity is graphed on the y-axis.

Using a moving average window smooths out the jitter from individual pallet moves,
for example when a pallet rotates into the machine and another pallet is quickly
sent to the inbound rotary. The size of the window can be controlled by the slider
in the top-right. The size of the window should be set so that the major trends are
visible while short oscillations are smoothed out.

In an efficient, well-running system the bottleneck is always machining. This will be
reflected in the buffer occupancy chart with the "Stocker[Waiting for unload]" line zero
or almost zero, the "Rotary" for each machine always above zero, the
"Stocker[Waiting for machining]" only positive if all rotary tables are full, and any
in-process transfer queues small. If instead the load stations become the bottleneck,
the buffer occupancy chart will show a rise either "Stocker[Waiting for unload]" or a
rise in the in-process transfer queue between processes. Also, the rotary buffer
occupancy will drop to zero.

## Station Use

![Screenshot of Station Use Heatmap](screenshots/insight-analysis-station-oee.png)

The Station Use heatmap shows the station usage over the month. On the x-axis
are the days of the month and on the y-axis are the machines and load
stations. There are three charts which can be selected in the top-right
corner: "Standard OEE", "Planned OEE", and "Occupied".

- The "Standard OEE" chart computes the actual overall equipment effectiveness (OEE) over the month.
  For each station and each day, FMS Insight uses the log of parts produced and adds up the expected
  operation time for each part cycle and then divides by 24 hours
  to obtain a percentage that the station was busy with productive work. (If station cycles
  were longer than expected, this extra time is not counted in the OEE.) For each grid cell in the chart, the OEE percentage is drawn
  with a color with darker colors higher OEE and lighter colors lower OEE. A grid cell
  can be moused over to obtain extra information in a tooltip.

- The "Planned OEE" chart displays the simulated station usage for the downloaded jobs and
  is the prediction of what the OEE should look like based on all the schedules for the month.

- The "Occupied" chart computes the total percentage of time that a pallet is occupying the station. For
  each station and each day, FMS Insight uses the log of events to determine when a pallet arrives
  and departs from each station. The total time a pallet is at the station is then divided by 24 hours
  to obtain a percentage that the station was occupied. These percentages are then charted with darker colors
  higher occupancy.

The Station Use heatmaps are useful for several observations. First, the Occupied heatmap can be used to
determine the overall usage of the load station. If the load station occupied percentages are very high,
it can indicate that the load station is a bottleneck and preventing the cell from performing useful work.
In a healthy cell, the load stations should be faster than the machines,
so the load stations should be empty at least part of the time waiting for machining to finish.

The difference between the Standard OEE and Occupied heatmaps can be useful to determine if there is a lot
of cycle interruptions. For example, if a program is expected to take 25 minutes but spends 40 minutes at
the machine, the Standard OEE heatmap will be credited with 25 minutes and the Occupied heatmap will be
credited with 40 minutes, causing the Occupied heatmap to be darker in color for that day. Now, cycle
interruptions do occasionally happen; comparing the Standard OEE and Occupied heatmaps allows you to
determine if these are one-off events or a consistent problem. If most days are darker on the Occupied
heatmap, the cycle interruptions should be investigated in more detail. (For example, look at individual parts on the
machine cycle charts, ensure the expected time entered into the schedule is actually correct, investigate
machine maintenance records, etc.)

Finally, the Standard OEE heatmap helps visualize how balanced the machines were loaded over the month.
We want to see all the machines consistently roughly the same color. If you see that
a machine has a lighter color for a couple days, that indicates either the machine was down or
that the daily mix for that day did not have enough flexibility. You should then consider
picking a part and extending that part to run on the lightly loaded machine. To find such a
part, you can use the part production chart below to see which part mix was run on this day
to help find a part that might be changed to run on the lightly loaded machine.

## Part Production

![Screenshot of Part Production Heatmap](screenshots/insight-analysis-part-completed.png)

The Part Production heatmap shows the distribution of completed parts over
the month. On the x-axis are the days of the month and on the y-axis are the
part types. For each part and each day, FMS Insight counts how many parts
were produced that day. For each grid cell in the chart, the entry
is drawn as a color with darker colors higher machine hours and lighter colors lower
machine hours. A grid cell can be moused over to obtain extra information in a
tooltip.

The part production OEE heatmap is mainly useful to visualize the part mix as it
varies throughout the month, by comparing the relative color shades. Also, it can help
find a part to change move onto a lightly loaded machine. For example, consider that a machine
is found to be lightly loaded via the station OEE heatmap. That same day can be viewed on
the part production OEE heatmap and the darkest colored part was the highest run that day and
could be considered to be extended to be run on the lightly loaded machine.

Note that these heatmaps should only be used to brainstorm ideas. We would still
to investigate if expanding `yyy` to include machine 2 would increase overall
system performance. Are there enough pallets? How many extra inspections are required?
Will this cause a traffic jam? These questions can be answered using simulation, _SeedTactic: Designer_,
Little's Law, or a tool such as our [SeedTactic: Planning](https://www.seedtactics.com/features#seedtactic-planning).
