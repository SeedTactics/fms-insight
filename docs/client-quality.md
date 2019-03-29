---
id: client-quality
title: Quality Daily Monitoring
sidebar_label: Quality
---

The quality analysis are intended for the quality engineers. These pages display information about
the past few days of inspections and cell operation and allow the engineers visibility into the cell's operation.
(Anything older than a couple days should not be analyzed in the heat of the moment but
instead be addressed in a [monthly review](improve-fms.md).)

We suggest that the quality dashboard is bookmarked by the engineers and visited directly.
There is also a tab for searching for parts similar to a failed part and a tab which displays recent
inspections.

# Dashboard

The dashboard is intended to be kept open by the quality engineers to monitor the cell. It displays all inspections
marked as failed in the past 5 days.
(An inspection is marked as failed by an operator on the [inspection station monitor page](client-station-monitor.md).)

# Failed Part Lookup

The _Failed Part Lookup_ tab allows an engineer to lookup the specific information about a part by serial and also
search for similar paths. This is done in three steps:

1. First, the serial for a part is entered or [scanned](client-scanners.md).

2. The details for this specific serial is loaded and displayed. FMS Insight also extacts the path and time that
   machining completed.

3. Similar paths are displayed. FMS Insight searches for all paths for this part within 5 days of the time that
   machining completed. The data is grouped by inspection type and path, allowing other parts signaled for inspections
   to be seen.

The resulting data can be copied to the clipboard and pasted in a spreadsheet to provide a checklist to quarantine
parts.

# Paths

The _Paths_ tab shows all paths from the last week, grouped by inspection type and part. It shows the data either via
a Sankey chart or a table and shows signaled, succeeded, and failed inspections.
