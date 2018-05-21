---
id: overview
title: Overview
sidebar_label: Overview
---

FMS Insight is a progam which runs on an flexible machining system (FMS)
cell controller which provides:

* A server which stores a log of all events that occur in the cell.
* A server which allows planned jobs to be sent into the cell controller.
* An HTML client which displays a dashboard, station monitor, and data analysis
  based on the log of events, planned jobs, and current cell status.

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

Finally, FMS Insight serves an HTTP client with the following features:

* Dashboard: The dashboard provides an overview of the current jobs and their
  progress, current machine status, and other data.
* Station Monitor:  The station monitor is intended to be viewed at various operator
  stations throughout the factory and provides information to the operator on what
  actions to take.  For example, at the load station it shows what to load and unload and
  at the inspection stand it shows what inspections were triggered.
* Efficiency Report:  The efficiency report shows various charts and data analysis of the
  event log.  These charts are intended to help improve the efficiency of the cell.
* Part Cost/Piece:  The part cost/piece report shows a calculated cost/piece based on the
  event log.  The cost/piece is intended to help validate that operational changes are
  actually improvements.