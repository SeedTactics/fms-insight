---
title: FMS Quality Daily Monitoring
nav: FMS Insight Client > Quality
description: >-
  The FMS Insight quality pages are used by quality engineers.
  These pages display information about the past few days of inspections,
  allow the lookup of data about a specific serial, and search for similar
  material using the same machines or pallets.
---

# FMS Insight Quality Pages

The quality analysis page is intended for the quality engineers. These tabs display information about
the past few days of inspections and cell operation and allow the engineers visibility into the cell's operation.
(Anything older than a couple days should not be analyzed in the heat of the moment but
instead be addressed in a [monthly review](improve-fms).)

We suggest that the quality dashboard is bookmarked by the engineers and visited directly.
There is also a tab for searching for parts similar to a failed part and a tab which displays recent
inspections.

## Dashboard

The dashboard is intended to be kept open by the quality engineers to monitor
the cell. It displays all inspections marked as failed in the past 5 days.
(An inspection is marked as failed by an operator on the [inspection station
monitor page](client-station-monitor).) Clicking on the three dots icon
in each row will open more details about the part.

![Screenshot of quality dashboard](screenshots/insight-quality-dashboard.png)

## Failed Part Lookup

The _Failed Part Lookup_ tab allows an engineer to lookup the specific information about a part by serial and also
search for similar paths. This is done in three steps:

1. First, the serial for a part is manually entered or [scanned](client-scanners).

2. The details for this specific serial is loaded and displayed. FMS Insight also extacts the path and time that
   machining completed.

3. Similar paths are displayed. FMS Insight initially searches for all paths for this part within 5 days of the time that
   machining completed. The data is grouped by inspection type and path, allowing other parts signaled for inspections
   to be seen. **In order to see any data, a specific inspection type must be selected!**

![Screenshot of similar paths](screenshots/insight-quality-similar-paths.png)

Initially, only 5 days around the completed time are loaded (in the above screenshot, the range is from 4/4/2019
to 4/14/2019.) The arrow buttons next to the dates in the bottom right corner can be used to load additional days and
extend the range, to perhaps find more parts.

We suggest copying data to the clipboard and pasting it in a spreadsheet to provide a checklist to quarantine
parts.

## Paths

The _Paths_ tab shows all paths from the last week, grouped by inspection type and part. It shows the data either via
a Sankey chart or a table and shows signaled, succeeded, and failed inspections. It can be used to visualize
the paths that have occurred recently via the Sankey diagram or investigating the paths in detail via the table.
Anything older than 7
days is available as part of the [monthly review](improve-fms) on the
[flexibility analysis page](client-flexibility-analysis).

![Screenshot of path sankey](screenshots/insight-quality-recent-sankey.png)

## Quarantine Material

If [quarantined material](material-quarantine) is enabled, this page displays all the quarantined material queues.
Material can be moved between the various quarantine queues by clicking and dragging on the vertical dots
on the material card. In addition, notes can be added to each piece of material by opening the material card.

![Screenshot of Material screen](screenshots/insight-quality-quarantine.png)
