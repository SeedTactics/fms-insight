---
id: overview
title: Overview
sidebar_label: Overview
---

FMS Insight is a program which runs on an flexible machining system (FMS)
cell controller which has the following goals:

- Track material by barcode serial throughout the manufacturing process, tracking
  the material through both automated handling and manual handling. The documentation on
  [material tracking](material-tracking.md) goes into more detail on the goals and challenges
  of tracking. The log of all material is stored in a database.
- Use the material tracking to help the operators on the shop floor by providing instructions
  and data displays, for example to provide the inspection operator with data about the
  specific part being inspected. FMS Insight provides a [HTML client](client-launch.md)
  optimized for touchscreen use.
- Disseminate information about the daily cell operation throughout the factory. FMS Insight provides targeted pages
  for engineering, quality control, supervisors, scheduling, and management.
- Facilitate continuous improvement and cost reduction via analytics. FMS Insight assists in a monthly review
  of the cell by display metrics and graphs to help improve the cost/piece and efficiency
  of the FMS. The documentation on [improving an FMS](improve-fms.md) discusses in more
  detail how analytics can be used to improve the cost/piece of a cell.
- Simplify job/plan data creation in the cell controller to prevent traffic jams. Too many
  jobs with too much demand in an automated system leads to pallet traffic jams just like too many
  cars on the road lead to traffic jams. When job/plan data creation inside the cell controller
  is simple and painless, it is much easier to rate-limit the flow of demand into the system
  to prevent traffic jams.

## Server

To achive these objectives, FMS Insight provides a server which runs on the cell controller
and stores a log of all events that occur in the cell and allows planned jobs to be sent into the cell controller.

The FMS Insight server maintains a log of all
events. These events include machine cycle start, machine cycle end, load
start, load end, pallet movements, inspections, serial assignment, wash, and
others. The server also maintains a log of planned jobs; each planned job is
stored with its date, part name, planned quantities, flexibility, and other
features of the job. The immutable log of events and planned jobs is used to
drive all decisions and analysis of the cell.

The second core feature of the FMS Insight server is to translate planned
jobs into the cell controller. The FMS Insight server does not implement any
complex logic or control itself; instead FMS Insight is a generic conduit
which allows other software to communicate jobs into the cell controller.
Since each project and ERP is different, FMS Insight does not have a way of
creating the jobs (other, customized software such as
[SeedTactic: OrderLink](https://www.seedtactics.com/products/seedtactic-orderlink) is required).
Nevertheless, FMS Insight eases this task by defining a common high-level job
description and implementing the messy details of converting to the specific
cell controller format and creating the parts, pallets, jobs, and schedules.

## Client

FMS Insight provides a touchscreen optimized [HTML client](client-launch.md). Many people
throughout the factory interact with the cell in different ways. Thus, FMS Insight provides
specific pages targeted at specific users which display relevant information. We suggest that
each user bookmarks the relevant page; for example, engineers in the quality department should just
bookmark the quality page in FMS Insight, visiting it directly to monitor the cell. All FMS
Insight pages support a connected [barcode scanner](client-scanners.md) to scan the serial number
of a part.

##### Shop Floor

FMS Insight provides several [station monitor](client-station-monitor.md)
pages. These pages are intended to be viewed at various operator stations
throughout the factory and provide information to the operator on what
actions to take. For example, at the load station it shows what to load and
unload and at the inspection stand it shows what inspections were triggered.

##### Daily Monitoring

FMS Insight provides targeted pages to assist with the day-to-day operation of the cell.
There is a targeted page for [factory floor supervisors](client-operations.md), [engineering/programming](client-engineering.md),
and [quality control](client-quality.md).

##### Monthly Review

FMS Insight supports continuous improvement by assisting with a monthly review.
We suggest that approximately once a month, all stakeholders review the operation of the cell and
decide on potential improvements. FMS Insight provides a [flexibility analysis](client-flexibility-analysis.md)
page which displays a monthly summary of the cell, displays cost/piece, and helps identify areas for improvement.

FMS Insight also provides a [Backup Viewer](client-backup-viewer.md), which is a standalone program to
view data directly historical backups.
